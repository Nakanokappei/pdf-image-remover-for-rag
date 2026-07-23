using PdfImageRemoverForRag.Core.Models;

namespace PdfImageRemoverForRag.Core.Abstractions;

/// <summary>
/// Reads a PDF, enumerates its Image XObjects, groups them, and returns an
/// immutable <see cref="PdfDocumentInfo"/>. Implemented by Infrastructure
/// using PDFsharp + PdfPig; the UI depends only on this interface.
/// </summary>
public interface IPdfDocumentAnalyzer
{
    /// <summary>Analyze <paramref name="pdfFilePath"/> off the UI thread.</summary>
    /// <param name="pdfFilePath">Absolute path to an existing readable PDF.</param>
    /// <param name="thumbnailMaxWidth">
    /// Maximum thumbnail width in pixels — passed to the Infrastructure
    /// thumbnail generator. Matches the spec §12 default of 160.
    /// </param>
    /// <param name="thumbnailMaxHeight">
    /// Maximum thumbnail height in pixels; default matches spec §12 (120).
    /// </param>
    /// <param name="progress">
    /// Optional receiver for phase-and-count updates while the analysis runs.
    /// Large PDFs take minutes, so the UI needs something better than a static
    /// "analyzing…" label; pass null when nobody is watching.
    /// </param>
    /// <param name="ct">Cancellation token, honoured for long files.</param>
    Task<PdfDocumentInfo> AnalyzeAsync(
        string pdfFilePath,
        int thumbnailMaxWidth = 160,
        int thumbnailMaxHeight = 120,
        IProgress<AnalysisProgress>? progress = null,
        CancellationToken ct = default);
}
