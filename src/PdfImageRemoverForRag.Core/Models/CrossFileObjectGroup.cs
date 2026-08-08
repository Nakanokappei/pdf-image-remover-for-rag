namespace PdfImageRemoverForRag.Core.Models;

/// <summary>
/// The placements of one object within one file, as part of a
/// <see cref="CrossFileObjectGroup"/>. Holds only the file path and
/// occurrence metadata — never decoded pixels or an open document.
/// </summary>
public sealed record CrossFileOccurrences(
    string FilePath,
    IReadOnlyList<ObjectOccurrence> Occurrences);

/// <summary>
/// One object identity across every open file: the same bytes (same SHA-256)
/// or the same string appearing in one or more PDFs collapse into a single
/// row, so a logo shared by several documents is removed with one tick.
/// Mirrors <see cref="ObjectGroup"/> but replaces the flat occurrence list
/// with a per-file breakdown that the save flow needs.
/// </summary>
public sealed record CrossFileObjectGroup(
    string GroupId,
    string Hash,
    int PixelWidth,
    int PixelHeight,
    string ColorSpace,
    int BitsPerComponent,
    string Compression,
    long EstimatedSize,
    bool IsImageMask,
    bool IsPossibleFullPageImage,
    bool IsSafelyRemovable,
    string? WarningMessage,
    byte[]? ThumbnailBytes,
    IReadOnlyList<CrossFileOccurrences> FileOccurrences,
    RemovableKind Kind = RemovableKind.Image,
    string? TextValue = null,
    ShapeGeometry? ShapeGeometry = null,
    DrawingGeometry? DrawingGeometry = null)
{
    /// <summary>
    /// Total placements across every file (the UI's Usage column and the tile
    /// badge).
    /// </summary>
    public int UsageCount => FileOccurrences.Sum(f => f.Occurrences.Count);

    /// <summary>Number of files that contain this image.</summary>
    public int FileCount => FileOccurrences.Count;

    /// <summary>
    /// The value this group is identified by: the stream hash for an image and
    /// for a drawing (both are streams the file already stores), and the match
    /// key — shown string or path signature — for the kinds that live as
    /// operators inside a content stream.
    /// The same thing <see cref="PlacedObject.Identity"/> carries, which is what
    /// makes <see cref="Matches"/> a lookup rather than a translation.
    ///
    /// This exists so the rule is written once. Getting image identity wrong is
    /// the mistake that forced a released build to be withdrawn, and the rule
    /// had started being hand-copied wherever a placement met a group.
    /// </summary>
    public string MatchKey =>
        Kind.IsIdentifiedByStreamHash() ? Hash : TextValue ?? string.Empty;

    /// <summary>
    /// Whether a drawn object belongs to this group. Kind first: an image hash
    /// and a text string are drawn from different alphabets, but nothing
    /// guarantees they cannot collide.
    /// </summary>
    public bool Matches(PlacedObject placed) =>
        placed.Kind == Kind && placed.Identity == MatchKey;
}
