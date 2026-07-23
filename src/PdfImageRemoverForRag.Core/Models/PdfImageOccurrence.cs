namespace PdfImageRemoverForRag.Core.Models;

/// <summary>
/// One placement of an image on a page. Multiple occurrences with the same
/// image data are collapsed into a single <see cref="PdfImageGroup"/>.
/// </summary>
/// <param name="PageNumber">1-based page number where the image is drawn.</param>
/// <param name="ObjectId">
/// PDF indirect-object identifier of the image XObject (e.g. "7 0 R").
/// Empty for images embedded as inline XObjects.
/// </param>
/// <param name="ResourceName">
/// Name used inside the page's Resources/XObject dictionary (e.g. "/Im1").
/// This is what the content-stream's <c>Do</c> operator references.
/// </param>
/// <param name="X">Bottom-left X of the image bounding box in page coordinates (points).</param>
/// <param name="Y">Bottom-left Y of the image bounding box in page coordinates (points).</param>
/// <param name="Width">Width of the image bounding box in page coordinates (points).</param>
/// <param name="Height">Height of the image bounding box in page coordinates (points).</param>
public sealed record PdfImageOccurrence(
    int PageNumber,
    string ObjectId,
    string ResourceName,
    double X,
    double Y,
    double Width,
    double Height);
