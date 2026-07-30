using System.Drawing.Imaging;
using PdfImageRemoverForRag.Core.Abstractions;
using PdfImageRemoverForRag.Core.Models;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

namespace PdfImageRemoverForRag.App;

/// <summary>
/// <see cref="IPageRasterizer"/> on the operating system's own PDF renderer
/// (<c>Windows.Data.Pdf</c>) — the same one the usage-locations window uses, and
/// the reason no native PDFium/Skia binary has to be shipped.
///
/// This lives in the App layer because the WinRT projection is Windows-only,
/// while the layer that rewrites PDFs has to keep building and testing on macOS.
/// </summary>
internal sealed class WindowsPageRasterizer : IPageRasterizer
{
    /// <summary>
    /// Windows.Data.Pdf measures pages in device-independent pixels (96 per
    /// inch), not in PDF points (72). Everything crossing this boundary has to
    /// be converted: getting it wrong put the usage-locations outline a quarter
    /// of a page out of place until build 58.
    /// </summary>
    const double DipsPerPoint = 96.0 / 72.0;

    /// <summary>
    /// Longest side, in pixels, any rendered region may have. A region can be a
    /// whole page, and a whole page at a high DPI is a bitmap large enough for
    /// GDI+ to fail the allocation — which is exactly how the thumbnail
    /// pipeline once died at object 229 of 1,255.
    /// </summary>
    const int MaxPixelsOnLongSide = 4000;

    /// <summary>
    /// How far off the requested pixel size may land before a second attempt is
    /// worth making.
    /// </summary>
    const double SizeTolerance = 0.1;

    readonly Dictionary<string, PdfDocument> _openDocuments = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Pixels the renderer actually produces per unit of
    /// <c>DestinationWidth</c>, measured rather than assumed.
    ///
    /// DestinationWidth is documented as pixels but behaves as device-independent
    /// pixels: measured on this machine, every requested width came back
    /// multiplied by 1.8 (50 -> 90, 100 -> 180, 1000 -> 1800), which is the
    /// display's scale factor. Trusting the request would break the pixel
    /// ceiling by whatever the viewer's display happens to be scaled to, so the
    /// ratio is learned from the first render and used to correct the next
    /// request. Starts at 1 — the correct value for an unscaled display.
    /// </summary>
    double _pixelsPerRequestedUnit = 1.0;

    public async Task<byte[]?> RenderRegionAsync(
        string pdfFilePath,
        int pageNumber,
        PageRegion region,
        int targetDpi,
        CancellationToken ct = default)
    {
        try
        {
            if (region.Width <= 0 || region.Height <= 0 || targetDpi <= 0) return null;

            var document = await LoadDocumentAsync(pdfFilePath);
            if (document is null || pageNumber < 1 || pageNumber > (int)document.PageCount) return null;

            ct.ThrowIfCancellationRequested();
            using var page = document.GetPage((uint)(pageNumber - 1));

            var media = page.Dimensions.MediaBox;
            double pageHeightDips = media.Height > 0 ? media.Height : page.Size.Height;
            if (pageHeightDips <= 0) return null;

            // PDF space has its origin at the bottom-left and the renderer's has
            // it at the top-left, so the region's top edge is measured down from
            // the page's top.
            double left = region.X * DipsPerPoint;
            double width = region.Width * DipsPerPoint;
            double height = region.Height * DipsPerPoint;
            double top = pageHeightDips - ((region.Y + region.Height) * DipsPerPoint);

            var sourceRect = new Windows.Foundation.Rect(left, top, width, height);
            int wanted = PixelWidthFor(region, targetDpi);

            using var bitmap = await RenderAtWidthAsync(page, sourceRect, wanted, ct);
            if (bitmap is null) return null;

            using var png = new MemoryStream();
            bitmap.Save(png, ImageFormat.Png);
            return png.ToArray();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // A region that will not render must leave the caller able to skip
            // it and keep the page as it was, so every other failure is a null.
            return null;
        }
    }

