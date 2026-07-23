using PdfImageRemoverForRag.Core.Models;

namespace PdfImageRemoverForRag.App;

/// <summary>
/// The bitmaps currently on screen, and nothing else.
///
/// This used to hold one grid bitmap and one tile bitmap for every object in
/// the workspace, built up front. That does not survive real documents: a
/// 2,000-object file at 200 % display scale needed well over a gigabyte of
/// tile bitmaps alone, and the count grows with every file the user opens.
///
/// Now the bitmaps live on disk (<see cref="ThumbnailStore"/>) and this class
/// keeps only the window the user can actually see — <see cref="Retain"/>
/// disposes everything outside it. Memory is therefore bounded by the size of
/// the viewport, not by the size of the document.
/// </summary>
internal sealed class ThumbnailCache : IDisposable
{
    readonly Dictionary<string, Image> _grid = new(StringComparer.Ordinal);
    readonly Dictionary<string, Image> _tile = new(StringComparer.Ordinal);
    readonly ThumbnailStore _store;
    readonly Size _gridSize;
    Size _tileSize;

    public ThumbnailCache(ThumbnailStore store, Size gridSize, Size tileSize)
    {
        _store = store;
        _gridSize = gridSize;
        _tileSize = tileSize;
    }

    /// <summary>How many bitmaps of each size are held right now — for the log.</summary>
    public (int Grid, int Tile) ResidentCounts => (_grid.Count, _tile.Count);

    /// <summary>What one load pass actually did, as opposed to what it tried.</summary>
    public readonly record struct LoadResult(int Attempted, int Rendered, int Failed);

    /// <summary>The grid-sized bitmap for this object, or null if not loaded.</summary>
    public Image? Grid(string hash) => _grid.GetValueOrDefault(hash);

    /// <summary>The tile-sized bitmap for this object, or null if not loaded.</summary>
    public Image? Tile(string hash) => _tile.GetValueOrDefault(hash);

    /// <summary>
    /// True when this object cannot be rendered at all (an image format we do
    /// not decode, a shape with no geometry). The views show a placeholder for
    /// these rather than saying a thumbnail is on its way.
    /// </summary>
    public bool IsUnrenderable(string hash) => _store.IsUnrenderable(hash);

    /// <summary>
    /// Change the tile bitmap size after a DPI change, dropping what was loaded
    /// at the old scale. Returns true when the size actually changed, i.e. when
    /// the caller must rebuild its tiles. The store keys its files by size, so
    /// the old renders stay on disk and cost nothing.
    /// </summary>
    public bool SetTileSize(Size tileSize)
    {
        if (_tileSize == tileSize) return false;

        _tileSize = tileSize;
        DisposeAll(_tile);
        return true;
    }

    /// <summary>
    /// Make sure every object in <paramref name="window"/> has both bitmaps
    /// rendered on disk and loaded into memory, and dispose everything outside
    /// the window.
    ///
    /// Rendering happens on a worker thread — it decodes and rescales, which is
    /// the expensive part — while loading and eviction happen on the caller's
    /// thread, so the returned bitmaps are safe to hand to controls. Returns
    /// how many objects were rendered this pass.
    /// </summary>
    public async Task<LoadResult> LoadWindowAsync(
        IReadOnlyList<CrossFileImageGroup> window,
        CancellationToken ct)
    {
        // Text draws its string; it never has a bitmap.
        var wanted = window.Where(g => g.Kind != RemovableKind.Text).ToList();

        // Render whatever the store is missing. Both sizes are produced in one
        // pass so that switching views never waits for a second round trip.
        var gridSize = _gridSize;
        var tileSize = _tileSize;
        var missing = wanted
            .Where(g => !_store.IsUnrenderable(g.Hash))
            .Where(g => !_store.HasRendered(g.Hash, ThumbnailStore.Kind.Grid, gridSize)
                     || !_store.HasRendered(g.Hash, ThumbnailStore.Kind.Tile, tileSize))
            .ToList();

        // Counted separately from the attempt count: "we tried 37" and "37
        // worked" are very different states, and only the second one means the
        // views will have anything to show.
        int rendered = 0;
        int failed = 0;
        if (missing.Count > 0)
        {
            (rendered, failed) = await Task.Run(() =>
            {
                int ok = 0, bad = 0;
                foreach (var group in missing)
                {
                    ct.ThrowIfCancellationRequested();
                    foreach (var (kind, size) in new[]
                             {
                                 (ThumbnailStore.Kind.Grid, gridSize),
                                 (ThumbnailStore.Kind.Tile, tileSize),
                             })
                    {
                        if (_store.HasRendered(group.Hash, kind, size)) continue;
                        if (_store.Render(group, kind, size)) ok++; else bad++;
                    }
                }
                return (ok, bad);
            }, ct).ConfigureAwait(true);
        }

        ct.ThrowIfCancellationRequested();

        // Load what is now on disk, then evict everything off-window. Eviction
        // is the whole point of this class — without it the cache grows back
        // into the unbounded thing it replaced.
        foreach (var group in wanted)
        {
            LoadInto(_grid, group.Hash, ThumbnailStore.Kind.Grid, _gridSize);
            LoadInto(_tile, group.Hash, ThumbnailStore.Kind.Tile, _tileSize);
        }
        Retain(wanted.Select(g => g.Hash));

        return new LoadResult(missing.Count, rendered, failed);
    }

    void LoadInto(Dictionary<string, Image> cache, string hash, ThumbnailStore.Kind kind, Size size)
    {
        if (cache.ContainsKey(hash)) return;

        var image = _store.TryLoad(hash, kind, size);
        if (image is not null) cache[hash] = image;
    }

    /// <summary>Dispose every bitmap whose object is not in <paramref name="hashes"/>.</summary>
    public void Retain(IEnumerable<string> hashes)
    {
        var keep = hashes.ToHashSet(StringComparer.Ordinal);
        RetainOne(_grid, keep);
        RetainOne(_tile, keep);

        static void RetainOne(Dictionary<string, Image> cache, HashSet<string> keep)
        {
            foreach (var (hash, image) in cache.Where(kv => !keep.Contains(kv.Key)).ToList())
            {
                image.Dispose();
                cache.Remove(hash);
            }
        }
    }

    /// <summary>Drop everything, e.g. when the workspace is closed.</summary>
    public void Clear()
    {
        DisposeAll(_grid);
        DisposeAll(_tile);
    }

    static void DisposeAll(Dictionary<string, Image> cache)
    {
        foreach (var image in cache.Values) image.Dispose();
        cache.Clear();
    }

    public void Dispose() => Clear();
}
