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

    /// <summary>
    /// A shadow layer — "Shadow" in the UI. An Image XObject holding ONE flat
    /// colour, shaped entirely by its soft mask. That is how a drop shadow
    /// survives being exported to PDF: PowerPoint keeps the shadow's colour in
    /// the picture and its blurred outline in the mask, because PDF has no
    /// blur operator to draw it with.
    ///
    /// It is listed apart from an ordinary image because of what happens
    /// downstream. A reader that walks the file's objects writes the picture
    /// out and drops the mask, so a layer that is nearly invisible on the page
    /// arrives in a RAG pipeline as a solid black rectangle. Users reported
    /// exactly that, could not tell which rows caused it, and left them.
    ///
    /// Removed exactly as an image is — it IS an Image XObject — so everything
    /// that resolves images by stream hash covers it too.
    /// </summary>
    Shadow = 4,
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
        kind is RemovableKind.Image or RemovableKind.Drawing or RemovableKind.Shadow;

    /// <summary>
    /// Whether the file draws objects of this kind with a <c>Do</c> operator
    /// naming an Image XObject. A shadow is one, which is why removing it
    /// takes the same path an image takes — the resource entry and the draw
    /// call — and not the form path a drawing takes.
    ///
    /// Separate from <see cref="IsIdentifiedByStreamHash"/> because the two
    /// questions have different answers: a drawing is also identified by a
    /// stream hash, but the stream is a form's, and looking for it among the
    /// image entries finds nothing.
    /// </summary>
    public static bool IsImageXObject(this RemovableKind kind) =>
        kind is RemovableKind.Image or RemovableKind.Shadow;
}
