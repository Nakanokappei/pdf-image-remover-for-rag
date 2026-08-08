using PdfImageRemoverForRag.Core.Models;
using PdfImageRemoverForRag.Infrastructure;
using PdfSharp.Pdf.IO;
using Xunit;

namespace PdfImageRemoverForRag.Infrastructure.Tests;

// Telling a shadow layer from a picture, and removing one.
//
// A shadow reaches a RAG pipeline as a solid black rectangle: the reader
// writes the picture out and drops the mask, so a layer nobody can see on the
// page arrives as a black box. Listing it as its own kind is what lets a user
// find it; these tests hold the line on WHICH objects get that label, because
// mislabelling a picture as a shadow would invite deleting real content.
public class ShadowLayerTests : IClassFixture<SamplePdfFixture>
{
    readonly SamplePdfFixture _samples;

    public ShadowLayerTests(SamplePdfFixture samples)
    {
        _samples = samples;
    }

    static PdfSharpDocumentAnalyzer NewAnalyzer() => new(new PdfPigThumbnailProvider());

    [Fact]
    public async Task OnlyTheFlatColoredLayer_IsCalledAShadow()
    {
        var info = await NewAnalyzer().AnalyzeAsync(_samples.ShadowLayerPath);

        // One of the three drawn images is a shadow. The other two are what
        // the rule must NOT catch: a picture that happens to carry a mask,
        // and a flat color with no mask at all.
        var shadow = Assert.Single(info.ObjectGroups, g => g.Kind == RemovableKind.Shadow);
        Assert.Equal("SHD_001", shadow.GroupId);
        Assert.Equal(2, info.ObjectGroups.Count(g => g.Kind == RemovableKind.Image));
    }

    [Fact]
    public async Task AShadow_IsRemovedLikeAnImage()
    {
        var info = await NewAnalyzer().AnalyzeAsync(_samples.ShadowLayerPath);
        var shadow = info.ObjectGroups.Single(g => g.Kind == RemovableKind.Shadow);

        var destination = Path.Combine(_samples.TempDirectory, "shadow-removed.pdf");
        var result = await new PdfSharpDocumentCleaner().CleanAsync(
            _samples.ShadowLayerPath, destination,
            new[]
            {
                new ObjectRemovalSelection(
                    shadow.GroupId, shadow.Occurrences, shadow.Kind, shadow.TextValue, shadow.Hash),
            });

        Assert.Equal(1, result.PagesModified);
        Assert.Equal(1, result.DrawCallsRemoved);

        // Gone from the file, not merely undrawn: the black rectangle only
        // stops appearing downstream once the object itself is out.
        using var cleaned = PdfReader.Open(destination, PdfDocumentOpenMode.Import);
        var names = cleaned.Pages[0].Elements
            .GetDictionary("/Resources")?.Elements.GetDictionary("/XObject")?.Elements.Keys;
        Assert.NotNull(names);
        Assert.DoesNotContain("/ImShadow", names!);

        // And the two images it sat beside are untouched.
        var after = await NewAnalyzer().AnalyzeAsync(destination);
        Assert.Equal(2, after.ObjectGroups.Count(g => g.Kind == RemovableKind.Image));
        Assert.DoesNotContain(after.ObjectGroups, g => g.Kind == RemovableKind.Shadow);
    }

    [Fact]
    public async Task TheSavedFile_PassesVerificationForARemovedShadow()
    {
        var info = await NewAnalyzer().AnalyzeAsync(_samples.ShadowLayerPath);
        var shadow = info.ObjectGroups.Single(g => g.Kind == RemovableKind.Shadow);

        var destination = Path.Combine(_samples.TempDirectory, "shadow-removed-verified.pdf");
        await new PdfSharpDocumentCleaner().CleanAsync(
            _samples.ShadowLayerPath, destination,
            new[]
            {
                new ObjectRemovalSelection(
                    shadow.GroupId, shadow.Occurrences, shadow.Kind, shadow.TextValue, shadow.Hash),
            });

        var verification = await new PdfSharpDocumentVerifier().VerifyAsync(
            _samples.ShadowLayerPath, destination,
            removedGroupHashes: new[] { shadow.Hash },
            retainedGroupHashes: info.ObjectGroups
                .Where(g => g.Kind == RemovableKind.Image)
                .Select(g => g.Hash)
                .ToArray());

        Assert.True(verification.RemovedImagesGoneFromResources);
        Assert.True(verification.NoDoOperatorsForRemovedImages);
        Assert.True(verification.NonRemovedImageGroupsRetained);
        Assert.Empty(verification.Warnings);
    }

    [Fact]
    public async Task AMaskedPicture_StaysAnImage()
    {
        // The first attempt at a rule judged the mask, and the sample it would
        // have caught is this one: a picture whose mask is nowhere near opaque
        // is still a picture. What decides is the color count.
        var info = await NewAnalyzer().AnalyzeAsync(_samples.SoftMaskedImagePath);

        var image = Assert.Single(info.ObjectGroups, g => g.Kind == RemovableKind.Image);
        Assert.Equal("IMG_001", image.GroupId);
        Assert.DoesNotContain(info.ObjectGroups, g => g.Kind == RemovableKind.Shadow);
    }
}
