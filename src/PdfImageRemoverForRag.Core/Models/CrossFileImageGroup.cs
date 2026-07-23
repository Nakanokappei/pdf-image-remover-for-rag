namespace PdfImageRemoverForRag.Core.Models;

/// <summary>
/// The placements of one image within one file, as part of a
/// <see cref="CrossFileImageGroup"/>. Holds only the file path and
/// occurrence metadata — never decoded pixels or an open document.
/// </summary>
public sealed record CrossFileOccurrences(
    string FilePath,
    IReadOnlyList<PdfImageOccurrence> Occurrences);

/// <summary>
/// One image identity across every open file: the same stream bytes (same
/// SHA-256) appearing in one or more PDFs collapse into a single row, so a
/// logo shared by several documents can be removed with one checkbox.
/// Mirrors <see cref="PdfImageGroup"/> but replaces the flat occurrence list
/// with a per-file breakdown that the save flow needs.
/// </summary>
public sealed record CrossFileImageGroup(
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
    ShapeGeometry? ShapeGeometry = null)
{
    /// <summary>Total placements across every file (UI 使用回数 / tile badge).</summary>
    public int UsageCount => FileOccurrences.Sum(f => f.Occurrences.Count);

    /// <summary>Number of files that contain this image.</summary>
    public int FileCount => FileOccurrences.Count;
}
