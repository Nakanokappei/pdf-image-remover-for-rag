namespace PdfImageRemoverForRag.Core.Models;

/// <summary>
/// What kind of object a removable group represents. The tool started with
/// images only; text and vector shapes were added so repeated
/// header/footer/watermark noise (RAG noise) can be removed the same way.
/// Order matters — the UI sorts groups Image → Text → Shape → Drawing.
/// </summary>
public enum RemovableKind
{
    /// <summary>An Image XObject removed via its <c>Do</c> operator.</summary>
    Image = 0,

    /// <summary>A text string removed via its <c>Tj</c> / <c>TJ</c> operator.</summary>
    Text = 1,

    /// <summary>A vector path (line/rectangle/curve) removed via its path + paint operators.</summary>
    Shape = 2,

    /// <summary>
    /// Artwork painted inside a Form XObject — "図" in the UI. A Shape is one
    /// path on the page; a Drawing is everything one form paints, and it is
    /// removable only as a whole because the form's content stream is shared
    /// between the pages that draw it. What IS per-page is the form's
    /// <c>Do</c> call, so a Drawing is removed by dropping that call from the
    /// page, never by rewriting the form.
    /// </summary>
    Drawing = 3,
}
