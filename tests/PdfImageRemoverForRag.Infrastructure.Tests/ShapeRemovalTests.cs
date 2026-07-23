using PdfImageRemoverForRag.Core.Models;
using PdfImageRemoverForRag.Infrastructure;
using Xunit;
using PdfPigDoc = UglyToad.PdfPig.PdfDocument;

namespace PdfImageRemoverForRag.Infrastructure.Tests;

// End-to-end vector-shape removal against the repeated-shapes sample: the
// shared header rule and border rectangle are detected (drawn on every page),
// one selection removes them from every page, and the per-page unique
// diagonal line survives.
public class ShapeRemovalTests : IClassFixture<SamplePdfFixture>
{
    readonly SamplePdfFixture _samples;

    public ShapeRemovalTests(SamplePdfFixture samples)
    {
        _samples = samples;
    }

    static PdfSharpDocumentAnalyzer NewAnalyzer() => new(new PdfPigThumbnailProvider());

    [Fact]
    public async Task AllShapes_AreDetected_IncludingSingleOccurrence()
    {
        var info = await NewAnalyzer().AnalyzeAsync(_samples.RepeatedShapesPath);
        var shapeGroups = info.ImageGroups.Where(g => g.Kind == RemovableKind.Shape).ToArray();

        // Groups (position-independent, by shape + width + color):
        //   header rule (3x), border (3x), blue square at 3 positions (3x),
        //   plus 3 per-page unique diagonals (1x each). = 6 groups.
        Assert.Equal(6, shapeGroups.Length);
        Assert.Equal(3, shapeGroups.Count(g => g.UsageCount == 3));
        Assert.Equal(3, shapeGroups.Count(g => g.UsageCount == 1));
    }

    [Fact]
    public async Task SameShapeAtDifferentPositions_IsOneGroup()
    {
        // The 30x30 blue square is drawn at a different x on each page but is
        // the same shape/width/color — position must not split it into three.
        var info = await NewAnalyzer().AnalyzeAsync(_samples.RepeatedShapesPath);
        var square = info.ImageGroups.Single(g =>
            g.Kind == RemovableKind.Shape && g.PixelWidth == 30 && g.PixelHeight == 30);
        Assert.Equal(3, square.UsageCount);
        Assert.Equal(new[] { 1, 2, 3 }, square.UsagePages);

        // Geometry is carried for the App to render a thumbnail: a rectangle,
        // stroked in the pen's blue (RGB → 0,0,255) for a colored thumbnail.
        Assert.NotNull(square.ShapeGeometry);
        Assert.Contains(square.ShapeGeometry!.Elements, e => e.Operator == "re");
        Assert.Equal(new RgbColor(0, 0, 255), square.ShapeGeometry!.StrokeColor);
    }

    [Fact]
    public async Task RemovingARepeatedShape_DropsItFromEveryPage()
    {
        var analyzer = NewAnalyzer();
        var info = await analyzer.AnalyzeAsync(_samples.RepeatedShapesPath);
        var shape = info.ImageGroups.First(g => g.Kind == RemovableKind.Shape);

        var dest = Path.Combine(_samples.TempDirectory, "repeated-shapes_cleaned.pdf");
        var cleaner = new PdfSharpDocumentCleaner();
        var result = await cleaner.CleanAsync(_samples.RepeatedShapesPath, dest, new[]
        {
            new ImageRemovalSelection(shape.GroupId, shape.Occurrences,
                RemovableKind.Shape, shape.TextValue),
        });

        Assert.Equal(3, result.PagesModified);
        Assert.Equal(3, result.DrawCallsRemoved);

        // Re-analyze: the removed shape is gone; the other repeated shape and
        // the unique diagonals remain untouched.
        var reanalyzed = await analyzer.AnalyzeAsync(dest);
        Assert.DoesNotContain(reanalyzed.ImageGroups,
            g => g.Kind == RemovableKind.Shape && g.TextValue == shape.TextValue);
        Assert.Contains(reanalyzed.ImageGroups, g => g.Kind == RemovableKind.Shape);
    }

    [Fact]
    public async Task RemovingAShape_KeepsPageCountAndText()
    {
        var analyzer = NewAnalyzer();
        var info = await analyzer.AnalyzeAsync(_samples.RepeatedShapesPath);
        var shape = info.ImageGroups.First(g => g.Kind == RemovableKind.Shape);
        var dest = Path.Combine(_samples.TempDirectory, "repeated-shapes_kept_cleaned.pdf");

        await new PdfSharpDocumentCleaner().CleanAsync(_samples.RepeatedShapesPath, dest, new[]
        {
            new ImageRemovalSelection(shape.GroupId, shape.Occurrences,
                RemovableKind.Shape, shape.TextValue),
        });

        var reanalyzed = await analyzer.AnalyzeAsync(dest);
        Assert.Equal(info.PageCount, reanalyzed.PageCount);

        // Body text is unaffected by shape removal (independent parser check).
        using var pig = PdfPigDoc.Open(dest);
        var allText = string.Concat(pig.GetPages().Select(p => p.Text));
        Assert.Contains("body text", allText);
    }
}
