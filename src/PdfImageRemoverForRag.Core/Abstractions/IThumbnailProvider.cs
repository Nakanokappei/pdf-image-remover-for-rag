using PdfImageRemoverForRag.Core.Models;

namespace PdfImageRemoverForRag.Core.Abstractions;

/// <summary>
/// Extracts displayable thumbnail bytes (PNG or JPEG — anything a standard
/// image decoder handles) for every raster image in a PDF and returns a
/// hash-keyed dictionary the analyzer can splice back into the discovery
/// records it produced with PDFsharp.
/// </summary>
/// <remarks>
/// The interface takes a file path rather than raw bytes because
/// re-decoding an Image XObject in isolation is not viable — color space
/// arrays, soft masks and image masks live on the surrounding dictionary
/// and are needed to render pixels correctly. Passing the whole PDF lets
/// implementations use their existing document context.
///
/// Per spec §12, a single unrenderable image must not abort the batch:
/// failures are silently dropped from the returned dictionary, and the
/// analyzer treats a missing key as "no thumbnail — fall back to the
/// placeholder icon in the UI".
/// </remarks>
public interface IThumbnailProvider
{
    /// <summary>
    /// Produce thumbnail bytes for every Image XObject in the file. Keys are
    /// the same SHA-256 hex string the analyzer computes for
    /// <see cref="Models.ObjectDiscovery.StreamHash"/>. The size hints are
    /// advisory — callers scale for display.
    /// </summary>
    /// <param name="wantedHashes">
    /// The image streams a thumbnail is worth producing for, by the same hash
    /// the keys use. Null asks for everything, which is what a caller with
    /// nothing better to say should pass.
    ///
    /// It exists because extraction re-reads the whole document with a second
    /// parser, and on a long file that is nearly all of the analysis time: an
    /// EMPTY set lets an implementation skip the work entirely, and a known set
    /// lets it stop as soon as it has them. Measured at 9.8 seconds of a 10.9
    /// second analysis on a 136-page file whose images could not be decoded at
    /// all — ten seconds spent to produce nothing.
    /// </param>
    Task<IReadOnlyDictionary<string, byte[]>> ExtractThumbnailsAsync(
        string pdfFilePath,
        int maxWidth,
        int maxHeight,
        IReadOnlyCollection<string>? wantedHashes = null,
        IProgress<AnalysisProgress>? progress = null,
        CancellationToken ct = default);
}
