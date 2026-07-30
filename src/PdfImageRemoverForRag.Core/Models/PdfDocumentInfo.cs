namespace PdfImageRemoverForRag.Core.Models;

/// <summary>
/// Result of analyzing a source PDF. Everything the UI needs to render the
/// image-list screen and to decide whether removal is safe fits inside this
/// record — analysis runs off the UI thread and the UI reads immutable data.
/// </summary>
/// <param name="OverlapRegions">
/// Places where objects of different kinds overlap, per page — what the flatten
/// side offers to turn into a single image. Deliberately NOT folded into
/// <paramref name="ImageGroups"/>: a group is one identity wherever it appears,
/// while a region is one spot on one page, and flattening acts on the spot.
/// </param>
public sealed record PdfDocumentInfo(
    string FilePath,
    long FileSize,
    int PageCount,
    bool IsEncrypted,
    IReadOnlyList<PdfImageGroup> ImageGroups,
    IReadOnlyList<OverlapRegion> OverlapRegions)
{
    /// <summary>File name without directory, for compact UI display.</summary>
    public string FileName => Path.GetFileName(FilePath);

    /// <summary>Distinct image kinds (spec §11 "画像種類").</summary>
    public int ImageKindCount => ImageGroups.Count;

    /// <summary>Total placements across every group (spec §11 "使用箇所").</summary>
    public int TotalUsageCount => ImageGroups.Sum(g => g.UsageCount);
}
