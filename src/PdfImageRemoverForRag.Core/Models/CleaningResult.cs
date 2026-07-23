namespace PdfImageRemoverForRag.Core.Models;

/// <summary>
/// Output of a cleaning run. Success only means the temp-file swap
/// completed; the caller must still invoke <c>IPdfDocumentVerifier</c> to
/// confirm the resulting PDF opens and drops the target images.
/// </summary>
public sealed record CleaningResult(
    string SourcePath,
    string DestinationPath,
    IReadOnlyList<string> RemovedGroupHashes,
    int PagesModified,
    int DrawCallsRemoved,
    TimeSpan Elapsed);
