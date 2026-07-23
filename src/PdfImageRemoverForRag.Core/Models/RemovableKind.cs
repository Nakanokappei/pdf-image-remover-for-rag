namespace PdfImageRemoverForRag.Core.Models;

/// <summary>
/// What kind of object a removable group represents. The tool started with
/// images only; text and vector shapes were added so repeated
/// header/footer/watermark noise (RAG noise) can be removed the same way.
/// Order matters — the UI sorts groups Image → Text → Shape.
/// </summary>
public enum RemovableKind
{
    /// <summary>An Image XObject removed via its <c>Do</c> operator.</summary>
    Image = 0,

    /// <summary>A text string removed via its <c>Tj</c> / <c>TJ</c> operator.</summary>
    Text = 1,

    /// <summary>A vector path (line/rectangle/curve) removed via its path + paint operators.</summary>
    Shape = 2,
}
