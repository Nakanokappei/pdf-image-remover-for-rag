namespace PdfImageRemoverForRag.Core.Models;

/// <summary>
/// A set of identical image placements — same underlying image bytes drawn
/// one or more times across a PDF. The user acts on groups, not on individual
/// occurrences: checking a group removes every placement of that image.
/// </summary>
/// <remarks>
/// The base fields (GroupId … Occurrences) match spec §9 exactly. The tool
/// adds two safety fields — <see cref="IsSafelyRemovable"/> and
/// <see cref="WarningMessage"/> — because §14.3 requires the UI to distinguish
/// images that can be cleanly removed from images living inside a shared
/// Form XObject. <see cref="ThumbnailBytes"/> is populated by Infrastructure
/// during analysis and is left <c>null</c> when generation fails; the UI must
/// fall back to a placeholder icon in that case (§12).
/// </remarks>
public sealed record PdfImageGroup(
    string GroupId,
    string Hash,
    int PixelWidth,
    int PixelHeight,
    string ColorSpace,
    int BitsPerComponent,
    string Compression,
    long EstimatedSize,
    bool IsImageMask,
    bool IsPossibleFullPageImage,
    bool IsSafelyRemovable,
    string? WarningMessage,
    byte[]? ThumbnailBytes,
    IReadOnlyList<PdfImageOccurrence> Occurrences,
    RemovableKind Kind = RemovableKind.Image,
    string? TextValue = null,
    ShapeGeometry? ShapeGeometry = null)
{
    /// <summary>Total number of placements across all pages (§11.3 "使用回数").</summary>
    public int UsageCount => Occurrences.Count;

    /// <summary>Distinct pages that draw this image, in ascending order.</summary>
    public IReadOnlyList<int> UsagePages =>
        Occurrences.Select(o => o.PageNumber).Distinct().OrderBy(p => p).ToArray();
}
