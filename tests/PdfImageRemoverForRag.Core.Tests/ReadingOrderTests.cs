using PdfImageRemoverForRag.Core.Grouping;
using PdfImageRemoverForRag.Core.Models;
using Xunit;

namespace PdfImageRemoverForRag.Core.Tests;

// The order the Flatten panel lists things in: down the page, and left to right
// within a line. PDF coordinates grow upwards, so "the top" is the largest Y.
public class ReadingOrderTests
{
    // A 780x540 landscape page, the shape this project's own sample decks use.
    const double PageHeight = 540;

    static PlacedObject At(string identity, double left, double topFromPageTop,
        double width = 40, double height = 12) =>
        new(RemovableKind.Text, identity, left, PageHeight - topFromPageTop - height, width, height);

    static string[] Order(params PlacedObject[] objects) =>
        ReadingOrder.Sort(objects).Select(o => o.Identity).ToArray();

    [Fact]
    public void HigherOnThePage_ComesFirst()
    {
        Assert.Equal(
            new[] { "top", "bottom" },
            Order(At("bottom", left: 40, topFromPageTop: 300), At("top", left: 40, topFromPageTop: 40)));
    }

    [Fact]
    public void OnTheSameLine_TheLeftmostComesFirst()
    {
        // Tops 4 pt apart: one line as a reader sees it, so the left one leads
        // even though the other starts marginally higher.
        Assert.Equal(
            new[] { "left", "right" },
            Order(At("right", left: 400, topFromPageTop: 100),
                  At("left", left: 60, topFromPageTop: 104)));
    }

    [Fact]
    public void ATopRightHeading_BeatsALowerLeftFigure()
    {
        // The case that decided the rule. By straight-line distance from the
        // page corner the figure wins (300 pt against 600), but the reader
        // meets the heading first.
        Assert.Equal(
            new[] { "heading", "figure" },
            Order(At("figure", left: 30, topFromPageTop: 300),
                  At("heading", left: 600, topFromPageTop: 20)));
    }

    [Fact]
    public void ALineApartByMoreThanALine_IsTwoLines()
    {
        // 20 pt apart is two lines, so the higher one leads however far right
        // it sits.
        Assert.Equal(
            new[] { "upper", "lower" },
            Order(At("lower", left: 30, topFromPageTop: 120),
                  At("upper", left: 500, topFromPageTop: 100)));
    }

    [Fact]
    public void TheResultDoesNotDependOnTheInputOrder()
    {
        var a = At("a", left: 60, topFromPageTop: 40);
        var b = At("b", left: 300, topFromPageTop: 44);
        var c = At("c", left: 60, topFromPageTop: 200);

        Assert.Equal(
            ReadingOrder.Sort(new[] { a, b, c }).Select(o => o.Identity),
            ReadingOrder.Sort(new[] { c, b, a }).Select(o => o.Identity));
    }

    [Fact]
    public void RegionsUseTheSameRule()
    {
        // Units are numbered in the panel by the order they come back in, so
        // they follow the page the same way their contents do.
        var page = new PageDimensions(1, 780, PageHeight);
        var lower = OverlapDetector.RegionCovering(page, new[] { At("lower", 30, 300) });
        var upperRight = OverlapDetector.RegionCovering(page, new[] { At("upper", 600, 20) });

        Assert.Equal(
            new[] { upperRight, lower },
            ReadingOrder.Sort(new[] { lower, upperRight }));
    }
}
