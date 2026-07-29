using PdfImageRemoverForRag.Core.Models;
using PdfImageRemoverForRag.Infrastructure;
using Xunit;

namespace PdfImageRemoverForRag.Infrastructure.Tests;

// The usage-locations window outlines an object wherever it is drawn, which
// only works if occurrences carry rectangles. Images always did; these cover
// text and shapes, whose occurrences used to hold a page number and four
// zeroes.
public class UsageLocationTests : IClassFixture<SamplePdfFixture>
{
    readonly SamplePdfFixture _samples;

    public UsageLocationTests(SamplePdfFixture samples)
    {
        _samples = samples;
    }

    static PdfSharpDocumentAnalyzer NewAnalyzer() => new(new PdfPigThumbnailProvider());

    [Fact]
    public async Task EveryTextOccurrence_CarriesARectangleOnItsPage()
    {
        var info = await NewAnalyzer().AnalyzeAsync(_samples.RepeatedTextPath);
        var header = info.ImageGroups.Single(
            g => g.Kind == RemovableKind.Text && g.TextValue == "CONFIDENTIAL");

        Assert.All(header.Occurrences, occurrence =>
        {
            Assert.True(occurrence.Width > 0, "text occurrence has no width");
            Assert.True(occurrence.Height > 0, "text occurrence has no height");
            // On the page rather than off it (the samples are A4).
            Assert.InRange(occurrence.X, 0, 595);
            Assert.InRange(occurrence.Y, 0, 842);
        });
    }

    [Fact]
    public async Task TheSameStringOnEveryPage_IsOutlinedInTheSamePlace()
    {
        // A running header sits at one spot on every page; the rectangles
        // should agree, which is what shows the outline is derived from the
        // text matrix and not from the order things were found in.
        var info = await NewAnalyzer().AnalyzeAsync(_samples.RepeatedTextPath);
        var header = info.ImageGroups.Single(
            g => g.Kind == RemovableKind.Text && g.TextValue == "CONFIDENTIAL");

        var first = header.Occurrences[0];
        Assert.All(header.Occurrences, occurrence =>
        {
            Assert.Equal(first.X, occurrence.X, 1);
            Assert.Equal(first.Y, occurrence.Y, 1);
            Assert.Equal(first.Width, occurrence.Width, 1);
        });
        Assert.Equal(3, header.Occurrences.Count);
    }

    [Fact]
    public async Task EveryShapeOccurrence_CarriesItsOwnRectangle()
    {
        var info = await NewAnalyzer().AnalyzeAsync(_samples.RepeatedShapesPath);
        var shapes = info.ImageGroups.Where(g => g.Kind == RemovableKind.Shape).ToArray();

        Assert.NotEmpty(shapes);
        foreach (var shape in shapes)
        {
            Assert.All(shape.Occurrences, occurrence =>
                Assert.True(occurrence.Width > 0 || occurrence.Height > 0,
                    "shape occurrence has no extent at all"));
        }
    }

    [Fact]
    public async Task AShapeDrawnAtSeveralPositions_ReportsEachPosition()
    {
        // Shapes group position-independently: the same square drawn in three
        // places is one row. Its occurrences must still say where each one is,
        // or the window would outline one square three times.
        var info = await NewAnalyzer().AnalyzeAsync(_samples.RepeatedShapesPath);
        var repeated = info.ImageGroups
            .Where(g => g.Kind == RemovableKind.Shape && g.UsageCount > 1)
            .ToArray();

        Assert.NotEmpty(repeated);
        Assert.Contains(repeated, shape =>
            shape.Occurrences.Select(o => (Math.Round(o.X, 1), Math.Round(o.Y, 1)))
                .Distinct().Count() > 1);
    }

    [Fact]
    public async Task AFullPageShape_IsNotWarnedAboutAsAScannedPage()
    {
        // The full-page warning says the page will go blank because the object
        // is probably a scan. Now that shapes carry rectangles, a page-sized
        // rule or background must not inherit it.
        var info = await NewAnalyzer().AnalyzeAsync(_samples.RepeatedShapesPath);

        Assert.All(info.ImageGroups.Where(g => g.Kind != RemovableKind.Image),
            group => Assert.False(group.IsPossibleFullPageImage));
    }

    [Fact]
    public async Task ImageOccurrences_StillCarryTheirRectangles()
    {
        // Guard against the text/shape work disturbing the path that already
        // worked.
        var info = await NewAnalyzer().AnalyzeAsync(_samples.RepeatedLogoPath);
        var image = info.ImageGroups.First(g => g.Kind == RemovableKind.Image);

        Assert.All(image.Occurrences, occurrence =>
        {
            Assert.True(occurrence.Width > 0);
            Assert.True(occurrence.Height > 0);
        });
    }
}
