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
    /// given quality, or the PNG it was handed if the encoder refuses it.
    ///
    /// Only pictures the app makes itself go through here — a flattened region.
    /// An image that arrived inside the user's PDF keeps whatever form it
    /// arrived in, because introducing loss the source never had is not this
    /// tool's decision to make.
    ///
    /// Nothing else is weighed. Not the size — choosing whichever encoding came
    /// out smaller left the quality setting doing nothing on a flattened
    /// diagram. And not transparency: a flattened region is transparent
    /// wherever its objects draw nothing, and keeping that meant keeping the
    /// whole picture lossless, which on one customer's file cost four and a
    /// half times the bytes and produced an output LARGER than the input. The
    /// alpha is composited onto white and spent, which is a judgement about
    /// what this tool is for — feeding a retrieval pipeline, not reproducing a
    /// page over a colored background.
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
