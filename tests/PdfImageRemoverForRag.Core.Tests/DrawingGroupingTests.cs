using PdfImageRemoverForRag.Core.Grouping;
using PdfImageRemoverForRag.Core.Models;
using Xunit;

namespace PdfImageRemoverForRag.Core.Tests;

// Grouping behavior specific to drawings (RemovableKind.Drawing): the artwork a
// Form XObject paints. One form drawn on several pages is one object with
// several placements, exactly as one image stream drawn on several pages is —
// and for the same reason, since both are identified by the bytes of a stream
// the file stores once.
public class DrawingGroupingTests
{
    // The discovery factories live in Discoveries; only the geometry below is
    // specific to drawings.
    static ObjectDiscovery Drawing(string formHash, int page, DrawingGeometry? geometry = null) =>
        Discoveries.Drawing(formHash, page, geometry);
    static ObjectGroupBuilder NewBuilder() => Discoveries.NewBuilder();

    // A head, a body and a speech bubble: three paths, two paint operators, all
    // in the drawing's own 120x120 box rather than each in its own.
    static DrawingGeometry Icon() => new(
        new[]
        {
            new ShapeGeometry(
                new[] { new ShapePathElement("c", new[] { new PointD(30, 10), new PointD(70, 50) }) },
                40, 40, "f", 0, null, new RgbColor(83, 86, 90)),
            new ShapeGeometry(
                new[] { new ShapePathElement("re", new[] { new PointD(20, 58), new PointD(80, 98) }) },
                60, 40, "f", 0, null, new RgbColor(83, 86, 90)),
            new ShapeGeometry(
                new[] { new ShapePathElement("re", new[] { new PointD(75, 15), new PointD(115, 45) }) },
                40, 30, "S", 1, new RgbColor(83, 86, 90)),
        },
        120, 120);

    [Fact]
    public void TheSameFormOnTwoPages_IsOneDrawingWithTwoPlacements()
    {
        var groups = NewBuilder().Build(new[]
        {
            Drawing("HASH_ICON", page: 1),
            Drawing("HASH_ICON", page: 2),
        });

        var drawing = Assert.Single(groups);
        Assert.Equal(RemovableKind.Drawing, drawing.Kind);
        Assert.Equal("DRW_001", drawing.GroupId);
        Assert.Equal(2, drawing.UsageCount);
        Assert.Equal(new[] { 1, 2 }, drawing.UsagePages);
    }

    [Fact]
    public void DrawingsSortAfterEveryOtherKind_AndGetTheirOwnIds()
    {
        // The enum's order is the display order, and a drawing is the newest
        // kind, so it comes last wherever the list is shown.
        var groups = NewBuilder().Build(new[]
        {
            Drawing("HASH_ICON", page: 1),
            Discoveries.Shape("border"),
            Discoveries.Text("CONFIDENTIAL"),
            Discoveries.Image("HASH_LOGO"),
        });

        Assert.Equal(4, groups.Count);
        Assert.Equal(
            new[] { RemovableKind.Image, RemovableKind.Text, RemovableKind.Shape, RemovableKind.Drawing },
            groups.Select(g => g.Kind).ToArray());
        Assert.Equal(
            new[] { "IMG_001", "TXT_001", "SHP_001", "DRW_001" },
            groups.Select(g => g.GroupId).ToArray());
    }

    [Fact]
    public void ADrawing_CarriesEveryPathItsFormPaints()
    {
        var groups = NewBuilder().Build(new[] { Drawing("HASH_ICON", page: 1, geometry: Icon()) });

        var geometry = Assert.Single(groups).DrawingGeometry;
        Assert.NotNull(geometry);
        Assert.Equal(3, geometry!.Parts.Count);
        // Both paint operators survive: two filled parts and one stroked, which
        // is what a lone ShapeGeometry could not have expressed.
        Assert.Equal(2, geometry.Parts.Count(p => p.IsFilled));
        Assert.Contains(geometry.Parts, p => p.PaintOperator == "S");
        // And the box is the drawing's, not any one path's.
        Assert.Equal(120, geometry.Width);
        Assert.Equal(120, geometry.Height);
    }

    [Fact]
    public void ADrawing_IsMatchedByItsFormStreamHash()
    {
        // The cleaner resolves a selection back to a resource name through this
        // key. A drawing has no shown string and no path signature, so getting
        // this wrong would hand the cleaner an empty key and silently remove
        // nothing.
        var groups = CrossFileObjectGroupBuilder.Build(new[]
        {
            ("a.pdf", (IReadOnlyList<ObjectGroup>)NewBuilder()
                .Build(new[] { Drawing("HASH_ICON", page: 1) }).ToArray()),
        });

        var drawing = Assert.Single(groups);
        Assert.Equal("DRW_001", drawing.GroupId);
        Assert.Equal("HASH_ICON", drawing.MatchKey);
    }
}
