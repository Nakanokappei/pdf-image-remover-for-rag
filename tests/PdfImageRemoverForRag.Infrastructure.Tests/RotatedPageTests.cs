using PdfImageRemoverForRag.Core.Models;
using PdfImageRemoverForRag.Infrastructure;
using PdfSharp.Pdf.IO;
using Xunit;

namespace PdfImageRemoverForRag.Infrastructure.Tests;

// A page carrying /Rotate is displayed turned, and nothing in this layer turns
// with it: objects are found in content-stream coordinates and rewritten in
// content-stream coordinates. That agreement is what makes rotation a non-event
// here — but it rests on PDFsharp reporting the page's size and placing appended
// content without compensating for the entry, which is a fact about a library,
// not a guarantee. These tests hold it in place.
//
// It is emphatically NOT a non-event one layer up: the operating system's
// renderer DOES turn the page, so the flatten path has to map its rectangle into
// that space and turn the result back. That mapping is PageRotation, tested in
// Core; what is checked here is that this layer keeps handing over content space
// and lets the rasterizer do the turning.
//
// The fixture is the image-and-text sample with /Rotate 90 added after the
// content was written, so its content stream is identical and any difference in
// analysis is the rotation's doing.
public class RotatedPageTests : IClassFixture<SamplePdfFixture>
{
    readonly SamplePdfFixture _samples;

    public RotatedPageTests(SamplePdfFixture samples)
    {
        _samples = samples;
    }

    static PdfSharpDocumentAnalyzer NewAnalyzer() => new(new PdfPigThumbnailProvider());

    string Destination(string name) => Path.Combine(_samples.TempDirectory, name);

    [Fact]
    public void TheSamplePageReallyCarriesTheRotation()
    {
        // Without this the three tests below would keep passing if the sample
        // quietly stopped being rotated, and would then prove nothing.
        using var doc = PdfReader.Open(_samples.RotatedPagePath, PdfDocumentOpenMode.Import);
        Assert.Equal(90, doc.Pages[0].Rotate);
    }

    [Fact]
    public async Task AnalysisReadsTheSameRectangle_RotatedOrNot()
    {
        var upright = await NewAnalyzer().AnalyzeAsync(_samples.ImageAndTextPath);
        var rotated = await NewAnalyzer().AnalyzeAsync(_samples.RotatedPagePath);

        var expected = Assert.Single(upright.OverlapRegions);
        var actual = Assert.Single(rotated.OverlapRegions);
        Assert.Equal(expected.X, actual.X, 3);
        Assert.Equal(expected.Y, actual.Y, 3);
        Assert.Equal(expected.Width, actual.Width, 3);
        Assert.Equal(expected.Height, actual.Height, 3);
    }

    [Fact]
    public async Task TheRendererIsAskedInContentSpace_NotTheViewersSpace()
    {
        // Turning the rectangle is the rasterizer's job, because only it knows
        // what space its renderer works in. If this layer pre-turned it the two
        // would turn it twice.
        var info = await NewAnalyzer().AnalyzeAsync(_samples.RotatedPagePath);
        var region = Assert.Single(info.OverlapRegions);
        var rasterizer = new FlatColourRasterizer();

        await new PdfSharpDocumentCleaner(rasterizer).CleanAsync(
            _samples.RotatedPagePath, Destination("rotated_render_request.pdf"),
            Array.Empty<ObjectRemovalSelection>(), new[] { region });

        var request = Assert.Single(rasterizer.Requests);
        Assert.Equal(region.X, request.Region.X, 3);
        Assert.Equal(region.Y, request.Region.Y, 3);
        Assert.Equal(region.Width, request.Region.Width, 3);
        Assert.Equal(region.Height, request.Region.Height, 3);
    }

    [Fact]
    public async Task FlatteningARotatedPage_DrawsTheReplacementWhereTheObjectsWere()
    {
        // The drawing side measures down from the page's top, so it depends on
        // PDFsharp reporting a height that /Rotate has not swapped. If that ever
        // changed, every flattened region on a rotated page would move — quietly,
        // because nothing else would fail.
        var info = await NewAnalyzer().AnalyzeAsync(_samples.RotatedPagePath);
        var region = Assert.Single(info.OverlapRegions);

        var dest = Destination("rotated_flattened.pdf");
        var result = await new PdfSharpDocumentCleaner(new FlatColourRasterizer()).CleanAsync(
            _samples.RotatedPagePath, dest,
            Array.Empty<ObjectRemovalSelection>(), new[] { region });
        Assert.Equal(1, result.RegionsFlattened);

        var reanalyzed = await NewAnalyzer().AnalyzeAsync(dest);
        var image = Assert.Single(reanalyzed.ObjectGroups, g => g.Kind == RemovableKind.Image);
        var placement = Assert.Single(image.Occurrences);
        Assert.Equal(region.X, placement.X, 1);
        Assert.Equal(region.Y, placement.Y, 1);
        Assert.Equal(region.Width, placement.Width, 1);
        Assert.Equal(region.Height, placement.Height, 1);
    }

    [Fact]
    public async Task ACleanedPage_KeepsItsRotation()
    {
        // Losing the entry would leave every page of the saved document on its
        // side — a whole-document regression from a one-region edit.
        var info = await NewAnalyzer().AnalyzeAsync(_samples.RotatedPagePath);
        var region = Assert.Single(info.OverlapRegions);

        var dest = Destination("rotated_keeps_rotation.pdf");
        await new PdfSharpDocumentCleaner(new FlatColourRasterizer()).CleanAsync(
            _samples.RotatedPagePath, dest,
            Array.Empty<ObjectRemovalSelection>(), new[] { region });

        using var doc = PdfReader.Open(dest, PdfDocumentOpenMode.Import);
        Assert.Equal(90, doc.Pages[0].Rotate);
    }

    [Fact]
    public async Task RemovingAnImageFromARotatedPage_KeepsItsRotation()
    {
        // The removal path never touches the page dictionary either, and it is
        // the path most documents take.
        var info = await NewAnalyzer().AnalyzeAsync(_samples.RotatedPagePath);
        var image = info.ObjectGroups.Single(g => g.Kind == RemovableKind.Image);

        var dest = Destination("rotated_removed.pdf");
        await new PdfSharpDocumentCleaner().CleanAsync(
            _samples.RotatedPagePath, dest,
            new[] { new ObjectRemovalSelection(image.GroupId, image.Occurrences, Hash: image.Hash) });

        using var doc = PdfReader.Open(dest, PdfDocumentOpenMode.Import);
        Assert.Equal(90, doc.Pages[0].Rotate);
    }
}
