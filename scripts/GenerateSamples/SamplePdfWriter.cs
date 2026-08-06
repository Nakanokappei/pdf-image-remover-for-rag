// SamplePdfWriter — the single implementation of the spec §8.2 sample PDFs.
// Consumed by two callers:
//   * scripts/GenerateSamples (console) — regenerates samples/ for manual runs
//   * tests/PdfImageRemoverForRag.Infrastructure.Tests — generates per-fixture
//     copies into a temp directory so integration tests stay hermetic
// Keeping one writer guarantees the console samples and the test fixtures
// can never drift apart.
//
// The tool ships no bundled bitmaps and depends only on PDFsharp; it inlines
// a tiny PNG encoder so we do not pull in image libraries with restrictive
// licenses (SixLabors.ImageSharp) or native dependencies (SkiaSharp).

using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Snippets.Font;

namespace PdfImageRemoverForRag.Scripts.GenerateSamples;

/// <summary>
/// Writes the sample PDFs into a directory: the five spec §8.2 documents
/// (one-image / repeated-logo / multiple-images / image-and-text /
/// scanned-page) plus jpeg-image (DCTDecode path), repeated-text,
/// repeated-shapes, form-embedded-image (the not-safely-removable case), and
/// form-drawn-shapes (vector artwork inside a form, which analysis cannot see).
/// </summary>
public static class SamplePdfWriter
{
    /// <summary>
    /// Generate all sample PDFs into <paramref name="outputDirectory"/>
    /// (created if missing) and return the written file paths.
    /// </summary>
    public static IReadOnlyList<string> WriteAll(string outputDirectory)
    {
        EnsureFontResolver();
        Directory.CreateDirectory(outputDirectory);

        // Materialize the bitmaps once; the logo is reused across documents
        // and pages so downstream grouping-by-hash has something to prove.
        var logoPng = BuildPng(240, 80, (x, y, w, h) =>
            y < h / 2 ? ((byte)200, (byte)40, (byte)40) : ((byte)40, (byte)40, (byte)200));
        var photoPng = BuildPng(400, 300, (x, y, w, h) =>
        {
            // Diagonal gradient for a "photo-like" image.
            var t = (x + y) / (double)(w + h);
            return ((byte)(255 * t),
                    (byte)(128 + 60 * Math.Sin(x * 0.05)),
                    (byte)(255 * (1 - t)));
        });
        var iconPng = BuildPng(64, 64, (x, y, w, h) => ((byte)140, (byte)140, (byte)140));
        var scanPng = BuildPng(800, 1100, (x, y, w, h) =>
            (y / 40) % 2 == 0
                ? ((byte)245, (byte)245, (byte)240)
                : ((byte)220, (byte)220, (byte)210));

        var written = new List<string>
        {
            WriteOneImage(Path.Combine(outputDirectory, "one-image.pdf"), logoPng),
            WriteRepeatedLogo(Path.Combine(outputDirectory, "repeated-logo.pdf"), logoPng),
            WriteMultipleImages(Path.Combine(outputDirectory, "multiple-images.pdf"), logoPng, photoPng, iconPng),
            WriteImageAndText(Path.Combine(outputDirectory, "image-and-text.pdf"), photoPng),
            WriteRotatedPage(Path.Combine(outputDirectory, "rotated-page.pdf"), photoPng),
            WriteScannedPage(Path.Combine(outputDirectory, "scanned-page.pdf"), scanPng),
            WriteFullPageOverlap(Path.Combine(outputDirectory, "full-page-overlap.pdf"), scanPng),
            WriteJpegImage(Path.Combine(outputDirectory, "jpeg-image.pdf")),
            WriteRepeatedText(Path.Combine(outputDirectory, "repeated-text.pdf")),
            WriteRepeatedShapes(Path.Combine(outputDirectory, "repeated-shapes.pdf")),
            WriteFormEmbeddedImage(Path.Combine(outputDirectory, "form-embedded-image.pdf"), logoPng),
            WriteFormDrawnShapes(Path.Combine(outputDirectory, "form-drawn-shapes.pdf")),
            WriteSingleCharacterText(Path.Combine(outputDirectory, "single-character-text.pdf")),
            WriteSoftMaskedImage(Path.Combine(outputDirectory, "soft-masked-image.pdf")),
            WriteShadowLayer(Path.Combine(outputDirectory, "shadow-layer.pdf")),
            WriteAnnotationSharedImage(Path.Combine(outputDirectory, "annotation-shared-image.pdf")),
        };
        written.AddRange(WriteFlattenUnits(
            Path.Combine(outputDirectory, "flatten-units-a.pdf"),
            Path.Combine(outputDirectory, "flatten-units-b.pdf"),
            logoPng));
        return written;
    }

