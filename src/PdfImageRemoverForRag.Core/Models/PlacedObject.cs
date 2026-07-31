namespace PdfImageRemoverForRag.Core.Models;

/// <summary>
/// One drawn object at one place on one page, as seen by overlap detection:
/// what kind it is, what identifies it (the same key the cleaner matches on),
/// and the rectangle it covers in PDF points with the origin at the page's
/// bottom-left.
///
/// This is deliberately not <see cref="PdfImageOccurrence"/>: an occurrence
/// belongs to a group and carries a resource name, while overlap detection only
/// needs geometry plus an identity, and it needs one for text and shapes too
/// (whose occurrences carry no rectangle).
/// </summary>
/// <param name="Kind">Image, text or shape.</param>
/// <param name="Identity">
/// Stream hash for an image, the shown string for text, the path signature for
/// a shape — the value the cleaner matches instances on.
/// </param>
/// <param name="HidesWhatIsBehind">
/// Whether the object paints over what is under it. Images and text always do.
/// A shape does when its paint operator fills; a stroke-only path — a frame, a
/// rule, an outline — does not, and that changes how it may connect to other
/// objects (see <c>OverlapDetector</c>).
/// </param>
public sealed record PlacedObject(
    RemovableKind Kind,
    string Identity,
    double X,
    double Y,
    double Width,
    double Height,
    bool HidesWhatIsBehind = true);

/// <summary>
/// One member of an overlap region: what to look for when the cleaner rewrites
/// the page. The instance is found again by geometry (inside the region) at
/// cleaning time rather than by a stored operator index, because the file is
/// re-read from disk and indices would not survive.
/// </summary>
public sealed record OverlapMember(RemovableKind Kind, string Identity);

/// <summary>
/// A place where objects of two or more different kinds overlap, and which can
/// therefore be flattened into a single raster image: the page, the union of
/// the members' rectangles, and the members themselves.
/// </summary>
/// <param name="PageWidthPoints">
/// The page's width. Carried on the region rather than looked up, because the
/// one question that needs it — is this area effectively the whole page? — is
/// asked wherever a region is held, and a region without its page's size cannot
/// answer it. Zero means the size was not supplied.
/// </param>
/// <param name="PageHeightPoints">The page's height; see the width.</param>
public sealed record OverlapRegion(
    int PageNumber,
    double X,
    double Y,
    double Width,
    double Height,
    IReadOnlyList<PlacedObject> Members,
    double PageWidthPoints = 0,
    double PageHeightPoints = 0)
{
    /// <summary>The page this region is on, size included.</summary>
    public PageDimensions Page => new(PageNumber, PageWidthPoints, PageHeightPoints);
}
