using PdfImageRemoverForRag.Core.Grouping;
using PdfImageRemoverForRag.Core.Models;
using PdfImageRemoverForRag.Infrastructure;
using Xunit;

namespace PdfImageRemoverForRag.Infrastructure.Tests;

// What the flattened picture is rendered FROM.
//
// A region is a rectangle, and rendering it from the page as it stands
// photographs everything that reaches into that rectangle — including objects
// the user did not tick, which go on being drawn underneath. On screen the two
// line up and nobody notices; a reader that pulls images out of the file gets
// the same picture twice, once as itself and once cropped inside the rendering.
// A customer reported exactly that.
public class FlattenIsolationTests : IClassFixture<SamplePdfFixture>
{
    readonly SamplePdfFixture _samples;

    public FlattenIsolationTests(SamplePdfFixture samples)
    {
        _samples = samples;
    }

    static PdfSharpDocumentAnalyzer NewAnalyzer() => new(new PdfPigThumbnailProvider());

    [Fact]
    public async Task ThePictureIsRenderedWithoutTheObjectsTheUserKept()
    {
        // The sample's one region holds an image and text over it. Tick only
        // the text: the image is being kept, so it must not be in the picture.
        var info = await NewAnalyzer().AnalyzeAsync(_samples.ImageAndTextPath);
        var detected = Assert.Single(info.OverlapRegions);
        var textOnly = OverlapDetector.RegionCovering(
            detected.Page,
            detected.Members.Where(m => m.Kind == RemovableKind.Text).ToArray());

        var rasterizer = new FlatColorRasterizer();
        await new PdfSharpDocumentCleaner(rasterizer).CleanAsync(
            _samples.ImageAndTextPath,
            Path.Combine(_samples.TempDirectory, "isolated-render.pdf"),
            Array.Empty<ObjectRemovalSelection>(),
            new[] { textOnly });

        var rendered = Assert.Single(rasterizer.RenderedContent);
        Assert.Equal(0, rendered.Images);

        // And it is not the source file being rendered: the copy holds one
        // page, so the region's page number becomes 1.
        var request = Assert.Single(rasterizer.Requests);
        Assert.Equal(1, request.PageNumber);
        Assert.NotEqual(_samples.ImageAndTextPath, Assert.Single(rasterizer.RenderedFiles));
    }

    [Fact]
    public async Task TheTickedObjectsAreStillInThePictureThatReplacesThem()
    {
        // The other half: taking the neighbors out must not take the members
        // with them, or the rendering would be blank.
        var info = await NewAnalyzer().AnalyzeAsync(_samples.ImageAndTextPath);
        var detected = Assert.Single(info.OverlapRegions);
        var flattenedText = detected.Members
            .Where(m => m.Kind == RemovableKind.Text)
            .Select(m => m.Identity)
            .ToArray();
        Assert.NotEmpty(flattenedText);

        var rasterizer = new FlatColorRasterizer();
        await new PdfSharpDocumentCleaner(rasterizer).CleanAsync(
            _samples.ImageAndTextPath,
            Path.Combine(_samples.TempDirectory, "isolated-render-keeps.pdf"),
            Array.Empty<ObjectRemovalSelection>(),
            new[] { OverlapDetector.RegionCovering(detected.Page, detected.Members.ToArray()) });

        var rendered = Assert.Single(rasterizer.RenderedContent);
        foreach (var value in flattenedText) Assert.Contains(value, rendered.Text);
        // The whole region was ticked this time, image included.
        Assert.Equal(1, rendered.Images);
    }

    [Fact]
    public async Task ThePictureIsAskedForOnATransparentBackground()
    {
        // It holds only some of what was in the rectangle, so the paper has to
        // stay unpainted — an opaque background would hide the neighbors the
        // user kept, which is the very thing being fixed.
        var info = await NewAnalyzer().AnalyzeAsync(_samples.ImageAndTextPath);
        var region = Assert.Single(info.OverlapRegions);

        var rasterizer = new FlatColorRasterizer();
        await new PdfSharpDocumentCleaner(rasterizer).CleanAsync(
            _samples.ImageAndTextPath,
            Path.Combine(_samples.TempDirectory, "isolated-render-alpha.pdf"),
            Array.Empty<ObjectRemovalSelection>(),
            new[] { region });

        Assert.True(Assert.Single(rasterizer.Transparency));
    }
}
