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
    /// <param name="jpegQuality">
    /// What a JPEG is encoded at, whatever it was encoded at before. A picture
    /// on its way into a RAG pipeline is there to be read on a screen, and one
    /// quality for all of them is one thing fewer to reason about.
    /// </param>
    StoredImage? Resize(StoredImage image, int width, int height, int jpegQuality);
}
