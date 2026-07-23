namespace PdfImageRemoverForRag.Core.Models;

/// <summary>
/// Raw analysis output for a single Image XObject, produced by the
/// Infrastructure layer and fed into <c>ImageGroupBuilder</c>. Two discoveries
/// with the same <see cref="StreamHash"/> collapse into one
/// <see cref="PdfImageGroup"/>; their occurrences are unioned.
/// </summary>
/// <param name="ObjectId">PDF indirect-object identifier for logs and diagnostics.</param>
/// <param name="StreamHash">
/// SHA-256 (uppercase hex) of the raw filtered stream bytes. This is the
/// primary group key.
/// </param>
/// <param name="PixelWidth">Image pixel width (from the XObject dictionary).</param>
/// <param name="PixelHeight">Image pixel height (from the XObject dictionary).</param>
/// <param name="ColorSpace">Color space label (e.g. "/DeviceRGB", "ImageMask").</param>
/// <param name="BitsPerComponent">Bits-per-component from the XObject dictionary.</param>
/// <param name="Compression">Filter name (e.g. "/FlateDecode", "/DCTDecode").</param>
/// <param name="StreamByteCount">
/// Length of the stream in bytes — Infrastructure fills this from
/// <c>PdfStream.Length</c>. Used to compute <c>EstimatedSize</c> on the group.
/// </param>
/// <param name="IsImageMask">Whether the /ImageMask flag is set on the XObject.</param>
/// <param name="IsSafelyRemovable">
/// <c>false</c> when the image is reached only through a shared Form XObject
/// or through a structure the tool cannot rewrite without touching unrelated
/// content (§14.3). The UI must forbid checking such rows.
/// </param>
/// <param name="UnsafeReason">
/// User-visible short reason when <see cref="IsSafelyRemovable"/> is false.
/// Ignored when the image is removable.
/// </param>
/// <param name="ThumbnailBytes">
/// Optional pre-generated PNG thumbnail (max 160x120, spec §12). <c>null</c>
/// when thumbnail generation failed — analysis must not fail just because a
/// single thumbnail could not be produced.
/// </param>
/// <param name="Occurrences">All placements of this Image XObject.</param>
/// <param name="Kind">
/// Image or Text. Defaults to Image so existing image-producing code is
/// unchanged; the analyzer sets Text for repeated-string discoveries.
/// </param>
/// <param name="TextValue">
/// The cleaner's match key for non-image kinds. For
/// <see cref="RemovableKind.Text"/> it is the exact shown string (also
/// displayed); for <see cref="RemovableKind.Shape"/> it is the path
/// signature (internal, not displayed). Null for images.
/// </param>
public sealed record ImageDiscovery(
    string ObjectId,
    string StreamHash,
    int PixelWidth,
    int PixelHeight,
    string ColorSpace,
    int BitsPerComponent,
    string Compression,
    long StreamByteCount,
    bool IsImageMask,
    bool IsSafelyRemovable,
    string? UnsafeReason,
    byte[]? ThumbnailBytes,
    IReadOnlyList<PdfImageOccurrence> Occurrences,
    RemovableKind Kind = RemovableKind.Image,
    string? TextValue = null,
    ShapeGeometry? ShapeGeometry = null);
