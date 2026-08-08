namespace PdfImageRemoverForRag.Core.Models;

/// <summary>
/// Result of analyzing a source PDF. Everything the UI needs to render the
/// object list and to decide whether removal is safe fits inside this
/// record — analysis runs off the UI thread and the UI reads immutable data.
/// </summary>
/// <param name="Pages">
/// Every page's size, in the file's own order. Carried because a placement
/// outside any overlap region still has to become one — the panel lists every
/// place an object is drawn, and a region needs the page it is on — and only
/// the analyzer has read the page to know how big it is.
/// </param>
/// <param name="OverlapRegions">
/// Places where objects of different kinds overlap, per page — what the flatten
/// side offers to turn into a single image. Deliberately NOT folded into
/// <paramref name="ObjectGroups"/>: a group is one identity wherever it appears,
/// while a region is one spot on one page, and flattening acts on the spot.
/// </param>
public sealed record PdfDocumentInfo(
    string FilePath,
    long FileSize,
    int PageCount,
    bool IsEncrypted,
    IReadOnlyList<ObjectGroup> ObjectGroups,
    IReadOnlyList<OverlapRegion> OverlapRegions,
    IReadOnlyList<PageDimensions> Pages)
{
    /// <summary>File name without directory, for compact UI display.</summary>
    public string FileName => Path.GetFileName(FilePath);

    /// <summary>How many distinct objects the file holds, one per group.</summary>
    public int ObjectGroupCount => ObjectGroups.Count;

    /// <summary>Total placements across every group (spec §11 "使用箇所").</summary>
    public int TotalUsageCount => ObjectGroups.Sum(g => g.UsageCount);
}
