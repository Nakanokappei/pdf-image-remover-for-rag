namespace PdfImageRemoverForRag.Core.Models;

/// <summary>
/// A user's decision to remove every placement of one image group. The UI
/// produces one <see cref="ImageRemovalSelection"/> per checked row; the
/// cleaner consumes the whole list. Storing every occurrence up-front (rather
/// than re-deriving from the group) keeps this record self-describing so it
/// can be logged, replayed, and diffed without re-parsing the PDF.
/// </summary>
public sealed record ImageRemovalSelection(
    string GroupId,
    IReadOnlyList<PdfImageOccurrence> Occurrences,
    RemovableKind Kind = RemovableKind.Image,
    string? TextValue = null,
    string? Hash = null)
{
    /// <summary>
    /// The stream hash of the image to remove — the same identity the grouping
    /// and the post-save verification use.
    ///
    /// Images used to be matched by the indirect-object id carried on each
    /// occurrence. That is a weaker identity than the hash: a document may hold
    /// the same image bytes as several distinct objects, and then the occurrence
    /// list covers only the objects that were seen, so pages referencing a
    /// different copy kept their image and verification failed. Matching on the
    /// hash removes every placement of those bytes, which is exactly what the
    /// row the user checked promises.
    /// </summary>
    public string? Hash { get; init; } = Hash;
}
