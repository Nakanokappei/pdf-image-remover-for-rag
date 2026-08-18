using PdfImageRemoverForRag.Core.Models;

namespace PdfImageRemoverForRag.Core.Abstractions;

/// <summary>
/// The imaging-library jobs the layer that rewrites PDFs cannot do itself:
/// redrawing an image smaller, and deciding which form a picture should be
/// stored in.
///
/// Implemented outside that layer for the same reason
/// <see cref="IPageRasterizer"/> is: both mean the operating system's imaging
/// library, and the rewriting layer has to stay portable.
/// </summary>
public interface IImageResampler
{
    /// <summary>
    /// The form a picture this app RENDERED should be stored in: a JPEG at the
    /// given quality, or the PNG it was handed when a JPEG cannot carry it.
    ///
    /// Only pictures the app makes itself go through here — a flattened region.
    /// An image that arrived inside the user's PDF keeps whatever form it
    /// arrived in, because introducing loss the source never had is not this
    /// tool's decision to make.
    ///
    /// One thing stops a JPEG, and it is not a question of size. JPEG has no
    /// transparency, and a flattened region is transparent wherever its objects
    /// draw nothing; flattening that alpha to white would paint over the page
    /// underneath. Anything with a transparent pixel stays lossless.
    ///
    /// Everything else is written at the quality the user set, even where the
    /// PNG would have been smaller. Choosing by size instead left the quality
    /// setting doing nothing on a flattened diagram, which is not a saving the
    /// user asked for.
    /// </summary>
    byte[] ChooseStorageForm(byte[] renderedPng, int jpegQuality);

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
