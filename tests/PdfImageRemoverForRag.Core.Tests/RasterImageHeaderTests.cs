using PdfImageRemoverForRag.Core.Validation;
using Xunit;

namespace PdfImageRemoverForRag.Core.Tests;

/// <summary>
/// The guard that keeps a hostile image away from the decoder. The cases that
/// matter are the ones a normal file never produces: a tiny header declaring an
/// enormous picture, and a format nobody recognises.
/// </summary>
public class RasterImageHeaderTests
{
    static byte[] Png(int width, int height)
    {
        // Signature, then the IHDR chunk: length, type, width, height.
        var bytes = new byte[24];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(bytes, 0);
        bytes[11] = 13; // IHDR payload length
        "IHDR"u8.ToArray().CopyTo(bytes, 12);
        WriteBigEndian(bytes, 16, width);
        WriteBigEndian(bytes, 20, height);
        return bytes;

        static void WriteBigEndian(byte[] target, int offset, int value)
        {
            target[offset] = (byte)(value >> 24);
            target[offset + 1] = (byte)(value >> 16);
            target[offset + 2] = (byte)(value >> 8);
            target[offset + 3] = (byte)value;
        }
    }

    static byte[] Jpeg(int width, int height) => new byte[]
    {
        0xFF, 0xD8,                   // SOI
        0xFF, 0xC0, 0x00, 0x11, 0x08, // SOF0, length 17, 8-bit precision
        (byte)(height >> 8), (byte)height,
        (byte)(width >> 8), (byte)width,
        0x03, 0x01, 0x11, 0x00, 0x02, 0x11, 0x01, 0x03, 0x11, 0x01,
    };

    [Fact]
    public void ReadsPngDimensions()
    {
        Assert.Equal((1920, 1080), RasterImageHeader.TryReadDimensions(Png(1920, 1080)));
    }

    [Fact]
    public void ReadsJpegDimensions()
    {
        Assert.Equal((800, 600), RasterImageHeader.TryReadDimensions(Jpeg(800, 600)));
    }

    [Fact]
    public void AcceptsAnOrdinaryScannedPage()
    {
        // 600 dpi A4 — the largest thing a user plausibly puts in a PDF.
        Assert.True(RasterImageHeader.IsSafeToDecode(Png(4960, 7016)));
    }

    [Fact]
    public void RejectsADecompressionBomb()
    {
        // A few bytes of header declaring 1.6 billion pixels — roughly 6 GB
        // once decoded. This is the case the guard exists for.
        Assert.False(RasterImageHeader.IsSafeToDecode(Png(40000, 40000)));
    }

    [Fact]
    public void RejectsDimensionsThatWouldOverflowAnIntProduct()
    {
        // 65536 * 65536 wraps to 0 when multiplied as int; the check must not
        // be fooled into reading that as "no pixels, therefore harmless".
        Assert.False(RasterImageHeader.IsSafeToDecode(Png(65536, 65536)));
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(100, 0)]
    [InlineData(-1, 100)]
    public void RejectsNonsenseDimensions(int width, int height)
    {
        Assert.False(RasterImageHeader.IsSafeToDecode(Png(width, height)));
    }

    [Fact]
    public void RejectsAFormatItCannotRead()
    {
        // Only PNG and JPEG reach this code, so anything else is refused rather
        // than waved through to the decoder.
        Assert.False(RasterImageHeader.IsSafeToDecode("GIF89a still an image"u8));
    }

    [Fact]
    public void RejectsTruncatedHeaders()
    {
        Assert.False(RasterImageHeader.IsSafeToDecode(Png(100, 100).AsSpan(0, 20)));
        Assert.False(RasterImageHeader.IsSafeToDecode(ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void RejectsAPngWhoseFirstChunkIsNotIhdr()
    {
        var bytes = Png(100, 100);
        "IDAT"u8.ToArray().CopyTo(bytes, 12);
        Assert.False(RasterImageHeader.IsSafeToDecode(bytes));
    }
}