    /// <summary>
    /// Install a font resolver so PDFsharp works headlessly on macOS where
    /// no "Arial" is registered with the platform resolver. Idempotent.
    /// </summary>
    public static void EnsureFontResolver()
    {
        if (GlobalFontSettings.FontResolver is null)
        {
            GlobalFontSettings.FontResolver = new SegoeWpFontResolver();
        }
    }

    // -----------------------------------------------------------------------
    // Individual documents
    // -----------------------------------------------------------------------

    static string WriteOneImage(string path, byte[] logoPng)
    {
        using var doc = NewDocument("one-image sample");
        var page = doc.AddPage();
        using var gfx = XGraphics.FromPdfPage(page);
        using var img = XImage.FromStream(new MemoryStream(logoPng));

        // Draw logo near the top-left; leave the rest for text so that
        // removing the image still leaves the page with meaningful content.
        gfx.DrawImage(img, 40, 40, 240, 80);
        DrawParagraph(gfx, "One image sample",
            "This document contains a single embedded raster image.",
            "Removing it should leave only text on the page.");
        doc.Save(path);
        return path;
    }

    static string WriteRepeatedLogo(string path, byte[] logoPng)
    {
        using var doc = NewDocument("repeated-logo sample");
        for (int i = 1; i <= 5; i++)
        {
            var page = doc.AddPage();
            using var gfx = XGraphics.FromPdfPage(page);
            // Recreate the XImage per page — PDFsharp still deduplicates the
            // underlying stream when the source bytes match, which is what
            // the grouping logic downstream must detect.
            using var img = XImage.FromStream(new MemoryStream(logoPng));
            gfx.DrawImage(img, 40, 40, 240, 80);
            DrawParagraph(gfx, $"Repeated logo page {i}",
                $"This is page {i} of 5. The header image should appear on every page.",
                "The five-page document uses the same logo bitmap on each page.");
        }
        doc.Save(path);
        return path;
    }

    static string WriteMultipleImages(string path, byte[] logoPng, byte[] photoPng, byte[] iconPng)
    {
        using var doc = NewDocument("multiple-images sample");
        var page = doc.AddPage();
        using var gfx = XGraphics.FromPdfPage(page);
        using var logo = XImage.FromStream(new MemoryStream(logoPng));
        using var photo = XImage.FromStream(new MemoryStream(photoPng));
        using var icon = XImage.FromStream(new MemoryStream(iconPng));

        // Three distinct images on the same page.
        gfx.DrawImage(logo, 40, 40, 240, 80);
        gfx.DrawImage(photo, 40, 160, 300, 220);
        gfx.DrawImage(icon, 400, 160, 64, 64);
        DrawParagraph(gfx, "Multiple images sample",
            "Three separate images share this page.",
            "Removing one image must not affect the others.");
        doc.Save(path);
        return path;
    }

    static string WriteImageAndText(string path, byte[] photoPng)
    {
        using var doc = NewDocument("image-and-text sample");
        var page = doc.AddPage();
        using var gfx = XGraphics.FromPdfPage(page);
        using var photo = XImage.FromStream(new MemoryStream(photoPng));

        // Text sits on top of the image so we can verify that removing the
        // image leaves the text glyphs intact.
        gfx.DrawImage(photo, 40, 120, 500, 300);
        DrawParagraph(gfx, "Image and text overlay",
            "The paragraph glyphs are drawn on top of the raster image.",
            "After image removal the text should remain readable.");
        doc.Save(path);
        return path;
    }

    /// <summary>
    /// The same page as <see cref="WriteImageAndText"/>, carrying <c>/Rotate 90</c>.
    /// </summary>
    /// <remarks>
    /// A rotated page is where "content space" stops being a formality: a viewer
    /// turns the paper, this program does not, and the two only agree as long as
    /// every side keeps to its own space. Drawing the content BEFORE setting the
    /// entry keeps the content stream identical to the unrotated sample's, so a
    /// test can put the two documents' analysis side by side and any difference
    /// is the rotation's doing.
    /// </remarks>
    static string WriteRotatedPage(string path, byte[] photoPng)
    {
        using var doc = NewDocument("rotated-page sample");
        var page = doc.AddPage();
        using (var gfx = XGraphics.FromPdfPage(page))
        {
            using var photo = XImage.FromStream(new MemoryStream(photoPng));
            gfx.DrawImage(photo, 40, 120, 500, 300);
            DrawParagraph(gfx, "Image and text overlay",
                "The paragraph glyphs are drawn on top of the raster image.",
                "After image removal the text should remain readable.");
        }
        page.Rotate = 90;
        doc.Save(path);
        return path;
    }

