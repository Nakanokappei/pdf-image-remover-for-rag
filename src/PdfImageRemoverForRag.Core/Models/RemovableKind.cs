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
    /// Artwork painted inside a Form XObject — "Drawing" in the UI. A Shape is one
    /// path on the page; a Drawing is everything one form paints, and it is
    /// removable only as a whole because the form's content stream is shared
    /// between the pages that draw it. What IS per-page is the form's
    /// <c>Do</c> call, so a Drawing is removed by dropping that call from the
    /// page, never by rewriting the form.
    /// </summary>
    Drawing = 3,
}

/// <summary>Questions about a kind that more than one layer has to agree on.</summary>
public static class RemovableKinds
{
    /// <summary>
    /// Whether objects of this kind are identified by the SHA-256 of a stream
    /// the file stores — as opposed to living as operators inside a content
    /// stream, where the shown string or the path signature identifies them.
    ///
    /// Written once because it decides three separate things that must agree:
    /// what <c>MatchKey</c> hands the cleaner, which hashes the cleaner
    /// resolves resource names from, and which groups the post-save check can
    /// verify at all. It had been hand-copied to each of them, and the last
    /// time they disagreed a released build had to be withdrawn.
    /// </summary>
    public static bool IsIdentifiedByStreamHash(this RemovableKind kind) =>
        kind is RemovableKind.Image or RemovableKind.Drawing;
}
