using PdfImageRemoverForRag.Core.Models;
using PdfImageRemoverForRag.Infrastructure;
using Xunit;

namespace PdfImageRemoverForRag.Infrastructure.Tests;

// The analyzer reports where objects of different kinds overlap, per page, for
// the flatten side to offer. These check the wiring against the sample PDFs —
// that the right pages are reported, that the members are the real ones, and
// that the exclusions hold.
public class OverlapAnalysisTests : IClassFixture<SamplePdfFixture>
{
    readonly SamplePdfFixture _samples;

    public OverlapAnalysisTests(SamplePdfFixture samples)
    {
        _samples = samples;
    }

    static PdfSharpDocumentAnalyzer NewAnalyzer() => new(new PdfPigThumbnailProvider());

    [Fact]
    public async Task TextDrawnOverAnImage_IsOneRegionWithBothKinds()
    {
        var info = await NewAnalyzer().AnalyzeAsync(_samples.ImageAndTextPath);

        var region = Assert.Single(info.OverlapRegions);
        Assert.Equal(1, region.PageNumber);
        Assert.Contains(region.Members, m => m.Kind == RemovableKind.Image);
        Assert.Contains(region.Members, m => m.Kind == RemovableKind.Text);
        // The region has to cover both, so it is at least as big as the image.
        Assert.True(region.Width > 100 && region.Height > 100);
    }

    [Fact]
    public async Task AStrokedFrameAroundTheBody_IsNotARegion()
    {
        // This sample draws a border rectangle and a rule on every page, both
        // stroke-only, with body text inside them. They hide nothing, so they do
        // not drag the text into a region — before the fill rule this produced
        // one region per page covering 77 % x 81 % of the paper.
        var info = await NewAnalyzer().AnalyzeAsync(_samples.RepeatedShapesPath);

        Assert.Empty(info.OverlapRegions);
    }

    [Fact]
    public async Task TextTooShortOrTooRareToBeRemovable_StillCountsForOverlap()
    {
        // The body text of this sample is drawn over the image and is shown once,
        // so it is not a removable text group — the object list filters to
        // strings shown 2+ times. Flattening exists for exactly this kind of
        // text (a chart's one-off labels), so detection must see it anyway.
        var info = await NewAnalyzer().AnalyzeAsync(_samples.ImageAndTextPath);

        var region = Assert.Single(info.OverlapRegions);
        var texts = region.Members
            .Where(m => m.Kind == RemovableKind.Text)
            .Select(m => m.Identity)
            .ToArray();
        Assert.NotEmpty(texts);
        Assert.All(texts, value => Assert.DoesNotContain(info.ImageGroups,
            g => g.Kind == RemovableKind.Text && g.TextValue == value));
    }

    [Fact]
    public async Task AnImageWithNothingOverIt_IsNotARegion()
    {
        var info = await NewAnalyzer().AnalyzeAsync(_samples.OneImagePath);

        Assert.Empty(info.OverlapRegions);
    }

    [Fact]
    public async Task RepeatedTextAlone_IsNotARegion()
    {
        // Header, footer and body text on plain pages: text over text is
        // excluded, and there is nothing else to overlap.
        var info = await NewAnalyzer().AnalyzeAsync(_samples.RepeatedTextPath);

        Assert.Empty(info.OverlapRegions);
    }

    [Fact]
    public async Task AnImageInsideAFormXObject_IsNeverPartOfARegion()
    {
        // A Form's content stream is shared, so it cannot be rewritten — the
        // same reason such an image is not safely removable. Flattening must not
        // offer it either.
        var info = await NewAnalyzer().AnalyzeAsync(_samples.FormEmbeddedImagePath);

        Assert.Empty(info.OverlapRegions);
    }

    [Fact]
    public async Task RegionMembersNameThingsTheCleanerCanMatch()
    {
        // An image member's identity is the stream hash the object list groups
        // by, and a text member's is the string as shown. Without that the
        // cleaner could not find the instances again.
        var info = await NewAnalyzer().AnalyzeAsync(_samples.ImageAndTextPath);
        var region = Assert.Single(info.OverlapRegions);

        var imageMember = region.Members.First(m => m.Kind == RemovableKind.Image);
        Assert.Contains(info.ImageGroups,
            g => g.Kind == RemovableKind.Image && g.Hash == imageMember.Identity);

        var textMember = region.Members.First(m => m.Kind == RemovableKind.Text);
        Assert.False(string.IsNullOrWhiteSpace(textMember.Identity));
    }
}