    /// <summary>
    /// A scan with a caption typed over it: an overlap region that covers the
    /// whole page.
    /// </summary>
    /// <remarks>
    /// Flattening this turns the sheet into a single picture and leaves none of
    /// its text as text — the case the whole-page warning exists for, and one no
    /// other sample produces. <see cref="WriteScannedPage"/> is the same scan
    /// with nothing on it, so it stays a full-page IMAGE and not a region.
    /// </remarks>
    static string WriteFullPageOverlap(string path, byte[] scanPng)
    {
        using var doc = NewDocument("full-page-overlap sample");
        var page = doc.AddPage();
        using var gfx = XGraphics.FromPdfPage(page);
        using var scan = XImage.FromStream(new MemoryStream(scanPng));
        gfx.DrawImage(scan, 0, 0, page.Width.Point, page.Height.Point);
        gfx.DrawString(
            "Scanned page with a caption over it",
            new XFont("Segoe WP", 12, XFontStyleEx.Regular),
            XBrushes.Black, 60, 100);
        doc.Save(path);
        return path;
    }

    /// <summary>
    /// Two flatten units on one page that share an image, plus a companion file
    /// holding the same image — the document merging and splitting by hand can
    /// actually be tried on.
    /// </summary>
    /// <remarks>
    /// The panel lists the units the object selected on the LEFT takes part in,
    /// so two units only appear together when one object is in both: here the
    /// same logo, drawn at the top of the page and again lower down, each with a
    /// caption over it. The upper unit takes a filled rectangle as well, which
    /// gives it three members and something to split off.
    ///
    /// The captions are drawn on both pages because a text string has to be
    /// shown twice in a file before it counts as removable, and a unit needs two
    /// KINDS — one occurrence would leave the picture with nothing to overlap.
    /// Page 2 is offset so its objects are not value-equal to page 1's.
    ///
    /// <paramref name="companionPath"/> gets the same logo over a caption, which
    /// is what makes one image group span two files: without that the panel can
    /// never list units from two files at once, and the rule that refuses to
    /// merge across files cannot be tried.
    /// </remarks>
    static string[] WriteFlattenUnits(string path, string companionPath, byte[] logoPng)
    {
        var caption = new XFont("Segoe WP", 12, XFontStyleEx.Regular);

        // The page both operations are meant for: an upper unit of three
        // members and a lower one of two, far enough apart not to be detected
        // as one.
        using (var doc = NewDocument("flatten-units sample"))
        {
            var page = doc.AddPage();
            using (var gfx = XGraphics.FromPdfPage(page))
            {
                using var logo = XImage.FromStream(new MemoryStream(logoPng));
                gfx.DrawImage(logo, 60, 80, 160, 60);
                gfx.DrawString("Figure 1", caption, XBrushes.Black, 70, 125);
                gfx.DrawRectangle(XBrushes.Orange, 190, 95, 50, 30);

                gfx.DrawImage(logo, 60, 400, 160, 60);
                gfx.DrawString("Figure 2", caption, XBrushes.Black, 70, 445);

                DrawParagraph(gfx, "Merging and splitting flatten units",
                    "The logo above and the logo below are the same image.",
                    "Selecting it lists both units, which is what merging needs.");
            }

            // Second page: the captions repeat here so they count as removable
            // text, and its unit is what a cross-page attempt is refused on.
            var second = doc.AddPage();
            using (var gfx = XGraphics.FromPdfPage(second))
            {
                using var logo = XImage.FromStream(new MemoryStream(logoPng));
                gfx.DrawImage(logo, 90, 110, 160, 60);
                gfx.DrawString("Figure 1", caption, XBrushes.Black, 100, 155);
                gfx.DrawString("Figure 2", caption, XBrushes.Black, 100, 500);
            }
            doc.Save(path);
        }

        // The companion: the same logo, so one group reaches into both files.
        using (var doc = NewDocument("flatten-units companion sample"))
        {
            for (int i = 1; i <= 2; i++)
            {
                var page = doc.AddPage();
                using var gfx = XGraphics.FromPdfPage(page);
                using var logo = XImage.FromStream(new MemoryStream(logoPng));
                gfx.DrawImage(logo, 60, 80, 160, 60);
                gfx.DrawString("Figure 1", caption, XBrushes.Black, 70, 125);
                gfx.DrawString($"Companion page {i}.", caption, XBrushes.Black, 60, 200);
            }
            doc.Save(companionPath);
        }

        return new[] { path, companionPath };
    }

