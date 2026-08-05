using PdfImageRemoverForRag.Core.Models;

namespace PdfImageRemoverForRag.Core.Abstractions;

/// <summary>
/// Renders a rectangle of one PDF page to pixels. Flattening an overlap needs
/// this: the objects that overlap are replaced by an image of what they looked
/// like, so the text stops reaching a RAG pipeline while the page still reads
/// the same to a person.
/// </summary>
/// <remarks>
/// This is an interface in Core rather than a class in Infrastructure because
/// the only rasterizer available without shipping a native binary is the
/// operating system's own (<c>Windows.Data.Pdf</c>), and Infrastructure has to
/// keep building and running its tests on macOS. The implementation therefore
/// lives in the App layer, next to the other Windows-only code, and is passed
/// in — the same arrangement as <see cref="IThumbnailProvider"/>.
///
/// It also means the rewrite logic can be tested without an OS renderer at all:
/// a test supplies a rasterizer that returns a flat colour.
/// </remarks>
public interface IPageRasterizer
{
    /// <summary>
    /// Render <paramref name="region"/> of one page and return PNG bytes, or
    /// <c>null</c> if it cannot be rendered.
    ///
    /// Returning null must leave the caller a usable choice: skip flattening
    /// that region and leave the page as it was. Half-applying a flatten —
    /// deleting the objects and then failing to draw their replacement — would
    /// blank part of the page.
    /// </summary>
    /// <param name="pdfFilePath">The source file, re-read by the implementation.</param>
    /// <param name="pageNumber">1-based page number.</param>
    /// <param name="region">
    /// The area to render, in PDF points with the origin at the page's
    /// bottom-left — the same space image occurrences use. Implementations
    /// convert to whatever their renderer wants.
    ///
    /// A page carrying <c>/Rotate</c> is where that conversion earns its keep:
    /// a renderer draws such a page turned, so the rectangle has to be mapped
    /// (<see cref="PageRotation"/>) and the result turned back — the returned
    /// image is always oriented like the CONTENT, because the caller draws it
    /// into content coordinates.
    /// </param>
    /// <param name="targetDpi">
    /// Resolution to render at. Implementations may render below it to stay
    /// inside their own pixel budget, and should never render above it.
    /// </param>
    /// <param name="transparentBackground">
    /// Leave the paper unpainted, so the result carries alpha where the page
    /// draws nothing. Flattening needs it: the picture it produces holds only
    /// the objects the user ticked, and an opaque background would hide the
    /// neighbours they chose to keep. A preview wants the opposite — paper is
    /// what a page looks like.
    /// </param>
    Task<byte[]?> RenderRegionAsync(
        string pdfFilePath,
        int pageNumber,
        PageRegion region,
        int targetDpi,
        bool transparentBackground = false,
        CancellationToken ct = default);
}
