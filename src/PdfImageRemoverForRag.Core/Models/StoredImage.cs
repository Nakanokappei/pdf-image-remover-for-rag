namespace PdfImageRemoverForRag.Core.Models;

/// <summary>
/// How an image's pixels are stored: as a JPEG file, or as the raw samples a
/// PDF holds under a lossless filter. Which one an image uses is kept when it
/// is resized — a photo re-encoded as raw samples grows enormously, and a
/// screenshot re-encoded as JPEG comes back blurred around its text.
/// </summary>
public enum StoredImageEncoding
{
    /// <summary>Raw 8-bit samples, one or three components per pixel.</summary>
    Samples,

    /// <summary>A complete JPEG file.</summary>
    Jpeg,
}

/// <summary>
/// One image out of a PDF, in the form it is stored in, with its soft mask if it
/// has one.
/// </summary>
/// <param name="Components">
/// 1 for grey, 3 for colour. Meaningless for <see cref="StoredImageEncoding.Jpeg"/>,
/// where the file says so itself.
/// </param>
/// <param name="SoftMask">
/// The image's transparency, as raw 8-bit grey samples of its own size. PDF
/// keeps it in a separate stream and allows it a different resolution, so it
/// carries its own dimensions. Null when the image is opaque.
/// </param>
public sealed record StoredImage(
    int Width,
    int Height,
    byte[] Data,
    StoredImageEncoding Encoding,
    int Components,
    StoredMask? SoftMask = null);

/// <summary>An image's soft mask: 8-bit grey samples and their size.</summary>
public sealed record StoredMask(int Width, int Height, byte[] Data);
