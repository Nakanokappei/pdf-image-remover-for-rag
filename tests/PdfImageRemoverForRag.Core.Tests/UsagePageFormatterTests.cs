using PdfImageRemoverForRag.Core.Formatting;
using Xunit;

namespace PdfImageRemoverForRag.Core.Tests;

// Spec §24 "使用ページ範囲の表示" and §11.3 (list vs "1〜50").
public class UsagePageFormatterTests
{
    [Fact]
    public void Empty_ReturnsEmptyString()
    {
        Assert.Equal(string.Empty, UsagePageFormatter.Format(Array.Empty<int>()));
    }

    [Fact]
    public void SinglePage_ReturnsBareNumber()
    {
        Assert.Equal("7", UsagePageFormatter.Format(new[] { 7 }));
    }

    [Fact]
    public void TwoConsecutivePages_ListsThemSeparately()
    {
        // Only three-or-longer runs collapse to a range — a run of two would
        // become "1〜2" which is longer than "1, 2" and less readable.
        Assert.Equal("1, 2", UsagePageFormatter.Format(new[] { 1, 2 }));
    }

    [Fact]
    public void ThreeConsecutivePages_UseRangeWithWaveDash()
    {
        Assert.Equal("1〜3", UsagePageFormatter.Format(new[] { 1, 2, 3 }));
    }

    [Fact]
    public void FiftyConsecutivePages_UseSingleRange()
    {
        // Matches the spec's "1〜50" example.
        Assert.Equal("1〜50", UsagePageFormatter.Format(Enumerable.Range(1, 50)));
    }

    [Fact]
    public void MixedRunsAndSingletons_AreJoinedWithCommas()
    {
        Assert.Equal("1〜3, 5, 7〜9",
            UsagePageFormatter.Format(new[] { 1, 2, 3, 5, 7, 8, 9 }));
    }

    [Fact]
    public void DuplicateAndOutOfOrder_AreNormalized()
    {
        // The formatter tolerates upstream sloppy input.
        Assert.Equal("1〜3", UsagePageFormatter.Format(new[] { 3, 1, 2, 2, 1 }));
    }
}
