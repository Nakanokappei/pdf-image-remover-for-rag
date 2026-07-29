using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

namespace PdfImageRemoverForRag.App;

/// <summary>
/// One rendered PDF page: the bitmap plus the page's size in PDF points, so a
/// caller can map a point-space bounding box (an image occurrence) onto the
/// pixels. The caller owns and disposes the bitmap.
/// </summary>
internal sealed record RenderedPage(Bitmap Bitmap, double PageWidthPoints, double PageHeightPoints);

/// <summary>
/// Rasterizes whole PDF pages with the OS's own PDF renderer
/// (<see cref="Windows.Data.Pdf.PdfDocument"/>). Used only by the
/// usage-locations window, which shows where an object is drawn on the page.
///
/// This is why the App targets a Windows 10 SDK TFM: the WinRT renderer is
/// present on every Windows 10/11 machine, so no native PDFium/Skia binary has
/// to be bundled — the same reason those were rejected for the rest of the app.
/// It is App-only (not Infrastructure) because Infrastructure stays GDI- and
/// Windows-free so it can build and test on macOS.
///
/// One instance caches the opened documents for a render session so a page is
/// not reloaded per request; a new session (one window) uses a new instance.
/// </summary>
internal sealed class PdfPageRenderer
{
    readonly Dictionary<string, PdfDocument> _openDocuments = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Render one 1-based page of <paramref name="filePath"/> at the given pixel
    /// width (height follows the page aspect). Returns null on any failure — a
    /// page that will not render shows a placeholder rather than breaking the
    /// window.
    /// </summary>
    public async Task<RenderedPage?> RenderAsync(string filePath, int pageNumber, int targetWidthPixels)
    {
        try
        {
            var document = await LoadDocumentAsync(filePath);
            if (document is null || pageNumber < 1 || pageNumber > (int)document.PageCount)
            {
                return null;
            }

            using var page = document.GetPage((uint)(pageNumber - 1));

            // Page size in PDF points, from the media box (falling back to the
            // reported size). Image occurrences carry point-space coordinates,
            // so this is the unit the caller maps its boxes with.
            //
            // Windows.Data.Pdf reports both in device-independent pixels
            // (96/inch), NOT in PDF points (72/inch) — measured on A4, which it
            // reports as 793.7 x 1122.5. Without this conversion every location
            // box lands at 3/4 of its distance from the page's bottom-left, which
            // on an A4 header logo put the outline a quarter of a page too low.
            const double PointsPerDip = 72.0 / 96.0;
            var media = page.Dimensions.MediaBox;
            double pageWidth = (media.Width > 0 ? media.Width : page.Size.Width) * PointsPerDip;
            double pageHeight = (media.Height > 0 ? media.Height : page.Size.Height) * PointsPerDip;

            var options = new PdfPageRenderOptions { DestinationWidth = (uint)Math.Max(1, targetWidthPixels) };
            using var stream = new InMemoryRandomAccessStream();
            await page.RenderToStreamAsync(stream, options);

            // Copy the encoded bytes out through a DataReader (pure WinRT, no
            // stream-adapter dependency) and decode into a stream-independent
            // Bitmap so nothing keeps the WinRT stream alive.
            stream.Seek(0);
            uint size = (uint)stream.Size;
            using var reader = new DataReader(stream.GetInputStreamAt(0));
            await reader.LoadAsync(size);
            var bytes = new byte[size];
            reader.ReadBytes(bytes);

            using var memory = new MemoryStream(bytes);
            using var decoded = Image.FromStream(memory);
            return new RenderedPage(new Bitmap(decoded), pageWidth, pageHeight);
        }
        catch
        {
            // A single page that cannot be rendered must not break the window.
            return null;
        }
    }

    async Task<PdfDocument?> LoadDocumentAsync(string filePath)
    {
        if (_openDocuments.TryGetValue(filePath, out var cached)) return cached;

        var file = await StorageFile.GetFileFromPathAsync(filePath);
        var document = await PdfDocument.LoadFromFileAsync(file);
        _openDocuments[filePath] = document;
        return document;
    }
}
