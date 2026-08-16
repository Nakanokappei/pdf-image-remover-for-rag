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
    /// The form a picture this app RENDERED should be stored in: the PNG it was
    /// given, or a JPEG when that is both smaller and safe.
    ///
    /// Only pictures the app makes itself go through here — a flattened region.
    /// An image that arrived inside the user's PDF keeps whatever form it
    /// arrived in, because introducing loss the source never had is not this
    /// tool's decision to make.
    ///
    /// Two things have to hold before a JPEG is used, and they are what make
    /// this safe rather than a guess:
    ///
    /// JPEG has no transparency. A flattened region is transparent wherever its
    /// objects draw nothing, and flattening the alpha to white would paint over
    /// the page underneath. Anything with a transparent pixel stays lossless.
    ///
    /// And the JPEG has to be smaller. Line art and flat fills compress far
    /// better whole than as a JPEG — measured on a full-page figure, the JPEG
    /// came to more than twice the lossless size — so this is what keeps a
    /// flattened diagram lossless while a flattened photograph, which costs
    /// fourteen times as much stored losslessly, becomes a JPEG.
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
