using PdfImageRemoverForRag.Core.Models;
using Xunit;

namespace PdfImageRemoverForRag.Core.Tests;

// The ceilings a saved image is held to, and the arithmetic that turns the
// user's choice into a number of pixels.
public class ImageReductionTests
{
    // A4 portrait, in points.
    const double A4Width = 595.28;
    const double A4Height = 841.89;

    [Fact]
    public void AResolutionBecomesTheSizeOfThePageAtThatResolution()
    {
        var reduction = new ImageReduction(true, ImageSizeLimit.RagFinePrint, 85);

        var (width, height) = reduction.CeilingFor(A4Width, A4Height);

        // 8.27 x 11.69 inches at 300 dpi.
        Assert.Equal(2480, width);
        Assert.Equal(3508, height);
    }

    [Fact]
    public void AResolutionFollowsThePageWhenThePageLiesDown()
    {
        // The point of measuring against the page: a landscape page is not
        // punished for its shape the way a fixed landscape box punishes a
        // portrait one.
        var reduction = new ImageReduction(true, ImageSizeLimit.RagComplexScripts, 85);

        var (width, height) = reduction.CeilingFor(A4Height, A4Width);

        Assert.Equal(2339, width);
        Assert.Equal(1654, height);
    }

    [Fact]
    public void TheLowestRungFollowsThePageLikeEveryOtherOne()
    {
        // It used to be a 1920 x 1080 box, which made it the one entry that
        // could not be compared with the rest. It is a resolution now, and a
        // resolution turns over with the page.
        var reduction = new ImageReduction(true, ImageSizeLimit.Screen, 85);

        Assert.Equal((761, 1076), reduction.CeilingFor(A4Width, A4Height));
        Assert.Equal((1076, 761), reduction.CeilingFor(A4Height, A4Width));
    }

    [Fact]
    public void APageOfNoSizeFallsBackToA4RatherThanToNothing()
    {
        // A ceiling of zero would fit every image down to a single pixel.
        var reduction = new ImageReduction(true, ImageSizeLimit.RagLatin, 85);

        Assert.Equal(reduction.CeilingFor(A4Width, A4Height), reduction.CeilingFor(0, 0));
    }

    [Fact]
    public void TheListReadsAsALadder()
    {
        // The settings window orders the entries by resolution and says so in
        // its own comment. If two ever collide, one of them has stopped being a
        // distinct choice and the list has an entry that means nothing.
        var resolutions = Enum.GetValues<ImageSizeLimit>()
            .Select(ImageReduction.DpiOf)
            .ToArray();

        Assert.Equal(resolutions.Distinct().Count(), resolutions.Length);
        Assert.Equal(new[] { 92, 140, 200, 300, 400 }, resolutions.OrderBy(dpi => dpi));
    }

    [Theory]
    [InlineData(0, ImageReduction.MinimumJpegQuality)]
    [InlineData(49, ImageReduction.MinimumJpegQuality)]
    [InlineData(85, 85)]
    [InlineData(101, ImageReduction.MaximumJpegQuality)]
    public void AQualityFromOutsideTheRangeIsBroughtBackInside(int given, int expected)
    {
        // settings.json is a file a person can open and type into, and a
        // quality of zero would write unreadable JPEGs into every output.
        Assert.Equal(expected, new ImageReduction(true, ImageSizeLimit.RagLatin, given).JpegQuality);
    }

    [Fact]
    public void TheLargestCeilingOnOfferIsTheOneUsedAsTheAbsoluteCap()
    {
        // Anything this app rasterizes is capped there even when reduction is
        // switched off, so the cap has to be the biggest thing on the list
        // rather than a number of its own that could drift below it.
        int largest = Enum.GetValues<ImageSizeLimit>().Max(ImageReduction.DpiOf);

        Assert.Equal(largest, ImageReduction.DpiOf(ImageReduction.AbsoluteCeiling));
    }

    [Fact]
    public void SwitchedOffIsAStateAndNotTheAbsenceOfOne()
    {
        Assert.False(ImageReduction.Off.Enabled);
        Assert.Equal(ImageReduction.DefaultJpegQuality, ImageReduction.Off.JpegQuality);
    }
}
