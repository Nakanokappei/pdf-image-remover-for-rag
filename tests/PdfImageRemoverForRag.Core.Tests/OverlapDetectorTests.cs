using PdfImageRemoverForRag.Core.Grouping;
using PdfImageRemoverForRag.Core.Models;
using Xunit;

namespace PdfImageRemoverForRag.Core.Tests;

// Overlap regions are what the flatten-to-image feature acts on: a region is
// reported only when objects of two or more different kinds share area, which
// is exactly image+text, image+shape, text+shape and image+text+shape.
public class OverlapDetectorTests
{
    static PlacedObject Image(double x, double y, double w, double h, string id = "IMGHASH") =>
        new(RemovableKind.Image, id, x, y, w, h);

    static PlacedObject Text(double x, double y, double w, double h, string value = "label") =>
        new(RemovableKind.Text, value, x, y, w, h);

    /// <summary>A filled shape: it hides what is under it, so meeting is enough.</summary>
    static PlacedObject Shape(double x, double y, double w, double h, string sig = "SHAPESIG") =>
        new(RemovableKind.Shape, sig, x, y, w, h);

    /// <summary>A stroke-only shape: a frame, a rule, an outline.</summary>
    static PlacedObject Outline(double x, double y, double w, double h, string sig = "OUTLINE") =>
        new(RemovableKind.Shape, sig, x, y, w, h, HidesWhatIsBehind: false);

    [Fact]
    public void TextInsideAnImage_IsOneRegion()
    {
        var regions = OverlapDetector.Detect(1, new[]
        {
            Image(100, 100, 200, 200),
            Text(150, 150, 40, 12),
        });

        var region = Assert.Single(regions);
        Assert.Equal(1, region.PageNumber);
        Assert.Equal(2, region.Members.Count);
        // Containment: the union is just the image.
        Assert.Equal(100, region.X, 3);
        Assert.Equal(100, region.Y, 3);
        Assert.Equal(200, region.Width, 3);
        Assert.Equal(200, region.Height, 3);
    }

    [Fact]
    public void PartiallyOverlappingTextAndImage_UnionCoversBoth()
    {
        var regions = OverlapDetector.Detect(1, new[]
        {
            Image(100, 100, 100, 100),
            Text(180, 150, 60, 12),
        });

        var region = Assert.Single(regions);
        Assert.Equal(100, region.X, 3);
        Assert.Equal(140, region.Width, 3);   // 100..240
    }

    [Fact]
    public void ObjectsThatDoNotTouch_AreNotARegion()
    {
        var regions = OverlapDetector.Detect(1, new[]
        {
            Image(0, 0, 50, 50),
            Text(300, 300, 40, 12),
        });

        Assert.Empty(regions);
    }

    [Fact]
    public void TwoTextsOverlappingEachOther_AreNotARegion()
    {
        // Text on text is still text; rasterizing it would gain nothing and
        // would lose the words. Explicitly excluded.
        var regions = OverlapDetector.Detect(1, new[]
        {
            Text(100, 100, 80, 12, "one"),
            Text(120, 100, 80, 12, "two"),
        });

        Assert.Empty(regions);
    }

    [Fact]
    public void TwoShapesOverlappingEachOther_AreNotARegion()
    {
        var regions = OverlapDetector.Detect(1, new[]
        {
            Shape(100, 100, 50, 50, "a"),
            Shape(120, 120, 50, 50, "b"),
        });

        Assert.Empty(regions);
    }

    [Fact]
    public void TextOnAShape_IsARegion()
    {
        var regions = OverlapDetector.Detect(1, new[]
        {
            Shape(100, 100, 200, 20, "box"),
            Text(110, 104, 60, 12),
        });

        Assert.Single(regions);
    }

    [Fact]
    public void OverlapIsTransitive_LabelBarAndAxisFlattenTogether()
    {
        // The label touches the bar, the bar touches the axis rule, the label
        // does not touch the rule — all three still belong to one region.
        var regions = OverlapDetector.Detect(1, new[]
        {
            Text(100, 180, 40, 12),
            Shape(100, 118, 40, 70, "bar"),     // stands on the axis, reaches the label
            Shape(90, 118, 300, 0, "axis"),
        });

        var region = Assert.Single(regions);
        Assert.Equal(3, region.Members.Count);
    }

