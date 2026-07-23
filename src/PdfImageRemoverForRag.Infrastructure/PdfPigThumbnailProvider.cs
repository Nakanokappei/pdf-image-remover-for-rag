using PdfImageRemoverForRag.Core.Abstractions;
using PdfImageRemoverForRag.Core.Hashing;
using PdfImageRemoverForRag.Core.Models;
using PdfPig = UglyToad.PdfPig;

namespace PdfImageRemoverForRag.Infrastructure;

/// <summary>
/// PdfPig-backed <see cref="IThumbnailProvider"/>. Opens the PDF once,
/// walks every page, and produces displayable image bytes per unique image:
///
/// <list type="bullet">
///   <item><b>PNG</b> via <c>IPdfImage.TryGetPng</c> for Flate/raw bitmaps.</item>
///   <item><b>JPEG passthrough</b> for DCTDecode images — PdfPig's
///   <c>TryGetPng</c> always returns false for JPEG (documented behavior),
///   but <c>RawBytes</c> IS a complete JPEG file that standard image APIs
///   decode directly. Without this branch every photographic image in a
///   real-world PDF renders as the placeholder icon.</item>
/// </list>
///
/// Keys are SHA-256 of the raw filtered stream bytes — the same hash the
/// analyzer computes — so the dictionary joins cleanly against
/// <c>ImageDiscovery.StreamHash</c>. Formats neither branch handles
/// (JPEG2000, CCITT, JBIG2) are simply absent from the result and fall back
/// to the placeholder icon in the UI (§12).
/// </summary>
public sealed class PdfPigThumbnailProvider : IThumbnailProvider
{
    public Task<IReadOnlyDictionary<string, byte[]>> ExtractThumbnailsAsync(
        string pdfFilePath,
        int maxWidth,
        int maxHeight,
        IProgress<AnalysisProgress>? progress = null,
        CancellationToken ct = default)
    {
        // maxWidth/maxHeight kept in the signature so the interface stays
        // stable; the App scales for display (grid vs tile need different
        // sizes from the same source bytes).
        _ = maxWidth;
        _ = maxHeight;

        return Task.Run<IReadOnlyDictionary<string, byte[]>>(
            () => Extract(pdfFilePath, progress, ct), ct);
    }

    static IReadOnlyDictionary<string, byte[]> Extract(
        string pdfFilePath, IProgress<AnalysisProgress>? progress, CancellationToken ct)
    {
        var result = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        try
        {
            using var doc = PdfPig.PdfDocument.Open(pdfFilePath);
            // Progress is counted in pages here, not images: the image count is
            // only known after walking a page, and decoding cost tracks pages
            // closely enough for a progress bar.
            int pageCount = doc.NumberOfPages;
            int pagesDone = 0;
            foreach (var page in doc.GetPages())
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report(new AnalysisProgress(
                    AnalysisPhase.ExtractingThumbnails, pagesDone++, pageCount));
                foreach (var image in page.GetImages())
                {
                    var rawBytes = image.RawBytes.ToArray();
                    var hash = StreamHasher.Sha256Hex(rawBytes);
                    if (result.ContainsKey(hash)) continue; // one thumbnail per unique stream

                    if (image.TryGetPng(out var png) && png is { Length: > 0 })
                    {
                        result[hash] = png;
                    }
                    else if (IsJpeg(rawBytes))
                    {
                        // DCTDecode passthrough — the raw stream is a valid
                        // standalone JPEG file.
                        result[hash] = rawBytes;
                    }
                    // else: unsupported format → no entry → placeholder icon.
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Per §12: a bad PDF must not abort the caller's whole analysis
            // just because thumbnails could not be produced. Return whatever
            // has been accumulated so far.
        }
        return result;
    }

    /// <summary>SOI marker check — every JPEG stream starts FF D8 FF.</summary>
    static bool IsJpeg(byte[] bytes) =>
        bytes.Length > 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF;
}
