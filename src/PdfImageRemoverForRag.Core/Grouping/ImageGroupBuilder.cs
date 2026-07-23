using PdfImageRemoverForRag.Core.Models;

namespace PdfImageRemoverForRag.Core.Grouping;

/// <summary>
/// Reduces a flat list of <see cref="ImageDiscovery"/> records into
/// <see cref="PdfImageGroup"/> instances by stream SHA-256 (spec §10).
/// Keeping this logic in Core — separated from the PDFsharp-dependent
/// analyzer — is what makes grouping unit-testable without touching a real
/// PDF (spec §24 "同一画像のグループ化").
/// </summary>
public sealed class ImageGroupBuilder
{
    readonly FullPageImageDetector _fullPageDetector;

    public ImageGroupBuilder(FullPageImageDetector fullPageDetector)
    {
        _fullPageDetector = fullPageDetector;
    }

    /// <summary>
    /// Group the discoveries. Image groups sort before text groups; within
    /// each kind, descending usage count so the noisiest repeating object (a
    /// logo or header on every page) shows first, matching the spec's mock-up
    /// (§11.1). IDs are assigned after the sort — <c>IMG_001…</c> for images,
    /// <c>TXT_001…</c> for text — so the id order mirrors the display order.
    /// </summary>
    public IReadOnlyList<PdfImageGroup> Build(IEnumerable<ImageDiscovery> discoveries)
    {
        var sorted = discoveries
            .GroupBy(d => d.StreamHash, StringComparer.Ordinal)
            .Select(BuildGroup)
            .OrderBy(g => g.Kind)
            .ThenByDescending(g => g.UsageCount)
            .ThenBy(g => g.Hash, StringComparer.Ordinal)
            .ToArray();
        return AssignGroupIds(sorted);
    }

    /// <summary>Assign sequential per-kind ids in the already-sorted order.</summary>
    internal static PdfImageGroup[] AssignGroupIds(IReadOnlyList<PdfImageGroup> sorted)
    {
        int imageIndex = 0, textIndex = 0, shapeIndex = 0;
        var result = new PdfImageGroup[sorted.Count];
        for (int i = 0; i < sorted.Count; i++)
        {
            var group = sorted[i];
            var id = group.Kind switch
            {
                RemovableKind.Text => $"TXT_{++textIndex:D3}",
                RemovableKind.Shape => $"SHP_{++shapeIndex:D3}",
                _ => $"IMG_{++imageIndex:D3}",
            };
            result[i] = group with { GroupId = id };
        }
        return result;
    }

    PdfImageGroup BuildGroup(IGrouping<string, ImageDiscovery> bucket)
    {
        // All discoveries in a bucket share the same underlying stream bytes,
        // so scalar metadata can be read from any element; occurrences are
        // unioned across the bucket.
        var first = bucket.First();
        var occurrences = bucket.SelectMany(d => d.Occurrences).ToArray();

        // Safety is ANDed across the bucket: if ANY placement is unsafe
        // (e.g. inside a shared Form XObject), removing the group would mean
        // deleting the safe placements while the unsafe ones survive, which
        // the spec forbids (§14.3) — so the whole group becomes unsafe.
        var unsafeDiscovery = bucket.FirstOrDefault(d => !d.IsSafelyRemovable);

        return new PdfImageGroup(
            GroupId: "IMG_000", // placeholder — real id assigned after sorting in Build
            Hash: bucket.Key,
            PixelWidth: first.PixelWidth,
            PixelHeight: first.PixelHeight,
            ColorSpace: first.ColorSpace,
            BitsPerComponent: first.BitsPerComponent,
            Compression: first.Compression,
            EstimatedSize: bucket.Sum(d => d.StreamByteCount),
            IsImageMask: first.IsImageMask,
            IsPossibleFullPageImage: _fullPageDetector.AnyIsPossibleFullPage(occurrences),
            IsSafelyRemovable: unsafeDiscovery is null,
            WarningMessage: unsafeDiscovery?.UnsafeReason,
            ThumbnailBytes: bucket.Select(d => d.ThumbnailBytes).FirstOrDefault(t => t is not null),
            Occurrences: occurrences,
            Kind: first.Kind,
            TextValue: first.TextValue,
            ShapeGeometry: first.ShapeGeometry);
    }
}
