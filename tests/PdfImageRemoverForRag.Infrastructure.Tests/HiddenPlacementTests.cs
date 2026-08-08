using PdfImageRemoverForRag.Core.Grouping;
using PdfImageRemoverForRag.Core.Models;
using PdfImageRemoverForRag.Infrastructure;
using PdfImageRemoverForRag.Infrastructure.Internal;
using PdfSharp.Pdf.IO;
using Xunit;

namespace PdfImageRemoverForRag.Infrastructure.Tests;

// Hiding a layer takes out ONE drawing of an object, where a removal selection
// takes out the object everywhere it appears. The difference is the whole point:
// a caption hidden on page 4 must not disappear from the other thirty pages, and
// the panel's eye promises exactly that.
public class HiddenPlacementTests : IClassFixture<SamplePdfFixture>
{
    readonly SamplePdfFixture _samples;

    public HiddenPlacementTests(SamplePdfFixture samples)
    {
        _samples = samples;
    }

    static PdfSharpDocumentAnalyzer NewAnalyzer() => new(new PdfPigThumbnailProvider());

    /// <summary>The pages whose resources still name this image.</summary>
    static List<int> PagesDrawing(string path, string hash)
    {
        using var document = PdfReader.Open(path, PdfDocumentOpenMode.Import);
        var pages = new List<int>();
        for (int i = 0; i < document.PageCount; i++)
        {
            foreach (var entry in ImageXObjectCollector.EnumerateImageEntries(document.Pages[i].Resources))
            {
                if (ImageXObjectCollector.ComputeStreamHash(entry.Dictionary) != hash) continue;
                pages.Add(i + 1);
                break;
            }
        }
        return pages;
    }

    [Fact]
    public async Task HidingOneDrawingLeavesTheOthersAlone()
    {
        // The sample draws one logo on all five pages. Hide the one on page 2.
        var info = await NewAnalyzer().AnalyzeAsync(_samples.RepeatedLogoPath);
        var logo = info.ObjectGroups.Single(g => g.Kind == RemovableKind.Image);
        var onPageTwo = logo.Occurrences.Single(o => o.PageNumber == 2);

        var place = OverlapDetector.RegionCovering(
            new PageDimensions(2, 0, 0),
            new[]
            {
                new PlacedObject(
                    RemovableKind.Image, logo.Hash,
                    onPageTwo.X, onPageTwo.Y, onPageTwo.Width, onPageTwo.Height),
            });

        var destination = Path.Combine(_samples.TempDirectory, "hidden-placement.pdf");
        var result = await new PdfSharpDocumentCleaner().CleanAsync(
            _samples.RepeatedLogoPath, destination,
            Array.Empty<ObjectRemovalSelection>(), regionsToFlatten: null,
            regionsToClear: new[] { place });

        Assert.Equal(new[] { 1, 3, 4, 5 }, PagesDrawing(destination, logo.Hash));
        // Taken out, not baked in: nothing was drawn in its place.
        Assert.Equal(0, result.RegionsFlattened);
        Assert.True(result.DrawCallsRemoved > 0);
    }

    [Fact]
    public async Task HidingNeedsNoRendererBecauseNothingIsDrawn()
    {
        // A cleaner built without a rasterizer refuses to flatten — and must
        // still empty a place, which is the operation that draws nothing.
        var info = await NewAnalyzer().AnalyzeAsync(_samples.ImageAndTextPath);
        var region = Assert.Single(info.OverlapRegions);

        var destination = Path.Combine(_samples.TempDirectory, "hidden-no-renderer.pdf");
        var result = await new PdfSharpDocumentCleaner().CleanAsync(
            _samples.ImageAndTextPath, destination,
            Array.Empty<ObjectRemovalSelection>(), regionsToFlatten: null,
            regionsToClear: new[] { region });

        Assert.True(result.DrawCallsRemoved >= region.Members.Count);

        // And what it took out is gone from the file, not merely unpainted.
        var after = await NewAnalyzer().AnalyzeAsync(destination);
        Assert.DoesNotContain(after.ObjectGroups, g => g.Kind == RemovableKind.Image);
    }
}
