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
    /// The screen a rendered region has to fit inside, in pixels. Not a limit on
    /// the longest side: width is measured against the width and height against
    /// the height, so a page-shaped region is bounded by its height and a
    /// banner-shaped one by its width.
    ///
    /// Two reasons, and the smaller one decides it. A region can be a whole
    /// page, and a whole page at a high DPI is a bitmap large enough for GDI+ to
    /// fail the allocation — which is how the thumbnail pipeline once died at
    /// object 229 of 1,255. And the picture ends up in a file that goes to a RAG
    /// pipeline whose reader displays it: the customer's standard screen is
    /// 1920x1080, so pixels past it are file size and nothing else (their upload
    /// limit is 15 MB, which a few full-page pictures can reach).
    ///
    /// The size is decided HERE rather than by shrinking afterwards, because the
    /// renderer can simply be asked for it. A whole A4 page comes out about 763
    /// pixels wide — the resolution that fits, not a reduction of the 200 DPI
    /// the caller asks for.
    /// </summary>
    const int MaxPixelWidth = 1920;
    const int MaxPixelHeight = 1080;

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
        bool transparentBackground = false,
        CancellationToken ct = default)
    {
        try
        {
            if (region.Width <= 0 || region.Height <= 0 || targetDpi <= 0) return null;

            var document = await LoadDocumentAsync(pdfFilePath);
            if (document is null || pageNumber < 1 || pageNumber > (int)document.PageCount) return null;

            ct.ThrowIfCancellationRequested();
            using var page = document.GetPage((uint)(pageNumber - 1));

            int rotation = RotationDegrees(page.Rotation);
            var (pageWidth, pageHeight) = PageSizeInPoints(page, rotation);
            if (pageWidth <= 0 || pageHeight <= 0) return null;

            // This renderer draws the page the way a viewer shows it — turned by
            // /Rotate, origin at the top-left — while the region arrives in the
            // content's own space. Asking in the wrong one is not a small error:
            // on a quarter-turned page the rectangle lands off the paper and
            // comes back blank, which the flatten path reads as "cannot render"
            // and skips.
            var displayed = PageRotation.ToDisplay(region, pageWidth, pageHeight, rotation);
            var sourceRect = new Windows.Foundation.Rect(
                displayed.X * DipsPerPoint, displayed.Y * DipsPerPoint,
                displayed.Width * DipsPerPoint, displayed.Height * DipsPerPoint);
            int wanted = PixelWidthFor(displayed, targetDpi);

            using var bitmap = await RenderAtWidthAsync(
                page, sourceRect, wanted, transparentBackground, ct);
            if (bitmap is null) return null;

            // Handed back the way the CONTENT has it, because that is the space
            // the caller draws it back into.
            TurnBackToContentOrientation(bitmap, rotation);

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
        PdfPage page, Windows.Foundation.Rect sourceRect, int wantedPixelWidth,
        bool transparentBackground, CancellationToken ct)
    {
        var bitmap = await RenderOnceAsync(page, sourceRect, wantedPixelWidth, transparentBackground, ct);
        if (bitmap is null) return null;

        double ratio = bitmap.Width / Math.Max(1.0, wantedPixelWidth / _pixelsPerRequestedUnit);
        if (ratio > 0) _pixelsPerRequestedUnit = ratio;

        if (Math.Abs(bitmap.Width - wantedPixelWidth) > wantedPixelWidth * SizeTolerance)
        {
            var corrected = await RenderOnceAsync(
                page, sourceRect, wantedPixelWidth, transparentBackground, ct);
            if (corrected is not null)
            {
                bitmap.Dispose();
                bitmap = corrected;
            }
        }

        // Last line of defense: the ceiling is about memory and file size, and
        // it has to hold even if the renderer ignores the request entirely.
        if (bitmap.Width > MaxPixelWidth || bitmap.Height > MaxPixelHeight)
        {
            double scale = ScaleToFit(bitmap.Width, bitmap.Height);
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
        PdfPage page, Windows.Foundation.Rect sourceRect, int wantedPixelWidth,
        bool transparentBackground, CancellationToken ct)
    {
        // The request is in whatever unit the renderer treats DestinationWidth
        // as; the learned ratio converts from the pixels we actually want.
        uint request = (uint)Math.Max(1, Math.Round(wantedPixelWidth / _pixelsPerRequestedUnit));
        var options = new PdfPageRenderOptions
        {
            SourceRect = sourceRect,
            DestinationWidth = request,
        };
        // Alpha zero leaves the paper unpainted, so the result carries
        // transparency wherever the page draws nothing. Measured before it was
        // relied on: the renderer honors it, and the default is opaque white.
        if (transparentBackground)
        {
            options.BackgroundColor = Windows.UI.Color.FromArgb(0, 255, 255, 255);
        }

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
    /// The page's own size in PDF points — content space, so a quarter turn is
    /// undone rather than applied.
    /// </summary>
    static (double Width, double Height) PageSizeInPoints(PdfPage page, int rotationDegrees)
    {
        const double PointsPerDip = 72.0 / 96.0;
        var media = page.Dimensions.MediaBox;
        if (media.Width > 0 && media.Height > 0)
        {
            // The media box is the page as authored, untouched by /Rotate.
            return (media.Width * PointsPerDip, media.Height * PointsPerDip);
        }

        // Size is the page as DISPLAYED, so on a quarter turn its sides are
        // already swapped. Swapping is its own inverse, so the same call that
        // produces a display size turns this one back into content space.
        var (width, height) = PageRotation.DisplaySize(
            page.Size.Width, page.Size.Height, rotationDegrees);
        return (width * PointsPerDip, height * PointsPerDip);
    }

    static int RotationDegrees(PdfPageRotation rotation) => rotation switch
    {
        PdfPageRotation.Rotate90 => 90,
        PdfPageRotation.Rotate180 => 180,
        PdfPageRotation.Rotate270 => 270,
        _ => 0,
    };

    /// <summary>
    /// Turn a rendering of a rotated page back the way its content stream has
    /// it, undoing what the viewer applied.
    /// </summary>
    static void TurnBackToContentOrientation(Bitmap bitmap, int rotationDegrees)
    {
        var back = PageRotation.Normalize(rotationDegrees) switch
        {
            90 => RotateFlipType.Rotate270FlipNone,
            180 => RotateFlipType.Rotate180FlipNone,
            270 => RotateFlipType.Rotate90FlipNone,
            _ => RotateFlipType.RotateNoneFlipNone,
        };
        if (back != RotateFlipType.RotateNoneFlipNone) bitmap.RotateFlip(back);
    }

    /// <summary>
    /// Pixel width for the requested resolution, reduced to whatever fits the
    /// screen the picture is for. Never rounds up past the requested DPI. Takes
    /// the region as the renderer will lay it out, so on a quarter-turned page
    /// the width asked for is the turned one.
    /// </summary>
    static int PixelWidthFor(PageRegion region, int targetDpi)
    {
        double scale = targetDpi / 72.0;
        double width = region.Width * scale;
        return Math.Max(1, (int)Math.Round(width * ScaleToFit(width, region.Height * scale)));
    }

    /// <summary>
    /// How much a picture of this size has to shrink to fit the screen: the
    /// tighter of the two ratios, and never an enlargement.
    /// </summary>
    static double ScaleToFit(double width, double height) => Math.Min(
        1.0,
        Math.Min(MaxPixelWidth / Math.Max(1.0, width), MaxPixelHeight / Math.Max(1.0, height)));

    async Task<PdfDocument?> LoadDocumentAsync(string filePath)
    {
        if (_openDocuments.TryGetValue(filePath, out var cached)) return cached;

        var file = await StorageFile.GetFileFromPathAsync(filePath);
        var document = await PdfDocument.LoadFromFileAsync(file);
        _openDocuments[filePath] = document;
        return document;
    }
}
