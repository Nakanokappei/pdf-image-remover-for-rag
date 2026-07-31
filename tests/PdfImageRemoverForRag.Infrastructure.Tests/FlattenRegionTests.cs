using PdfImageRemoverForRag.Core.Errors;
using PdfImageRemoverForRag.Core.Grouping;
using PdfImageRemoverForRag.Core.Models;
using PdfImageRemoverForRag.Infrastructure;
using Xunit;
using PdfPigDoc = UglyToad.PdfPig.PdfDocument;

namespace PdfImageRemoverForRag.Infrastructure.Tests;

// End-to-end flattening: an overlap region is replaced by a picture of itself,
// so the text stops being text while the page still looks the same.
//
// The renderer is <see cref="FlatColourRasterizer"/>, which explains there why
// standing in for the real one is the arrangement rather than a shortcut.
public class FlattenRegionTests : IClassFixture<SamplePdfFixture>
{
    readonly SamplePdfFixture _samples;

    public FlattenRegionTests(SamplePdfFixture samples)
    {
        _samples = samples;
    }

    static PdfSharpDocumentAnalyzer NewAnalyzer() => new(new PdfPigThumbnailProvider());

    string Destination(string name) => Path.Combine(_samples.TempDirectory, name);

    static IReadOnlyList<ImageRemovalSelection> NothingToRemove() =>
        Array.Empty<ImageRemovalSelection>();

    [Fact]
    public async Task FlatteningARegion_DropsItsTextAndDrawsAPictureInstead()
    {
        var info = await NewAnalyzer().AnalyzeAsync(_samples.ImageAndTextPath);
        var region = Assert.Single(info.OverlapRegions);
        var flattenedStrings = region.Members
            .Where(m => m.Kind == RemovableKind.Text)
            .Select(m => m.Identity)
            .ToArray();
        Assert.NotEmpty(flattenedStrings);

        var dest = Destination("image-and-text_flattened.pdf");
        var result = await new PdfSharpDocumentCleaner(new FlatColourRasterizer())
            .CleanAsync(_samples.ImageAndTextPath, dest, NothingToRemove(), new[] { region });

        Assert.Equal(1, result.RegionsFlattened);
        Assert.Equal(1, result.PagesModified);

        // Nothing was DELETED. Flattening lifts draw calls out of the content
        // stream too, but it puts a picture of them straight back, so counting
        // them as removals told a user who had only flattened that objects had
        // been thrown away. The two totals are reported apart all the way to
        // the status bar; this is where they part company.
        Assert.Equal(0, result.DrawCallsRemoved);

        // Read back with the other parser: the flattened strings are not text
        // any more, and something is drawn where they were.
        using var pig = PdfPigDoc.Open(dest);
        var page = Assert.Single(pig.GetPages());
        foreach (var value in flattenedStrings) Assert.DoesNotContain(value, page.Text);
        Assert.NotEmpty(page.GetImages());
    }

    [Fact]
    public async Task TheFlattenedRegionEndsUpAsTheOnlyImageDrawnOnThePage()
    {
        // Everything in the region goes, the original image included, and the
        // rendering takes their place — so re-analysis finds one image and no
        // text where the overlap was.
        var info = await NewAnalyzer().AnalyzeAsync(_samples.ImageAndTextPath);
        var region = Assert.Single(info.OverlapRegions);

        var dest = Destination("image-and-text_flattened_only.pdf");
        await new PdfSharpDocumentCleaner(new FlatColourRasterizer())
            .CleanAsync(_samples.ImageAndTextPath, dest, NothingToRemove(), new[] { region });

        var reanalyzed = await NewAnalyzer().AnalyzeAsync(dest);
        var image = Assert.Single(reanalyzed.ImageGroups, g => g.Kind == RemovableKind.Image);
        Assert.Equal(1, image.UsageCount);

        // And it covers exactly what it replaced. Worth asserting on its own:
        // the drawing side counts from the top of the page and regions are
        // measured from the bottom, and getting that flip wrong is how the
        // usage-locations outline ended up a quarter of a page out of place.
        var placement = Assert.Single(image.Occurrences);
        Assert.Equal(region.X, placement.X, 1);
        Assert.Equal(region.Y, placement.Y, 1);
        Assert.Equal(region.Width, placement.Width, 1);
        Assert.Equal(region.Height, placement.Height, 1);
    }

    [Fact]
    public async Task OnlyTheMembersHandedOver_AreFlattened()
    {
        // The user checks objects individually, so a region may arrive holding
        // some of what was detected. Here the text is checked and the image is
        // not: the text goes, the image keeps being drawn, and the rendering is
        // added over it.
        var info = await NewAnalyzer().AnalyzeAsync(_samples.ImageAndTextPath);
        var detected = Assert.Single(info.OverlapRegions);
        var textOnly = OverlapDetector.RegionCovering(
            detected.Page,
            detected.Members.Where(m => m.Kind == RemovableKind.Text).ToArray());
        var originalImageHash = detected.Members.First(m => m.Kind == RemovableKind.Image).Identity;

        var dest = Destination("image-and-text_flattened_text_only.pdf");
        var result = await new PdfSharpDocumentCleaner(new FlatColourRasterizer())
            .CleanAsync(_samples.ImageAndTextPath, dest, NothingToRemove(), new[] { textOnly });

        Assert.Equal(1, result.RegionsFlattened);

        var reanalyzed = await NewAnalyzer().AnalyzeAsync(dest);
        Assert.Contains(reanalyzed.ImageGroups,
            g => g.Kind == RemovableKind.Image && g.Hash == originalImageHash);
        Assert.Equal(2, reanalyzed.ImageGroups.Count(g => g.Kind == RemovableKind.Image));
    }

