namespace PdfImageRemoverForRag.Core.Models;

/// <summary>
/// One placement of a graphics object on a page. Occurrences of the same
/// object are collapsed into a single <see cref="ObjectGroup"/>.
/// </summary>
/// <param name="PageNumber">1-based page number where it is drawn.</param>
/// <param name="ObjectId">
/// PDF indirect-object identifier of the XObject (e.g. "7 0 R"). Empty for
/// kinds that are operators in a content stream rather than an object.
/// </param>
/// <param name="ResourceName">
/// Name used inside the page's Resources/XObject dictionary (e.g. "/Im1").
/// This is what the content-stream's <c>Do</c> operator references.
/// </param>
/// <param name="X">Bottom-left X of the bounding box in page coordinates (points).</param>
/// <param name="Y">Bottom-left Y of the bounding box in page coordinates (points).</param>
/// <param name="Width">Width of the bounding box in page coordinates (points).</param>
/// <param name="Height">Height of the bounding box in page coordinates (points).</param>
public sealed record ObjectOccurrence(
    int PageNumber,
    string ObjectId,
    string ResourceName,
    double X,
    double Y,
    double Width,
    double Height);
