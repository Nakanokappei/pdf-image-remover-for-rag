using PdfImageRemoverForRag.Core.Models;

namespace PdfImageRemoverForRag.Core.Abstractions;

/// <summary>
/// Redraws an image at a smaller size.
///
/// Implemented outside the layer that rewrites PDFs, for the same reason
/// <see cref="IPageRasterizer"/> is: decoding and resampling a bitmap means the
/// operating system's imaging library, and that layer has to stay portable.
/// </summary>
public interface IImageResampler
{
    /// <summary>
    /// The image redrawn to <paramref name="width"/> x <paramref name="height"/>
    /// pixels, or null when its bytes cannot be decoded — a caller that gets
    /// null must leave the image as it found it.
    /// </summary>
    /// <param name="maximumJpegQuality">
    /// The most a JPEG may be encoded at. The image's own quality is used when
    /// it is lower: re-encoding a photo saved at 60 as 85 makes the file bigger
    /// and puts none of the detail back.
    /// </param>
    StoredImage? Resize(StoredImage image, int width, int height, int maximumJpegQuality);
}
