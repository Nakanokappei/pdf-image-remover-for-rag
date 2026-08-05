using PdfImageRemoverForRag.Infrastructure;
using Xunit;

namespace PdfImageRemoverForRag.Infrastructure.Tests;

// Extraction re-reads the whole document with a second parser, and on a long
// file that is nearly all of the analysis time — 9.8 seconds of 10.9 on a
// 136-page document whose images could not be decoded at all. So the caller
// says which streams it wants, and this is what that buys.
public class ThumbnailExtractionScopeTests : IClassFixture<SamplePdfFixture>
{
    readonly SamplePdfFixture _samples;

    public ThumbnailExtractionScopeTests(SamplePdfFixture samples)
    {
        _samples = samples;
    }

    [Fact]
    public async Task AskingForNothing_ReadsNothing()
    {
        var thumbnails = await new PdfPigThumbnailProvider().ExtractThumbnailsAsync(
            _samples.MultipleImagesPath, 160, 120, Array.Empty<string>());

        Assert.Empty(thumbnails);
    }

    [Fact]
    public async Task AskingForOneStream_ReturnsThatOneAlone()
    {
        var everything = await new PdfPigThumbnailProvider()
            .ExtractThumbnailsAsync(_samples.MultipleImagesPath, 160, 120);
        Assert.True(everything.Count > 1);
        var wanted = everything.Keys.First();

        var one = await new PdfPigThumbnailProvider().ExtractThumbnailsAsync(
            _samples.MultipleImagesPath, 160, 120, new[] { wanted });

        Assert.Equal(new[] { wanted }, one.Keys);
    }

    [Fact]
    public async Task AskingForEverythingIsStillTheDefault()
    {
        // Null means "no opinion", which has to keep the old behaviour: a
        // caller that cannot say what it wants must not silently get less.
        var thumbnails = await new PdfPigThumbnailProvider()
            .ExtractThumbnailsAsync(_samples.MultipleImagesPath, 160, 120, wantedHashes: null);

        Assert.True(thumbnails.Count > 1);
    }
}
