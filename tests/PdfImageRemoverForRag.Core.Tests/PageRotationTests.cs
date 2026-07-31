using PdfImageRemoverForRag.Core.Models;
using Xunit;

namespace PdfImageRemoverForRag.Core.Tests;

// The mapping a rotated page needs when a rectangle found in content space has
// to be handed to something that draws the page the way a VIEWER does.
//
// The fixture is a 400x600 page with a 100x100 square in the content's
// bottom-left corner. Where that square appears on screen is the whole question,
// and each rotation has one right answer — which is why the assertions name
// corners rather than repeat the arithmetic.
public class PageRotationTests
{
    const double PageWidth = 400, PageHeight = 600;
    static readonly PageRegion BottomLeftSquare = new(0, 0, 100, 100);

    static PageRegion Displayed(PageRegion region, int rotation) =>
        PageRotation.ToDisplay(region, PageWidth, PageHeight, rotation);

    [Fact]
    public void WithoutRotation_TheMappingIsThePlainYFlip()
    {
        // The content's bottom-left corner is the display's bottom-left corner.
        var displayed = Displayed(BottomLeftSquare, 0);
        Assert.Equal(0, displayed.X);
        Assert.Equal(500, displayed.Y);
        Assert.Equal(100, displayed.Width);
        Assert.Equal(100, displayed.Height);
    }

    [Fact]
    public void AQuarterTurnClockwise_PutsTheContentsBottomLeftAtTheDisplaysTopLeft()
    {
        // Turn a portrait sheet clockwise and its left edge becomes the top one.
        var displayed = Displayed(BottomLeftSquare, 90);
        Assert.Equal(0, displayed.X);
        Assert.Equal(0, displayed.Y);
    }

    [Fact]
    public void AHalfTurn_PutsItAtTheDisplaysTopRight()
    {
        var displayed = Displayed(BottomLeftSquare, 180);
        Assert.Equal(300, displayed.X);
        Assert.Equal(0, displayed.Y);
    }

    [Fact]
    public void AQuarterTurnAntiClockwise_PutsItAtTheDisplaysBottomRight()
    {
        // 270 clockwise is a quarter turn the other way: the bottom edge goes to
        // the right, so the display is 600x400 and the square lands in its far
        // corner.
        var displayed = Displayed(BottomLeftSquare, 270);
        Assert.Equal(500, displayed.X);
        Assert.Equal(300, displayed.Y);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public void AQuarterTurnSwapsTheRectanglesSides_AndAHalfTurnDoesNot(int rotation)
    {
        var displayed = Displayed(new PageRegion(30, 40, 100, 200), rotation);
        bool quarter = rotation is 90 or 270;
        Assert.Equal(quarter ? 200 : 100, displayed.Width);
        Assert.Equal(quarter ? 100 : 200, displayed.Height);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public void AnyRectangleOnThePage_MapsOntoTheDisplayedPage(int rotation)
    {
        // The defect this guards is the one that was shipped: a rectangle mapped
        // with the wrong rule landed off the paper, and the OS renderer answered
        // a rotated page's flatten request with a blank image.
        var (displayWidth, displayHeight) =
            PageRotation.DisplaySize(PageWidth, PageHeight, rotation);
        foreach (var region in new PageRegion[]
        {
            new(0, 0, 100, 100),
            new(300, 500, 100, 100),
            new(0, 0, PageWidth, PageHeight),
            new(150, 250, 40, 30),
        })
        {
            var displayed = Displayed(region, rotation);
            Assert.InRange(displayed.X, 0, displayWidth);
            Assert.InRange(displayed.Y, 0, displayHeight);
            Assert.InRange(displayed.X + displayed.Width, 0, displayWidth);
            Assert.InRange(displayed.Y + displayed.Height, 0, displayHeight);
        }
    }

    [Fact]
    public void TheDisplayedPageSize_SwapsOnAQuarterTurnOnly()
    {
        Assert.Equal((PageWidth, PageHeight), PageRotation.DisplaySize(PageWidth, PageHeight, 0));
        Assert.Equal((PageHeight, PageWidth), PageRotation.DisplaySize(PageWidth, PageHeight, 90));
        Assert.Equal((PageWidth, PageHeight), PageRotation.DisplaySize(PageWidth, PageHeight, 180));
        Assert.Equal((PageHeight, PageWidth), PageRotation.DisplaySize(PageWidth, PageHeight, 270));
    }

    [Theory]
    [InlineData(-90, 270)]
    [InlineData(450, 90)]
    [InlineData(720, 0)]
    // Not a multiple of 90, so not a rotation a viewer can apply: treated as none.
    [InlineData(45, 0)]
    public void TheRotationEntryIsReducedToAQuarterTurn(int entry, int expected)
    {
        Assert.Equal(expected, PageRotation.Normalize(entry));
    }
}