    static string WriteRepeatedShapes(string path)
    {
        // Three pages with a shared header rule and a shared border rectangle
        // (the repeated vector "noise" to remove) plus a unique diagonal line
        // per page (must survive). Exercises the repeated-shape removal path.
        using var doc = NewDocument("repeated-shapes sample");
        var body = new XFont("Segoe WP", 11, XFontStyleEx.Regular);
        for (int i = 1; i <= 3; i++)
        {
            var page = doc.AddPage();
            using var gfx = XGraphics.FromPdfPage(page);
            gfx.DrawLine(new XPen(XColors.Gray, 1), 40, 60, 500, 60);         // repeated header rule
            gfx.DrawRectangle(new XPen(XColors.Silver, 1), 40, 80, 460, 680); // repeated border
            gfx.DrawLine(new XPen(XColors.Black, 1), 40, 100 + i * 20, 200, 300); // unique diagonal
            // Same 30x30 blue square at a DIFFERENT position each page — one
            // group by shape+width+color even though positions differ.
            gfx.DrawRectangle(new XPen(XColors.Blue, 1), 100 + i * 40, 400, 30, 30);
            gfx.DrawString($"Page {i} body text.", body, XBrushes.Black, 60, 120);
        }
        doc.Save(path);
        return path;
    }

    static string WriteRepeatedText(string path)
    {
        // Three pages with a shared header and footer (the "noise" to remove)
        // plus a unique body line per page (must survive). Exercises the
        // repeated-text removal path end-to-end.
        using var doc = NewDocument("repeated-text sample");
        var heading = new XFont("Segoe WP", 14, XFontStyleEx.Bold);
        var body = new XFont("Segoe WP", 11, XFontStyleEx.Regular);
        for (int i = 1; i <= 3; i++)
        {
            var page = doc.AddPage();
            using var gfx = XGraphics.FromPdfPage(page);
            gfx.DrawString("CONFIDENTIAL", heading, XBrushes.Gray, 40, 40);        // repeated header
            gfx.DrawString($"Body paragraph unique to page {i}.", body, XBrushes.Black, 40, 120);
            gfx.DrawString("Company Footer 2026", body, XBrushes.Gray, 40, 780);   // repeated footer
        }
        doc.Save(path);
        return path;
    }

    static string WriteJpegImage(string path)
    {
        // PDFsharp embeds JPEG sources as DCTDecode streams unchanged, which
        // is the case PdfPig's TryGetPng cannot convert — the thumbnail
        // pipeline must fall back to raw-JPEG passthrough for this file.
        using var doc = NewDocument("jpeg-image sample");
        var page = doc.AddPage();
        using var gfx = XGraphics.FromPdfPage(page);
        using var img = XImage.FromStream(new MemoryStream(MinimalJpegBytes.Value));
        gfx.DrawImage(img, 40, 40, 200, 150);
        DrawParagraph(gfx, "JPEG image sample",
            "The embedded image uses DCTDecode (JPEG) compression.",
            "Thumbnails for it require the raw-JPEG passthrough path.");
        doc.Save(path);
        return path;
    }

    // Smallest well-formed baseline JPEG (1x1 pixel). Kept as base64 so the
    // repository ships no binary assets.
    static readonly Lazy<byte[]> MinimalJpegBytes = new(() => Convert.FromBase64String(
        "/9j/4AAQSkZJRgABAQEAYABgAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkSEw8UHRofHh0a" +
        "HBwgJC4nICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPC4zNDL/2wBDAQkJCQwLDBgNDRgyIRwhMjIyMjIy" +
        "MjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjL/wAARCAABAAEDASIA" +
        "AhEBAxEB/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQA" +
        "AAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3" +
        "ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWm" +
        "p6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/9oADAMB" +
        "AAIRAxEAPwD3+iiigD//2Q=="));

    static string WriteFormEmbeddedImage(string path, byte[] logoPng)
    {
        // An image drawn INSIDE a Form XObject, with the form itself drawn on
        // two pages. The analyzer must list the image but mark it not safely
        // removable (§14.3 — rewriting a shared form's content stream could
        // affect other pages), so the UI shows it grayed / unpressable. This
        // is the only sample producing that state; without it the disabled
        // row/tile could never be seen or screen-reader-tested.
        using var doc = NewDocument("form-embedded-image sample");
        var form = new XForm(doc, XUnit.FromPoint(260), XUnit.FromPoint(100));
        using (var formGfx = XGraphics.FromForm(form))
        {
            using var img = XImage.FromStream(new MemoryStream(logoPng));
            formGfx.DrawImage(img, 10, 10, 240, 80);
        }

        for (int i = 1; i <= 2; i++)
        {
            var page = doc.AddPage();
            using var gfx = XGraphics.FromPdfPage(page);
            gfx.DrawImage(form, 40, 40);
            DrawParagraph(gfx, $"Form-embedded image page {i}",
                "The header image lives inside a shared Form XObject.",
                "It must be listed as not safely removable.");
        }
        doc.Save(path);
        return path;
    }

