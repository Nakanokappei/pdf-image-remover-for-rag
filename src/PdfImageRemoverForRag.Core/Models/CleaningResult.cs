namespace PdfImageRemoverForRag.Core.Models;

/// <summary>
/// Output of a cleaning run. Success only means the temp-file swap
/// completed; the caller must still invoke <c>IPdfDocumentVerifier</c> to
/// confirm the resulting PDF opens and drops the target images.
/// </summary>
/// <param name="RemovedGroupHashes">
/// The image groups that are gone from the whole file. Images baked into a
/// flattened region are deliberately absent: an image flattened on one page is
/// usually still drawn on others, and the verifier would otherwise demand the
/// absence of something the file is right to keep. What flattening did take
/// off a page is reported per placement in <see cref="FlattenedParts"/>.
/// </param>
/// <param name="FlattenedParts">
/// One entry per placement that flattening deleted — the picture, string or
/// path that a rendering now stands in for. The list the user reads is built
/// from placements, so it can only be brought in line with the saved file if
/// the run says which placements went; counting them would not be enough,
/// since a string shown four times may lose one showing.
/// </param>
/// <param name="DrawCallsRemoved">
/// Draw calls the run DELETED — nothing else takes their place. The ones
/// flattening lifts out are not counted here, because flattening puts a
/// picture of them straight back: they show up in
/// <see cref="RegionsFlattened"/> instead. Reporting the two as one number
/// told a user who had only flattened that objects had been deleted.
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
    int ImagesKeptForOtherReferences = 0,
    IReadOnlyList<FlattenedPart>? FlattenedParts = null)
{
    /// <summary>Never null, so callers do not each have to guard it.</summary>
    public IReadOnlyList<FlattenedPart> FlattenedParts { get; init; } =
        FlattenedParts ?? Array.Empty<FlattenedPart>();
}

/// <summary>
/// One placement flattening deleted: where it was, and what it was.
/// <paramref name="Identity"/> is the key that kind is matched on — the stream
/// hash for an image or a shadow, the shown string for text, the path
/// signature for a shape — so it can be compared against a group without
/// deciding identity a second time.
/// </summary>
public sealed record FlattenedPart(int PageNumber, RemovableKind Kind, string Identity);
