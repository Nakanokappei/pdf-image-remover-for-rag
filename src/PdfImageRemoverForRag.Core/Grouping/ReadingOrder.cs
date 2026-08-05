using PdfImageRemoverForRag.Core.Models;

namespace PdfImageRemoverForRag.Core.Grouping;

/// <summary>
/// The order a person reads a page in: down from the top, and left to right
/// within a line.
///
/// Two objects are on the same line when their tops are close enough that a
/// reader would take them as side by side rather than one above the other —
/// which is not the same as their tops being equal, since a heading and the
/// label beside it rarely start at the same point. Sorting on the top alone
/// puts the label before the heading it belongs to.
///
/// Straight-line distance from the page corner was the other candidate and is
/// not the same thing: on a landscape page a heading at the top right is 600 pt
/// from the corner while a small figure at the middle left is 300 pt, so the
/// figure would come first though the reader meets the heading first.
/// </summary>
public static class ReadingOrder
{
    /// <summary>
    /// How far apart two tops may be and still count as one line, in points.
    /// A line of body text is around 12 pt tall, so 8 pt is comfortably inside
    /// one line and comfortably outside two.
    /// </summary>
    public const double LinePoints = 8;

    /// <summary>Objects in reading order.</summary>
    public static IReadOnlyList<PlacedObject> Sort(IReadOnlyList<PlacedObject> objects) =>
        SortBy(objects,
            o => o.Y + o.Height,
            o => o.X,
            o => ((int)o.Kind, o.Identity));

    /// <summary>Regions in reading order, by the rectangle each one covers.</summary>
    public static IReadOnlyList<OverlapRegion> Sort(IReadOnlyList<OverlapRegion> regions) =>
        SortBy(regions,
            r => r.Y + r.Height,
            r => r.X,
            r => (r.PageNumber, string.Empty));

    /// <summary>
    /// Group by line, then order within it. Lines are found by walking the tops
    /// downwards and starting a new line wherever the gap exceeds
    /// <see cref="LinePoints"/> — a fixed grid of bands would instead split two
    /// objects a hair apart whenever a band boundary happened to fall between
    /// them.
    ///
    /// The last key breaks ties between objects at the very same place, so the
    /// result never depends on the order the caller happened to collect them in.
    /// </summary>
    static IReadOnlyList<T> SortBy<T>(
        IReadOnlyList<T> items,
        Func<T, double> top,
        Func<T, double> left,
        Func<T, (int, string)> tieBreak)
    {
        if (items.Count < 2) return items;

        var byTop = items.OrderByDescending(top).ToArray();
        var line = new Dictionary<int, int>(byTop.Length);
        var currentLine = 0;
        line[0] = 0;
        for (var i = 1; i < byTop.Length; i++)
        {
            if (top(byTop[i - 1]) - top(byTop[i]) > LinePoints) currentLine++;
            line[i] = currentLine;
        }

        return byTop
            .Select((item, index) => (item, line: line[index]))
            .OrderBy(x => x.line)
            .ThenBy(x => left(x.item))
            .ThenBy(x => tieBreak(x.item).Item1)
            .ThenBy(x => tieBreak(x.item).Item2, StringComparer.Ordinal)
            .Select(x => x.item)
            .ToArray();
    }
}
