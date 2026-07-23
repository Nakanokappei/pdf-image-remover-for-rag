using PdfImageRemoverForRag.Core.Models;

namespace PdfImageRemoverForRag.Core.Grouping;

/// <summary>
/// Decides whether a placed image is a "full-page image candidate" (§7, §11).
/// An image counts as full-page when its bounding box covers at least
/// <see cref="CoverageThreshold"/> of both the page width and height. The
/// threshold is deliberately not 100 %: PDFsharp's DrawImage inserts a couple
/// of user-space points of margin, so a strict equality check would miss
/// obvious scan pages.
/// </summary>
public sealed class FullPageImageDetector
{
    /// <summary>Coverage fraction (0..1) at which we start warning the user.</summary>
    public const double CoverageThreshold = 0.9;

    readonly Dictionary<int, PageDimensions> _pagesByNumber;

    public FullPageImageDetector(IEnumerable<PageDimensions> pages)
    {
        // Index by page number so lookups from an occurrence are O(1). Reject
        // duplicate page numbers early — this only ever means the caller sent
        // the same page twice, which would give ambiguous results.
        _pagesByNumber = new Dictionary<int, PageDimensions>();
        foreach (var page in pages)
        {
            if (!_pagesByNumber.TryAdd(page.PageNumber, page))
            {
                throw new ArgumentException(
                    $"duplicate page number {page.PageNumber} in dimensions", nameof(pages));
            }
        }
    }

    /// <summary>
    /// True when the occurrence covers at least <see cref="CoverageThreshold"/>
    /// of the referenced page in both dimensions.
    /// </summary>
    public bool IsPossibleFullPage(PdfImageOccurrence occurrence)
    {
        if (!_pagesByNumber.TryGetValue(occurrence.PageNumber, out var page))
        {
            // If we do not know the page size we cannot decide — err on the
            // side of NOT warning (a false negative is safer than a false
            // positive that blocks removals silently).
            return false;
        }
        if (page.WidthPoints <= 0 || page.HeightPoints <= 0) return false;
        double coverageW = occurrence.Width / page.WidthPoints;
        double coverageH = occurrence.Height / page.HeightPoints;
        return coverageW >= CoverageThreshold && coverageH >= CoverageThreshold;
    }

    /// <summary>True when at least one occurrence in the collection is full-page.</summary>
    public bool AnyIsPossibleFullPage(IEnumerable<PdfImageOccurrence> occurrences) =>
        occurrences.Any(IsPossibleFullPage);
}
