using PdfImageRemoverForRag.Core.Imaging;
using Xunit;

namespace PdfImageRemoverForRag.Core.Tests;

// Reading a JPEG's quality back out of it, so a picture is never re-encoded
// ABOVE the quality it already has — which costs bytes and returns no detail.
public class JpegQualityTests
{
    // The luminance table the JPEG standard suggests. An encoder scales this by
    // a factor derived from the quality, and the test writes a file the same way
    // a real encoder would.
    static readonly int[] Standard =
    {
        16, 11, 10, 16, 24, 40, 51, 61,
        12, 12, 14, 19, 26, 58, 60, 55,
        14, 13, 16, 24, 40, 57, 69, 56,
        14, 17, 22, 29, 51, 87, 80, 62,
        18, 22, 37, 56, 68, 109, 103, 77,
        24, 35, 55, 64, 81, 104, 113, 92,
        49, 64, 78, 87, 103, 121, 120, 101,
        72, 92, 95, 98, 112, 100, 103, 99,
    };

    /// <summary>A JPEG holding nothing but the quantization table for a quality.</summary>
    static byte[] JpegWithQuality(int quality)
    {
        int scale = quality < 50 ? 5000 / quality : 200 - (quality * 2);
        var bytes = new List<byte> { 0xFF, 0xD8, 0xFF, 0xDB, 0x00, 0x43, 0x00 };
        foreach (int entry in Standard)
        {
            bytes.Add((byte)Math.Clamp(((entry * scale) + 50) / 100, 1, 255));
        }
        bytes.AddRange(new byte[] { 0xFF, 0xD9 });
        return bytes.ToArray();
    }

    [Theory]
    [InlineData(50)]
    [InlineData(75)]
    [InlineData(85)]
    [InlineData(95)]
    public void TheQualityAFileWasWrittenAtIsReadBack(int quality)
    {
        var estimated = JpegQuality.Estimate(JpegWithQuality(quality));

        // Within a point: the tables are integers and several qualities round to
        // the same ones. Which side of the ceiling it falls is what matters.
        Assert.NotNull(estimated);
        Assert.InRange(estimated!.Value, quality - 1, quality + 1);
    }

    [Fact]
    public void APictureBelowTheCeilingIsRecognizedAsBelowIt()
    {
        // The question this is asked in practice: may it be encoded at 85, or
        // would that be an increase?
        Assert.True(JpegQuality.Estimate(JpegWithQuality(60)) < 85);
        Assert.True(JpegQuality.Estimate(JpegWithQuality(92)) > 85);
    }

    [Fact]
    public void BytesThatAreNotAJpegAnswerNothing()
    {
        // A caller that cannot learn the quality must leave the image alone
        // rather than assume one.
        Assert.Null(JpegQuality.Estimate(Array.Empty<byte>()));
        Assert.Null(JpegQuality.Estimate(new byte[] { 0x89, 0x50, 0x4E, 0x47 }));
        // A JPEG that carries no quantization table before its scan.
        Assert.Null(JpegQuality.Estimate(new byte[] { 0xFF, 0xD8, 0xFF, 0xDA, 0x00, 0x02 }));
    }
}