    static string WriteFormDrawnShapes(string path)
    {
        // Vector artwork drawn INSIDE a Form XObject, with no image anywhere in
        // it. This is the shape of a real customer document whose person
        // silhouette and speech bubble never appeared in the object list:
        // analysis reads a page's own content stream for shapes and text, and
        // enters a form only to collect the images inside it, so a form that
        // paints nothing but paths contributes nothing to the list.
        //
        // The page also carries a plain border rectangle. That is the control:
        // it is discovered, which is what makes "nothing came from the form" a
        // real assertion rather than "nothing was found at all".
        using var doc = NewDocument("form-drawn-shapes sample");
        var icon = new XForm(doc, XUnit.FromPoint(120), XUnit.FromPoint(120));
        using (var iconGfx = XGraphics.FromForm(icon))
        {
            // Three paths and two paint operators, matching what the reported
            // document held: a filled head, a filled body, a stroked bubble.
            iconGfx.DrawEllipse(XBrushes.Gray, 30, 10, 40, 40);
            iconGfx.DrawRectangle(XBrushes.Gray, 20, 58, 60, 40);
            iconGfx.DrawRoundedRectangle(new XPen(XColors.Gray, 1), 75, 15, 40, 30, 8, 8);
        }

        var caption = new XFont("Segoe WP", 11, XFontStyleEx.Regular);
        for (int i = 1; i <= 2; i++)
        {
            var page = doc.AddPage();
            using var gfx = XGraphics.FromPdfPage(page);
            // Identical on both pages, so it groups into one entry drawn twice.
            gfx.DrawRectangle(new XPen(XColors.Silver, 1), 40, 80, 460, 680);
            // Both pages draw the same form object, low on the page where the
            // reported document put its icon.
            gfx.DrawImage(icon, 60, 600);
            // Unique per page: two or more characters but shown once, so the
            // repeated-text filter keeps it out and the assertions stay about
            // shapes.
            gfx.DrawString($"Form-drawn shapes, page {i}", caption, XBrushes.Black, 60, 120);
        }
        doc.Save(path);
        return path;
    }

    static string WriteSingleCharacterText(string path)
    {
        // The three ways a short string can be judged, on three pages so the
        // repetition filter has something to work with:
        //   "S"    one readable character, shown on every page — a
        //          confidentiality marking, and the case this sample exists for
        //   "   "  whitespace only, shown on every page — must stay out of the
        //          list, since the row would show nothing and removing it would
        //          join the words on either side
        //   "X"    one readable character shown ONCE — still filtered, because
        //          the repetition rule is unchanged
        using var doc = NewDocument("single-character-text sample");
        var marking = new XFont("Segoe WP", 12, XFontStyleEx.Bold);
        var body = new XFont("Segoe WP", 11, XFontStyleEx.Regular);
        for (int i = 1; i <= 3; i++)
        {
            var page = doc.AddPage();
            using var gfx = XGraphics.FromPdfPage(page);
            gfx.DrawString("S", marking, XBrushes.Gray, 520, 40);
            gfx.DrawString("   ", body, XBrushes.Black, 40, 60);
            gfx.DrawString($"Body paragraph unique to page {i}.", body, XBrushes.Black, 40, 120);
        }
        // On the last page only, so it cannot reach two showings.
        using (var gfx = XGraphics.FromPdfPage(doc.Pages[doc.PageCount - 1]))
        {
            gfx.DrawString("X", marking, XBrushes.Black, 40, 160);
        }
        doc.Save(path);
        return path;
    }

    static string WriteSoftMaskedImage(string path)
    {
        // An image carrying a /SMask: its alpha channel, stored as a separate
        // image object hanging off the parent's dictionary rather than off the
        // page's resources. Nothing in the app lists such a mask, and nothing
        // should — it is not an object a person put on the page. But removing
        // the parent has to take the mask with it, or it stays behind for any
        // tool that reads a PDF by walking objects, which is how a real
        // document's masks turned up in a RAG pipeline as black rectangles.
        //
        // This is the only sample producing one, so it is the only cover the
        // mask-deletion branch has. Built by hand rather than through XImage
        // because the point is to control the /SMask exactly.
        using var doc = NewDocument("soft-masked-image sample");
        var page = doc.AddPage();

        const int width = 64;
        const int height = 48;
        var rgb = new byte[width * height * 3];
        var alpha = new byte[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = (y * width) + x;
                rgb[i * 3] = (byte)(x * 4);
                rgb[(i * 3) + 1] = 80;
                rgb[(i * 3) + 2] = (byte)(y * 5);
                // Opaque down one edge and transparent elsewhere — the shape
                // that makes a mask read as a near-black rectangle when some
                // other tool extracts it as a picture in its own right.
                alpha[i] = (byte)(x < width / 4 ? 255 : 0);
            }
        }

