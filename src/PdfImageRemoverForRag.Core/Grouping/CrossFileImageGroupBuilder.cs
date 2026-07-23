using PdfImageRemoverForRag.Core.Models;

namespace PdfImageRemoverForRag.Core.Grouping;

/// <summary>
/// Merges the per-file <see cref="PdfImageGroup"/> lists of every open
/// document into <see cref="CrossFileImageGroup"/>s keyed by stream SHA-256,
/// so identical images shared across files show as one row and can be
/// removed with a single selection.
/// </summary>
public static class CrossFileImageGroupBuilder
{
    /// <summary>
    /// Merge and sort. Same ordering contract as the single-file builder:
    /// descending total usage first, then hash for stability, with
    /// <c>IMG_001</c>-style ids assigned after the sort so ids always match
    /// the display order.
    /// </summary>
    public static IReadOnlyList<CrossFileImageGroup> Build(
        IEnumerable<(string FilePath, IReadOnlyList<PdfImageGroup> Groups)> documents)
    {
        // Bucket per-file groups by hash, preserving which file each came from.
        var byHash = new Dictionary<string, List<(string FilePath, PdfImageGroup Group)>>(StringComparer.Ordinal);
        foreach (var (filePath, groups) in documents)
        {
            foreach (var group in groups)
            {
                if (!byHash.TryGetValue(group.Hash, out var bucket))
                {
                    bucket = new List<(string, PdfImageGroup)>();
                    byHash[group.Hash] = bucket;
                }
                bucket.Add((filePath, group));
            }
        }

        var sorted = byHash
            .Select(kv => BuildOne(kv.Key, kv.Value))
            .OrderBy(g => g.Kind)
            .ThenByDescending(g => g.UsageCount)
            .ThenBy(g => g.Hash, StringComparer.Ordinal)
            .ToArray();

        // Kind-aware sequential ids (IMG_ / TXT_ / SHP_) matching display order.
        int imageIndex = 0, textIndex = 0, shapeIndex = 0;
        var result = new CrossFileImageGroup[sorted.Length];
        for (int i = 0; i < sorted.Length; i++)
        {
            var id = sorted[i].Kind switch
            {
                RemovableKind.Text => $"TXT_{++textIndex:D3}",
                RemovableKind.Shape => $"SHP_{++shapeIndex:D3}",
                _ => $"IMG_{++imageIndex:D3}",
            };
            result[i] = sorted[i] with { GroupId = id };
        }
        return result;
    }

    static CrossFileImageGroup BuildOne(string hash, List<(string FilePath, PdfImageGroup Group)> bucket)
    {
        // Identical stream bytes → identical image metadata; read scalars
        // from the first file's group.
        var first = bucket[0].Group;

        // Safety is ANDed across files, exactly like it is ANDed across
        // placements within one file (§14.3): if the image sits inside a
        // shared Form XObject in ANY file, one checkbox would remove the
        // safe placements while the unsafe ones survive — so the whole
        // cross-file group is unsafe.
        var unsafeGroup = bucket
            .Select(x => x.Group)
            .FirstOrDefault(g => !g.IsSafelyRemovable);

        return new CrossFileImageGroup(
            GroupId: "IMG_000", // placeholder — assigned after sorting in Build
            Hash: hash,
            PixelWidth: first.PixelWidth,
            PixelHeight: first.PixelHeight,
            ColorSpace: first.ColorSpace,
            BitsPerComponent: first.BitsPerComponent,
            Compression: first.Compression,
            EstimatedSize: bucket.Sum(x => x.Group.EstimatedSize),
            IsImageMask: first.IsImageMask,
            IsPossibleFullPageImage: bucket.Any(x => x.Group.IsPossibleFullPageImage),
            IsSafelyRemovable: unsafeGroup is null,
            WarningMessage: unsafeGroup?.WarningMessage,
            ThumbnailBytes: bucket.Select(x => x.Group.ThumbnailBytes).FirstOrDefault(t => t is not null),
            FileOccurrences: bucket
                .Select(x => new CrossFileOccurrences(x.FilePath, x.Group.Occurrences))
                .ToArray(),
            Kind: first.Kind,
            TextValue: first.TextValue,
            ShapeGeometry: first.ShapeGeometry);
    }
}