    /// <summary>
    /// Render the region at (as close as the renderer allows to)
    /// <paramref name="wantedPixelWidth"/>. The first attempt uses the ratio
    /// learned so far; if the result misses by more than
    /// <see cref="SizeTolerance"/>, the ratio is re-derived and one corrected
    /// attempt is made. Anything still over the ceiling is scaled down, so the
    /// promise about the maximum size holds whatever the API does.
    /// </summary>
    async Task<Bitmap?> RenderAtWidthAsync(
        PdfPage page, Windows.Foundation.Rect sourceRect, int wantedPixelWidth, CancellationToken ct)
    {
        var bitmap = await RenderOnceAsync(page, sourceRect, wantedPixelWidth, ct);
        if (bitmap is null) return null;

        double ratio = bitmap.Width / Math.Max(1.0, wantedPixelWidth / _pixelsPerRequestedUnit);
        if (ratio > 0) _pixelsPerRequestedUnit = ratio;

        if (Math.Abs(bitmap.Width - wantedPixelWidth) > wantedPixelWidth * SizeTolerance)
        {
            var corrected = await RenderOnceAsync(page, sourceRect, wantedPixelWidth, ct);
            if (corrected is not null)
            {
                bitmap.Dispose();
                bitmap = corrected;
            }
        }

        // Last line of defence: the ceiling is about memory and file size, and
        // it has to hold even if the renderer ignores the request entirely.
        int longest = Math.Max(bitmap.Width, bitmap.Height);
        if (longest > MaxPixelsOnLongSide)
        {
            double scale = MaxPixelsOnLongSide / (double)longest;
            var reduced = new Bitmap(
                Math.Max(1, (int)(bitmap.Width * scale)), Math.Max(1, (int)(bitmap.Height * scale)));
            using (var g = Graphics.FromImage(reduced))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(bitmap, 0, 0, reduced.Width, reduced.Height);
            }
            bitmap.Dispose();
            bitmap = reduced;
        }
        return bitmap;
    }

    /// <summary>One render pass, decoded into a stream-independent bitmap.</summary>
    async Task<Bitmap?> RenderOnceAsync(
        PdfPage page, Windows.Foundation.Rect sourceRect, int wantedPixelWidth, CancellationToken ct)
    {
        // The request is in whatever unit the renderer treats DestinationWidth
        // as; the learned ratio converts from the pixels we actually want.
        uint request = (uint)Math.Max(1, Math.Round(wantedPixelWidth / _pixelsPerRequestedUnit));
        var options = new PdfPageRenderOptions
        {
            SourceRect = sourceRect,
            DestinationWidth = request,
        };

        using var stream = new InMemoryRandomAccessStream();
        await page.RenderToStreamAsync(stream, options);
        ct.ThrowIfCancellationRequested();

        // Copy the encoded bytes out through a DataReader (pure WinRT, no
        // stream-adapter dependency) and decode into a stream-independent
        // Bitmap so nothing keeps the WinRT stream alive.
        stream.Seek(0);
        uint size = (uint)stream.Size;
        using var reader = new DataReader(stream.GetInputStreamAt(0));
        await reader.LoadAsync(size);
        var rendered = new byte[size];
        reader.ReadBytes(rendered);

        using var source = new MemoryStream(rendered);
        using var decoded = Image.FromStream(source);
        return new Bitmap(decoded);
    }

    /// <summary>
    /// Pixel width for the requested resolution, reduced if either side would
    /// otherwise exceed <see cref="MaxPixelsOnLongSide"/>. Never rounds up past
    /// the requested DPI.
    /// </summary>
    static int PixelWidthFor(PageRegion region, int targetDpi)
    {
        double scale = targetDpi / 72.0;
        double width = region.Width * scale;
        double height = region.Height * scale;
        double longest = Math.Max(width, height);
        if (longest > MaxPixelsOnLongSide) width *= MaxPixelsOnLongSide / longest;
        return Math.Max(1, (int)Math.Round(width));
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