    [Fact]
    public void AZeroHeightFilledBar_StillOverlapsTextDrawnAcrossIt()
    {
        // Paths of zero height do occur ("495x0 pt" in the object list). Without
        // a minimum extent they would intersect nothing at all.
        var regions = OverlapDetector.Detect(1, new[]
        {
            Shape(50, 700, 495, 0, "bar"),
            Text(60, 699.6, 80, 1),
        });

        Assert.Single(regions);
    }

    [Fact]
    public void AStrokeOnlyRuleCrossingText_IsNotARegion()
    {
        // A rule hides nothing, so a heading sitting on one has no reason to be
        // rasterized with it. The rule is wider than the text, so it is not
        // inside it either.
        var regions = OverlapDetector.Detect(1, new[]
        {
            Outline(50, 700, 495, 0, "rule"),
            Text(60, 699.6, 80, 1),
        });

        Assert.Empty(regions);
    }

    [Fact]
    public void APageFrameAroundText_IsNotARegion()
    {
        // The case that made this rule necessary: a border rectangle crosses
        // every paragraph on the page, and treating that as an overlap turned
        // most of a document into one region.
        var regions = OverlapDetector.Detect(1, new[]
        {
            Outline(40, 80, 515, 680, "page frame"),
            Text(60, 700, 200, 12, "a paragraph"),
            Text(60, 680, 200, 12, "another paragraph"),
        });

        Assert.Empty(regions);
    }

    [Fact]
    public void AFilledBandUnderAHeading_IsARegion()
    {
        // The other side of the same coin: a shaded table header band does hide
        // what is under it, and the headings drawn on it belong with it.
        var regions = OverlapDetector.Detect(1, new[]
        {
            Shape(50, 578, 495, 20, "header band"),
            Text(60, 582, 40, 10, "Column"),
        });

        var region = Assert.Single(regions);
        Assert.Equal(2, region.Members.Count);
    }

    [Fact]
    public void AnOutlineDrawnInsideAnImage_IsPartOfIt()
    {
        // An arrow or a callout box drawn on a photograph sits entirely within
        // it, and flattening the picture has to take the annotation along.
        var regions = OverlapDetector.Detect(1, new[]
        {
            Image(100, 100, 300, 200),
            Outline(150, 150, 60, 40, "callout"),
            Text(160, 160, 30, 10),
        });

        var region = Assert.Single(regions);
        Assert.Equal(3, region.Members.Count);
    }

    [Fact]
    public void AnOutlineOverlappingAnImagesEdge_IsNotPartOfIt()
    {
        // Half in, half out: not inside, so it stays furniture.
        var regions = OverlapDetector.Detect(1, new[]
        {
            Image(100, 100, 300, 200),
            Outline(350, 150, 200, 40, "rule running off the picture"),
        });

        Assert.Empty(regions);
    }

    [Fact]
    public void TouchingEdgesAlone_IsNotAnOverlap()
    {
        // The text starts exactly where the image ends: adjacent, not overlapping.
        var regions = OverlapDetector.Detect(1, new[]
        {
            Image(100, 100, 100, 100),
            Text(200, 100, 50, 12),
        });

        Assert.Empty(regions);
    }

    [Fact]
    public void SeparateOverlapsOnOnePage_AreSeparateRegions()
    {
        var regions = OverlapDetector.Detect(1, new[]
        {
            Image(50, 600, 100, 100, "top"),
            Text(60, 650, 40, 12, "caption one"),
            Image(50, 100, 100, 100, "bottom"),
            Text(60, 150, 40, 12, "caption two"),
        });

        Assert.Equal(2, regions.Count);
        // Reading order: the higher region on the page comes first.
        Assert.True(regions[0].Y > regions[1].Y);
    }

    [Fact]
    public void MembersAreOrderedIndependentlyOfInput()
    {
        var forwards = OverlapDetector.Detect(1, new[]
        {
            Image(100, 100, 200, 200),
            Text(150, 150, 40, 12, "b"),
            Shape(150, 130, 40, 4, "a"),
        });
        var backwards = OverlapDetector.Detect(1, new[]
        {
            Shape(150, 130, 40, 4, "a"),
            Text(150, 150, 40, 12, "b"),
            Image(100, 100, 200, 200),
        });

        Assert.Equal(
            forwards[0].Members.Select(m => (m.Kind, m.Identity)),
            backwards[0].Members.Select(m => (m.Kind, m.Identity)));
    }

    [Fact]
    public void AnObjectAloneOnThePage_IsNotARegion()
    {
        Assert.Empty(OverlapDetector.Detect(1, new[] { Image(0, 0, 10, 10) }));
        Assert.Empty(OverlapDetector.Detect(1, Array.Empty<PlacedObject>()));
    }
}