        var mask = NewImageXObject(doc, width, height, "/DeviceGray", alpha);
        var image = NewImageXObject(doc, width, height, "/DeviceRGB", rgb);
        image.Elements["/SMask"] = mask.Reference;

        var xObjects = new PdfDictionary(doc);
        xObjects.Elements["/ImSoft"] = image.Reference;
        page.Resources.Elements["/XObject"] = xObjects;

        // Placed with a plain cm/Do pair, so the analyzer sees exactly one
        // occurrence at a known rectangle.
        var content = page.Contents.AppendContent();
        content.CreateStream(Encoding.ASCII.GetBytes("q 200 0 0 150 60 500 cm /ImSoft Do Q\n"));

        doc.Save(path);
        return path;
    }

    static string WriteShadowLayer(string path)
    {
        // The three images that decide what counts as a shadow, on one page.
        //
        // A shadow layer is one flat colour shaped by a soft mask — that is
        // what a drop shadow becomes when it is exported, because PDF has no
        // blur operator to draw one with. The other two are here to pin the
        // rule from both sides: a picture that also has a mask is NOT a shadow
        // (it has a picture in it), and a flat colour with no mask is NOT one
        // either (it is a filled rectangle the page shows as itself).
        using var doc = NewDocument("shadow-layer sample");
        var page = doc.AddPage();

        const int width = 48;
        const int height = 32;
        var pixels = width * height;

        // The shadow: pure black everywhere, its outline held by the mask,
        // which fades from the middle outward the way a blur does.
        var shadowRgb = new byte[pixels * 3];
        var shadowAlpha = new byte[pixels];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = (y * width) + x;
                int fromEdge = Math.Min(Math.Min(x, width - 1 - x), Math.Min(y, height - 1 - y));
                shadowAlpha[i] = (byte)Math.Min(255, fromEdge * 24);
            }
        }

        // The picture: many colours, and a mask of its own, so a test can tell
        // that the mask is not what makes a shadow.
        var pictureRgb = new byte[pixels * 3];
        var pictureAlpha = new byte[pixels];
        for (int i = 0; i < pixels; i++)
        {
            pictureRgb[i * 3] = (byte)(i % 251);
            pictureRgb[(i * 3) + 1] = (byte)(i % 199);
            pictureRgb[(i * 3) + 2] = (byte)(i % 97);
            pictureAlpha[i] = 255;
        }

        // The flat fill: one colour, no mask.
        var fillRgb = new byte[pixels * 3];
        for (int i = 0; i < pixels; i++)
        {
            fillRgb[i * 3] = 200;
            fillRgb[(i * 3) + 1] = 40;
            fillRgb[(i * 3) + 2] = 40;
        }

        var shadow = NewImageXObject(doc, width, height, "/DeviceRGB", shadowRgb);
        shadow.Elements["/SMask"] =
            NewImageXObject(doc, width, height, "/DeviceGray", shadowAlpha).Reference;
        var picture = NewImageXObject(doc, width, height, "/DeviceRGB", pictureRgb);
        picture.Elements["/SMask"] =
            NewImageXObject(doc, width, height, "/DeviceGray", pictureAlpha).Reference;
        var fill = NewImageXObject(doc, width, height, "/DeviceRGB", fillRgb);

        var xObjects = new PdfDictionary(doc);
        xObjects.Elements["/ImShadow"] = shadow.Reference;
        xObjects.Elements["/ImPicture"] = picture.Reference;
        xObjects.Elements["/ImFill"] = fill.Reference;
        page.Resources.Elements["/XObject"] = xObjects;

        var content = page.Contents.AppendContent();
        content.CreateStream(Encoding.ASCII.GetBytes(
            "q 120 0 0 80 60 600 cm /ImShadow Do Q\n" +
            "q 120 0 0 80 60 480 cm /ImPicture Do Q\n" +
            "q 120 0 0 80 60 360 cm /ImFill Do Q\n"));

        doc.Save(path);
        return path;
    }

    static string WriteAnnotationSharedImage(string path)
    {
        // The same image drawn on the page AND used as an annotation's
        // appearance stream. Analysis only ever looks at page resources, so it
        // lists this image and lets the user remove it — but the annotation
        // still points at the object, and deleting it would leave a reference
        // pointing at nothing.
        //
        // Removal must therefore drop the page's reference and KEEP the object.
        // This is the sample that proves the difference, and the only one where
        // an image survives a removal that succeeded.
        using var doc = NewDocument("annotation-shared-image sample");
        var page = doc.AddPage();

        const int width = 32;
        const int height = 32;
        var rgb = new byte[width * height * 3];
        for (int i = 0; i < width * height; i++)
        {
            rgb[i * 3] = 30;
            rgb[(i * 3) + 1] = (byte)(i % 256);
            rgb[(i * 3) + 2] = 200;
        }
        var image = NewImageXObject(doc, width, height, "/DeviceRGB", rgb);

        // Drawn on the page in the ordinary way.
        var pageXObjects = new PdfDictionary(doc);
        pageXObjects.Elements["/ImShared"] = image.Reference;
        page.Resources.Elements["/XObject"] = pageXObjects;
        var content = page.Contents.AppendContent();
        content.CreateStream(Encoding.ASCII.GetBytes("q 80 0 0 80 60 600 cm /ImShared Do Q\n"));

        // And used again by a stamp annotation, whose appearance stream is a
        // Form XObject with resources of its own.
        var appearance = new PdfDictionary(doc);
        appearance.Elements["/Type"] = new PdfName("/XObject");
        appearance.Elements["/Subtype"] = new PdfName("/Form");
        appearance.Elements["/BBox"] = NewRectangle(0, 0, 80, 80);
        var formResources = new PdfDictionary(doc);
        var formXObjects = new PdfDictionary(doc);
        formXObjects.Elements["/ImShared"] = image.Reference;
        formResources.Elements["/XObject"] = formXObjects;
        appearance.Elements["/Resources"] = formResources;
        appearance.CreateStream(Encoding.ASCII.GetBytes("q 80 0 0 80 0 0 cm /ImShared Do Q\n"));
        doc.Internals.AddObject(appearance);

        var annotation = new PdfDictionary(doc);
        annotation.Elements["/Type"] = new PdfName("/Annot");
        annotation.Elements["/Subtype"] = new PdfName("/Stamp");
        annotation.Elements["/Rect"] = NewRectangle(300, 600, 380, 680);
        annotation.Elements["/F"] = new PdfInteger(4);   // printable
        var appearanceStates = new PdfDictionary(doc);
        appearanceStates.Elements["/N"] = appearance.Reference;
        annotation.Elements["/AP"] = appearanceStates;
        doc.Internals.AddObject(annotation);

        var annots = new PdfArray(doc);
        // AddObject has just given it one.
        annots.Elements.Add(annotation.Reference!);
        page.Elements["/Annots"] = annots;

        doc.Save(path);
        return path;
    }

    static PdfArray NewRectangle(double x1, double y1, double x2, double y2)
    {
        var rect = new PdfArray();
        foreach (var value in new[] { x1, y1, x2, y2 }) rect.Elements.Add(new PdfReal(value));
        return rect;
    }

    /// <summary>
    /// A bare Image XObject with its samples stored uncompressed. No filter,
    /// because what this sample exercises is whether the object is removed,
    /// not whether it can be decoded.
    /// </summary>
    static PdfDictionary NewImageXObject(
        PdfDocument doc, int width, int height, string colorSpace, byte[] samples)
    {
        var dict = new PdfDictionary(doc);
        dict.Elements["/Type"] = new PdfName("/XObject");
        dict.Elements["/Subtype"] = new PdfName("/Image");
        dict.Elements["/Width"] = new PdfInteger(width);
        dict.Elements["/Height"] = new PdfInteger(height);
        dict.Elements["/ColorSpace"] = new PdfName(colorSpace);
        dict.Elements["/BitsPerComponent"] = new PdfInteger(8);
        dict.CreateStream(samples);
        doc.Internals.AddObject(dict);
        return dict;
    }

    static string WriteScannedPage(string path, byte[] scanPng)
    {
        using var doc = NewDocument("scanned-page sample");
        var page = doc.AddPage();
        using var gfx = XGraphics.FromPdfPage(page);
        using var scan = XImage.FromStream(new MemoryStream(scanPng));

        // The image fills essentially the whole page — this is the "possible
        // full-page image" case that analysis must flag as a warning.
        gfx.DrawImage(scan, 0, 0, page.Width.Point, page.Height.Point);
        doc.Save(path);
        return path;
    }

    // -----------------------------------------------------------------------
    // Drawing helpers
    // -----------------------------------------------------------------------

    static PdfDocument NewDocument(string title)
    {
        var doc = new PdfDocument();
        doc.Info.Title = title;
        doc.Info.Creator = "PdfImageRemoverForRag.SamplePdfWriter";
        return doc;
    }

    static void DrawParagraph(XGraphics gfx, string heading, params string[] paragraphs)
    {
        // Draw a heading and one line per paragraph, starting below the image
        // area so image removal and text retention can be verified separately.
        var headingFont = new XFont("Segoe WP", 16, XFontStyleEx.Bold);
        var bodyFont = new XFont("Segoe WP", 11, XFontStyleEx.Regular);

        double y = 260;
        gfx.DrawString(heading, headingFont, XBrushes.Black, 40, y);
        y += 28;
        foreach (var line in paragraphs)
        {
            gfx.DrawString(line, bodyFont, XBrushes.Black, 40, y);
            y += 18;
        }
    }

    static byte[] BuildPng(int width, int height,
        Func<int, int, int, int, (byte r, byte g, byte b)> pixel)
    {
        // Build an RGB pixel buffer and encode it as a minimal PNG.
        var rgb = new byte[width * height * 3];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var (r, g, b) = pixel(x, y, width, height);
                int i = (y * width + x) * 3;
                rgb[i] = r;
                rgb[i + 1] = g;
                rgb[i + 2] = b;
            }
        }
        return MinimalPng.EncodeRgb(width, height, rgb);
    }
}

