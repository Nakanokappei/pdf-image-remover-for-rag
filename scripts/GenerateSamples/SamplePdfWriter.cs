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
/// repeated-shapes, and form-embedded-image (the not-safely-removable case).
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
            WriteScannedPage(Path.Combine(outputDirectory, "scanned-page.pdf"), scanPng),
            WriteJpegImage(Path.Combine(outputDirectory, "jpeg-image.pdf")),
            WriteRepeatedText(Path.Combine(outputDirectory, "repeated-text.pdf")),
            WriteRepeatedShapes(Path.Combine(outputDirectory, "repeated-shapes.pdf")),
            WriteFormEmbeddedImage(Path.Combine(outputDirectory, "form-embedded-image.pdf"), logoPng),
        };
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
/// feed PDFsharp's XImage.FromStream without an external image library.
/// </summary>
internal static class MinimalPng
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
