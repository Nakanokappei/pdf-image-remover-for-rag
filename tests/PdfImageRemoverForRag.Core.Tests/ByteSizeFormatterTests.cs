using PdfImageRemoverForRag.Core.Formatting;
using Xunit;

namespace PdfImageRemoverForRag.Core.Tests;

// "推定容量" column formatting (spec §11.3: KB or MB).
public class ByteSizeFormatterTests
{
    [Fact]
    public void Zero_FormatsAsBytes()
    {
        Assert.Equal("0 B", ByteSizeFormatter.Format(0));
    }

    [Fact]
    public void BelowOneKilobyte_FormatsAsBytes()
    {
        Assert.Equal("84 B", ByteSizeFormatter.Format(84));
    }

    [Fact]
    public void KilobyteRange_UsesOneDecimalAndTrimsTrailingZero()
    {
        Assert.Equal("1.5 KB", ByteSizeFormatter.Format(1536));
        // 12288 / 1024 = 12.0 → trailing zero trimmed.
        Assert.Equal("12 KB", ByteSizeFormatter.Format(12_288));
    }

    [Fact]
    public void MegabyteRange_FormatsWithMbUnit()
    {
        Assert.Equal("5 MB", ByteSizeFormatter.Format(5L * 1024 * 1024));
        Assert.Equal("2.5 MB", ByteSizeFormatter.Format((long)(2.5 * 1024 * 1024)));
    }

    [Fact]
    public void GigabyteRange_FormatsWithGbUnit()
    {
        Assert.Equal("3 GB", ByteSizeFormatter.Format(3L * 1024 * 1024 * 1024));
    }
}
