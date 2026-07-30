namespace PdfImageRemoverForRag.Core.Models;

/// <summary>
/// A rectangle on a page in PDF points, origin at the page's bottom-left — the
/// space image occurrences and <see cref="OverlapRegion"/> use.
///
/// Separate from <see cref="OverlapRegion"/> on purpose: a rasterizer needs the
/// geometry and nothing else, and should not be handed the list of objects that
/// happen to be inside it.
/// </summary>
public sealed record PageRegion(double X, double Y, double Width, double Height)
{
    public static PageRegion Of(OverlapRegion region) =>
        new(region.X, region.Y, region.Width, region.Height);
}