/// <summary>
/// Minimal PNG encoder (RGB, 8-bit, no interlace, filter 0). Just enough to
/// feed PDFsharp's XImage.FromStream without an external image library — which
/// is also what a test needs to stand in for the operating system's PDF
/// rasterizer, so it is public rather than duplicated over there.
/// </summary>
public static class MinimalPng
{
    public static byte[] EncodeRgb(int width, int height, byte[] rgbPixels)
    {
        using var ms = new MemoryStream();
        // PNG signature.
        ms.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        WriteIhdr(ms, width, height);
        WriteIdat(ms, width, height, rgbPixels);
        WriteChunk(ms, "IEND", Array.Empty<byte>());
        return ms.ToArray();
    }

    static void WriteIhdr(Stream s, int w, int h)
    {
        // IHDR payload is 13 bytes: width, height, depth, color type,
        // compression, filter, interlace.
        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(0, 4), w);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4, 4), h);
        ihdr[8] = 8;  // bit depth
        ihdr[9] = 2;  // color type: truecolor RGB
        ihdr[10] = 0; // compression method
        ihdr[11] = 0; // filter method
        ihdr[12] = 0; // interlace method
        WriteChunk(s, "IHDR", ihdr);
    }

    static void WriteIdat(Stream s, int w, int h, byte[] pixels)
    {
        // Prepend the "no filter" byte (0x00) to every scanline before zlib.
        int rowLen = w * 3;
        var withFilters = new byte[(rowLen + 1) * h];
        for (int y = 0; y < h; y++)
        {
            withFilters[y * (rowLen + 1)] = 0;
            Array.Copy(pixels, y * rowLen, withFilters, y * (rowLen + 1) + 1, rowLen);
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(withFilters, 0, withFilters.Length);
        }
        WriteChunk(s, "IDAT", compressed.ToArray());
    }

    static void WriteChunk(Stream s, string type, byte[] data)
    {
        // Chunk = length (BE u32) + type (4 ASCII) + data + CRC32 (BE u32
        // over type+data).
        var lenBytes = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(lenBytes, data.Length);
        s.Write(lenBytes);

        var typeBytes = Encoding.ASCII.GetBytes(type);
        s.Write(typeBytes);
        s.Write(data);

        var crcInput = new byte[typeBytes.Length + data.Length];
        Buffer.BlockCopy(typeBytes, 0, crcInput, 0, typeBytes.Length);
        Buffer.BlockCopy(data, 0, crcInput, typeBytes.Length, data.Length);

        var crcBytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, Crc32(crcInput));
        s.Write(crcBytes);
    }

    static uint Crc32(byte[] data)
    {
        // Standard CRC-32 (polynomial 0xEDB88320), matching zlib and PNG spec.
        var table = Crc32Table.Value;
        uint crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc = table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }
        return crc ^ 0xFFFFFFFFu;
    }

    static readonly Lazy<uint[]> Crc32Table = new(() =>
    {
        var t = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }
            t[n] = c;
        }
        return t;
    });
}
