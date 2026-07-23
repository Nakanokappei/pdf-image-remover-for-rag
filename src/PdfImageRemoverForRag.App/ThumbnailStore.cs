using PdfImageRemoverForRag.Core.Models;

namespace PdfImageRemoverForRag.App;

/// <summary>
/// The on-disk home for everything the object list draws.
///
/// Nothing image-shaped is kept in memory any longer than it takes to paint it.
/// During analysis the source bytes of each unique image go straight to a file
/// named after its hash; the scaled bitmaps the two views need are rendered
/// from there on demand and cached as files as well. The result is that opening
/// a hundred large PDFs costs disk, not RAM — the workspace itself holds only
/// numbers and text.
///
/// Everything lives under one per-run folder that is deleted on exit. A run
/// that dies without cleaning up leaves its folder behind, so
/// <see cref="RemoveAbandonedSessions"/> sweeps the siblings at startup.
/// </summary>
internal sealed class ThumbnailStore : IDisposable
{
    /// <summary>Which of the two rendered sizes is wanted.</summary>
    public enum Kind
    {
        Grid,
        Tile,
    }

    readonly string _sessionFolder;

    public ThumbnailStore()
    {
        // One folder per run. The process id makes an abandoned folder
        // recognisable as belonging to a process that is no longer alive.
        _sessionFolder = Path.Combine(CacheRoot, $"session-{Environment.ProcessId}");
        Directory.CreateDirectory(_sessionFolder);
    }

    /// <summary>Where this run's files go. Logged at startup so a "nothing is
    /// being cached" report can be checked against the actual folder.</summary>
    public string Folder => _sessionFolder;

    static string CacheRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PdfImageRemoverForRag", "cache");