    [Fact]
    public async Task TheRegionIsRenderedFromTheSourceAtTheDetectedRectangle()
    {
        var info = await NewAnalyzer().AnalyzeAsync(_samples.ImageAndTextPath);
        var region = Assert.Single(info.OverlapRegions);
        var rasterizer = new FlatColourRasterizer();

        var dest = Destination("image-and-text_render_request.pdf");
        await new PdfSharpDocumentCleaner(rasterizer)
            .CleanAsync(_samples.ImageAndTextPath, dest, NothingToRemove(), new[] { region });

        var request = Assert.Single(rasterizer.Requests);
        Assert.Equal(region.PageNumber, request.PageNumber);
        Assert.Equal(region.X, request.Region.X, 3);
        Assert.Equal(region.Y, request.Region.Y, 3);
        Assert.Equal(region.Width, request.Region.Width, 3);
        Assert.Equal(region.Height, request.Region.Height, 3);
        Assert.True(request.Dpi >= 150, "flattened text has to stay legible");
    }

    [Fact]
    public async Task ARegionThatWillNotRender_LeavesThePageAsItWas()
    {
        // Deleting the objects and then finding there is nothing to draw would
        // punch a white hole in the page. Skipping the region is the only safe
        // answer, and it must be reported as not flattened.
        var info = await NewAnalyzer().AnalyzeAsync(_samples.ImageAndTextPath);
        var region = Assert.Single(info.OverlapRegions);
        var strings = region.Members
            .Where(m => m.Kind == RemovableKind.Text)
            .Select(m => m.Identity)
            .ToArray();

        var dest = Destination("image-and-text_render_failed.pdf");
        var result = await new PdfSharpDocumentCleaner(new FlatColourRasterizer(succeeds: false))
            .CleanAsync(_samples.ImageAndTextPath, dest, NothingToRemove(), new[] { region });

        Assert.Equal(0, result.RegionsFlattened);
        Assert.Equal(0, result.PagesModified);

        using var pig = PdfPigDoc.Open(dest);
        var page = Assert.Single(pig.GetPages());
        foreach (var value in strings) Assert.Contains(value, page.Text);
    }

    [Fact]
    public async Task FlattenedImagesAreNotReportedAsRemovedFromTheFile()
    {
        // The bytes are still in the document — inside the rendering, and often
        // still drawn on other pages — so the verifier must not be told to
        // expect them gone.
        var info = await NewAnalyzer().AnalyzeAsync(_samples.ImageAndTextPath);
        var region = Assert.Single(info.OverlapRegions);

        var dest = Destination("image-and-text_flattened_hashes.pdf");
        var result = await new PdfSharpDocumentCleaner(new FlatColourRasterizer())
            .CleanAsync(_samples.ImageAndTextPath, dest, NothingToRemove(), new[] { region });

        Assert.Empty(result.RemovedGroupHashes);
    }

    [Fact]
    public async Task ARegionThatIsTheWholePage_SaysSo()
    {
        // The one case the whole-page warning exists for, end to end: a scan
        // with a caption typed over it. Flattening that leaves the page without
        // any text at all, so the UI has to be able to say so beforehand.
        var info = await NewAnalyzer().AnalyzeAsync(_samples.FullPageOverlapPath);
        var region = Assert.Single(info.OverlapRegions);

        Assert.True(OverlapDetector.CoversWholePage(region));
    }

    [Fact]
    public async Task AnOrdinaryRegion_DoesNotClaimToBeTheWholePage()
    {
        var info = await NewAnalyzer().AnalyzeAsync(_samples.ImageAndTextPath);
        var region = Assert.Single(info.OverlapRegions);

        Assert.False(OverlapDetector.CoversWholePage(region));
    }

    [Fact]
    public async Task AskingToFlattenWithoutARenderer_IsAnError()
    {
        var info = await NewAnalyzer().AnalyzeAsync(_samples.ImageAndTextPath);
        var region = Assert.Single(info.OverlapRegions);

        var ex = await Assert.ThrowsAsync<PdfCleanerException>(() =>
            new PdfSharpDocumentCleaner().CleanAsync(
                _samples.ImageAndTextPath, Destination("never-written.pdf"),
                NothingToRemove(), new[] { region }));
        Assert.Equal(PdfCleanerErrorKind.Unexpected, ex.Kind);
    }

    [Fact]
    public async Task ASaveThatNeitherFlattensNorRemoves_IsStillRefused()
    {
        await Assert.ThrowsAsync<PdfCleanerException>(() =>
            new PdfSharpDocumentCleaner(new FlatColourRasterizer()).CleanAsync(
                _samples.ImageAndTextPath, Destination("never-written-2.pdf"),
                NothingToRemove(), Array.Empty<OverlapRegion>()));
    }
}
