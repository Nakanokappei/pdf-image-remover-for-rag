using PdfImageRemoverForRag.Core.Models;
using PdfImageRemoverForRag.Infrastructure;
using PdfImageRemoverForRag.Infrastructure.Internal;
using PdfSharp.Pdf.IO;
using Xunit;

namespace PdfImageRemoverForRag.Infrastructure.Tests;

// End-to-end removal of a drawing against the form-drawn-shapes sample. What is
// deleted is the page's own Do call and its resource entry, never the form's
// content stream, and the page keeps everything else it was carrying.
public class DrawingRemovalTests : IClassFixture<SamplePdfFixture>
{
    readonly SamplePdfFixture _samples;

    public DrawingRemovalTests(SamplePdfFixture samples)
    {
        _samples = samples;
    }

    static PdfSharpDocumentAnalyzer NewAnalyzer() => new(new PdfPigThumbnailProvider());

    /// <summary>
    /// Remove the sample's one drawing and hand back where it was written.
    /// The destination sits in the fixture's directory, which is deleted with
    /// the fixture — no test has to remember to clean up after itself, and one
    /// that fails an assertion still leaves nothing behind.
    /// </summary>
    async Task<(string Destination, CleaningResult Result)> RemoveTheDrawingAsync(string name)
    {
        var info = await NewAnalyzer().AnalyzeAsync(_samples.FormDrawnShapesPath);
        var drawing = info.ObjectGroups.Single(g => g.Kind == RemovableKind.Drawing);

        var destination = Path.Combine(_samples.TempDirectory, $"{name}.pdf");
        var result = await new PdfSharpDocumentCleaner().CleanAsync(
            _samples.FormDrawnShapesPath, destination,
            new[]
            {
                new ObjectRemovalSelection(
                    drawing.GroupId, drawing.Occurrences, drawing.Kind, drawing.TextValue, drawing.Hash),
            });
        return (destination, result);
    }

    [Fact]
    public async Task RemovingADrawing_TakesItOffEveryPageThatDrewIt()
    {
        var (destination, result) = await RemoveTheDrawingAsync("drawing-removed-every-page");

        // Both pages drew it, so both pages change.
        Assert.Equal(2, result.PagesModified);
        Assert.Equal(2, result.DrawCallsRemoved);
        Assert.Equal(0, result.ImagesKeptForOtherReferences);

        // Nothing in the saved file draws or even lists a form any more.
        using var cleaned = PdfReader.Open(destination, PdfDocumentOpenMode.Import);
        Assert.Equal(2, cleaned.PageCount);
        for (var i = 0; i < cleaned.PageCount; i++)
        {
            Assert.Empty(ImageXObjectCollector.EnumerateFormEntries(cleaned.Pages[i].Resources));
        }
    }

    [Fact]
    public async Task RemovingADrawing_LeavesThePagesOwnShapeAlone()
    {
        // The border rectangle is painted by the page itself, not by the form.
        // Removing the drawing must not touch it — if it did, the removal would
        // be rewriting more of the content stream than the selection covers.
        var (destination, _) = await RemoveTheDrawingAsync("drawing-removed-shape-alone");

        var after = await NewAnalyzer().AnalyzeAsync(destination);
        var border = Assert.Single(after.ObjectGroups, g => g.Kind == RemovableKind.Shape);
        Assert.Equal(2, border.UsageCount);
        // And the drawing is not merely undrawn — it is gone from the list.
        Assert.DoesNotContain(after.ObjectGroups, g => g.Kind == RemovableKind.Drawing);
    }

    [Fact]
    public async Task TheSavedFile_PassesVerificationForARemovedDrawing()
    {
        // The post-save check has to cover drawings too, or a save that left a
        // form behind would still be reported as verified.
        var info = await NewAnalyzer().AnalyzeAsync(_samples.FormDrawnShapesPath);
        var drawing = info.ObjectGroups.Single(g => g.Kind == RemovableKind.Drawing);
        var (destination, _) = await RemoveTheDrawingAsync("drawing-removed-verified");

        var verification = await new PdfSharpDocumentVerifier().VerifyAsync(
            _samples.FormDrawnShapesPath, destination,
            removedGroupHashes: new[] { drawing.Hash },
            retainedGroupHashes: Array.Empty<string>());

        Assert.True(verification.RemovedImagesGoneFromResources);
        Assert.True(verification.NoDoOperatorsForRemovedImages);
        Assert.Empty(verification.Warnings);
    }
}