    /// <summary>
    /// Delete cache folders left behind by runs that are no longer alive.
    /// A crash cannot be relied on to clean up after itself, and these folders
    /// hold full-size image data, so they must not accumulate. Failures are
    /// ignored: a folder we cannot delete is a nuisance, never an error worth
    /// interrupting the user for.
    /// </summary>
    public static void RemoveAbandonedSessions()
    {
        try
        {
            if (!Directory.Exists(CacheRoot)) return;

            foreach (var folder in Directory.EnumerateDirectories(CacheRoot, "session-*"))
            {
                // Keep the folder if a live process still owns it.
                if (int.TryParse(Path.GetFileName(folder)["session-".Length..], out var processId)
                    && IsProcessAlive(processId))
                {
                    continue;
                }

                try { Directory.Delete(folder, recursive: true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    static bool IsProcessAlive(int processId)
    {
        // Our own id always counts as alive; for anything else, ask the OS.
        if (processId == Environment.ProcessId) return true;
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            // No such process — the folder is abandoned.
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>
    /// Stash one image's source bytes under its hash, unless they are already
    /// there. Called while the analyzer still has the PDF open, so the bytes go
    /// from the PDF to the disk without ever being collected in a dictionary.
    /// </summary>
    public void SaveSource(string hash, byte[] bytes)
    {
        var path = SourcePath(hash);
        if (File.Exists(path)) return;

        try
        {
            File.WriteAllBytes(path, bytes);
        }
        catch (IOException)
        {
            // A missing source degrades to the placeholder icon, exactly like
            // an image whose format we cannot decode. Never fail the open.
        }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>
    /// The rendered bitmap for one object at one size, or null when it has not
    /// been rendered yet. Reading it decodes a small PNG, so this is cheap
    /// enough to call while laying out a screenful.
    /// </summary>
    public Image? TryLoad(string hash, Kind kind, Size size)
    {
        var path = RenderedPath(hash, kind, size);
        if (!File.Exists(path)) return null;

        try
        {
            // Read through a byte array rather than Image.FromFile, which keeps
            // the file locked for the lifetime of the Image.
            using var stream = new MemoryStream(File.ReadAllBytes(path));
            return Image.FromStream(stream);
        }
        catch (IOException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            // Corrupt or truncated cache entry (e.g. killed mid-write).
            return null;
        }
    }

    /// <summary>True when the rendered bitmap for this object already exists.</summary>
    public bool HasRendered(string hash, Kind kind, Size size) =>
        File.Exists(RenderedPath(hash, kind, size));

    /// <summary>
    /// Render one object at one size and write it to the cache. Returns false
    /// when there is nothing to render — an unsupported image format, a shape
    /// without geometry — which the caller shows as a placeholder.
    ///
    /// Safe to call off the UI thread: GDI+ bitmaps are not thread-affine, and
    /// nothing here touches a control.
    /// </summary>
    public bool Render(CrossFileImageGroup group, Kind kind, Size size)
    {
        try
        {
            using var rendered = RenderCore(group, size);
            if (rendered is null)
            {
                // Formats we cannot decode (JPEG2000, CCITT, JBIG2) will fail
                // again on every pass, and a tile that keeps saying "building…"
                // is exactly the lie this whole mechanism exists to avoid. Mark
                // the object so it is shown as a placeholder and never retried.
                MarkUnrenderable(group.Hash);
                return false;
            }

            // Write to a temporary name and move into place, so a reader can
            // never observe a half-written PNG.
            var path = RenderedPath(group.Hash, kind, size);
            var partial = path + ".part";
            rendered.Save(partial, System.Drawing.Imaging.ImageFormat.Png);
            File.Move(partial, path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // GDI+ reports out-of-memory for allocations it cannot make, and a
            // cache write can fail for a dozen mundane reasons. One object
            // failing must never take the rest of the batch with it.
            return false;
        }
    }

    /// <summary>
    /// Produce the bitmap itself. Shapes are drawn from their geometry, which
    /// lives in memory because it is numbers; images are decoded from the
    /// source file this store wrote during analysis.
    /// </summary>
    Image? RenderCore(CrossFileImageGroup group, Size size)
    {
        if (group.Kind == RemovableKind.Text) return null;

        if (group.Kind == RemovableKind.Shape)
        {
            return group.ShapeGeometry is { } geometry
                ? ImageListRow.CreateShapeThumbnail(geometry, size.Width, size.Height)
                : null;
        }

        var sourcePath = SourcePath(group.Hash);
        if (!File.Exists(sourcePath)) return null;

        using var decoded = ImageListRow.DecodeThumbnail(File.ReadAllBytes(sourcePath));
        return decoded is null ? null : ImageListRow.CreateScaledCopy(decoded, size.Width, size.Height);
    }

    /// <summary>
    /// True when this object has already proved impossible to render, so the
    /// views should show a placeholder instead of promising one.
    /// </summary>
    public bool IsUnrenderable(string hash) => File.Exists(UnrenderablePath(hash));

    void MarkUnrenderable(string hash)
    {
        try { File.WriteAllBytes(UnrenderablePath(hash), Array.Empty<byte>()); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>
    /// Turn an object hash into something that can be a file name.
    ///
    /// Image hashes are plain hex, but shapes and text are prefixed —
    /// "SHAPE:…" and "TEXT:…" — and a colon is not a legal Windows file name
    /// character. It is read as an NTFS alternate-data-stream separator, so
    /// writing such a path fails silently-ish and reading it never finds
    /// anything: every shape simply had no thumbnail. Anything outside
    /// [0-9A-Za-z] becomes an underscore, which cannot collide because the
    /// rest of every hash is hex.
    /// </summary>
    static string SafeName(string hash) =>
        string.Create(hash.Length, hash, (destination, source) =>
        {
            for (int i = 0; i < source.Length; i++)
            {
                destination[i] = char.IsAsciiLetterOrDigit(source[i]) ? source[i] : '_';
            }
        });

    string SourcePath(string hash) => Path.Combine(_sessionFolder, $"{SafeName(hash)}.src");

    string UnrenderablePath(string hash) => Path.Combine(_sessionFolder, $"{SafeName(hash)}.none");

    /// <summary>
    /// The size goes in the file name so that a DPI change asks for different
    /// files rather than silently reusing bitmaps built at the old scale.
    /// </summary>
    string RenderedPath(string hash, Kind kind, Size size) =>
        Path.Combine(_sessionFolder,
            $"{SafeName(hash)}-{(kind == Kind.Grid ? "g" : "t")}{size.Width}x{size.Height}.png");

    /// <summary>Drop everything this run cached.</summary>
    public void Dispose()
    {
        try { Directory.Delete(_sessionFolder, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
