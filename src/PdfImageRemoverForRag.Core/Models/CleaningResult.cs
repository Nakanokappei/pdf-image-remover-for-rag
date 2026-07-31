namespace PdfImageRemoverForRag.Core.Models;

/// <summary>
/// Output of a cleaning run. Success only means the temp-file swap
/// completed; the caller must still invoke <c>IPdfDocumentVerifier</c> to
/// confirm the resulting PDF opens and drops the target images.
/// </summary>
/// <param name="RemovedGroupHashes">
/// The image groups that are gone from the whole file. Images baked into a
/// flattened region are deliberately absent: their bytes are still in the
/// document, and the verifier would otherwise demand their absence.
/// </param>
/// <param name="RegionsFlattened">
/// How many overlap regions were replaced by a raster image. Lower than the
/// number asked for when a region could not be rendered, or when nothing was
/// found at the place it was detected — both cases leave the page as it was.
/// </param>
/// <param name="ImagesKeptForOtherReferences">
/// How many removed images had to stay in the file because something other
/// than a page still pointed at them — an annotation's appearance stream, a
/// tiling pattern, a soft-mask group, a Type3 glyph. Their pages no longer
/// draw or list them, so they are removed as far as the document's content
/// goes, but the bytes remain and an object-enumerating reader will still find
/// them. Normally zero; a non-zero count is worth logging, because it is the
/// one case where removal cannot fully deliver what it promises.
/// </param>
public sealed record CleaningResult(
    string SourcePath,
    string DestinationPath,
    IReadOnlyList<string> RemovedGroupHashes,
    int PagesModified,
    int DrawCallsRemoved,
    TimeSpan Elapsed,
    int RegionsFlattened = 0,
    int ImagesKeptForOtherReferences = 0);
