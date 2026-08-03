using System.Text;
using PdfImageRemoverForRag.Core.Models;
using PdfImageRemoverForRag.Infrastructure;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
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

    [Fact]
    public async Task RemovingADrawing_TakesItOffEveryPageThatDrewIt()
    {
        var destination = Path.Combine(Path.GetTempPath(), $"drawing-removed-{Guid.NewGuid():N}.pdf");
        try
        {
            var info = await NewAnalyzer().AnalyzeAsync(_samples.FormDrawnShapesPath);
            var drawing = info.ImageGroups.Single(g => g.Kind == RemovableKind.Drawing);

            var result = await new PdfSharpDocumentCleaner().CleanAsync(
                _samples.FormDrawnShapesPath, destination,
                new[]
                {
                    new ImageRemovalSelection(
                        drawing.GroupId, drawing.Occurrences, drawing.Kind, drawing.TextValue, drawing.Hash),
                });

            // Both pages drew it, so both pages change.
            Assert.Equal(2, result.PagesModified);
            Assert.Equal(2, result.DrawCallsRemoved);
            Assert.Equal(0, result.ImagesKeptForOtherReferences);

            // Nothing in the saved file draws or even lists a form any more.
            using var cleaned = PdfReader.Open(destination, PdfDocumentOpenMode.Import);
            Assert.Equal(2, cleaned.PageCount);
            for (var i = 0; i < cleaned.PageCount; i++)
            {
                var forms = FormEntriesOf(cleaned.Pages[i]);
                Assert.Empty(forms);
            }
        }
        finally
        {
            if (File.Exists(destination)) File.Delete(destination);
        }
    }

    [Fact]
    public async Task RemovingADrawing_LeavesThePagesOwnShapeAlone()
    {
        // The border rectangle is painted by the page itself, not by the form.
        // Removing the drawing must not touch it — if it did, the removal would
        // be rewriting more of the content stream than the selection covers.
        var destination = Path.Combine(Path.GetTempPath(), $"drawing-removed-{Guid.NewGuid():N}.pdf");
        try
        {
            var analyzer = NewAnalyzer();
            var info = await analyzer.AnalyzeAsync(_samples.FormDrawnShapesPath);
            var drawing = info.ImageGroups.Single(g => g.Kind == RemovableKind.Drawing);

            await new PdfSharpDocumentCleaner().CleanAsync(
                _samples.FormDrawnShapesPath, destination,
                new[]
                {
                    new ImageRemovalSelection(
                        drawing.GroupId, drawing.Occurrences, drawing.Kind, drawing.TextValue, drawing.Hash),
                });

            var after = await analyzer.AnalyzeAsync(destination);
            var border = Assert.Single(after.ImageGroups, g => g.Kind == RemovableKind.Shape);
            Assert.Equal(2, border.UsageCount);
            // And the drawing is not merely undrawn — it is gone from the list.
            Assert.DoesNotContain(after.ImageGroups, g => g.Kind == RemovableKind.Drawing);
        }
        finally
        {
            if (File.Exists(destination)) File.Delete(destination);
        }
    }

    [Fact]
    public async Task TheSavedFile_PassesVerificationForARemovedDrawing()
    {
        // The post-save check has to cover drawings too, or a save that left a
        // form behind would still be reported as verified.
        var destination = Path.Combine(Path.GetTempPath(), $"drawing-removed-{Guid.NewGuid():N}.pdf");
        try
        {
            var info = await NewAnalyzer().AnalyzeAsync(_samples.FormDrawnShapesPath);
            var drawing = info.ImageGroups.Single(g => g.Kind == RemovableKind.Drawing);

            await new PdfSharpDocumentCleaner().CleanAsync(
                _samples.FormDrawnShapesPath, destination,
                new[]
                {
                    new ImageRemovalSelection(
                        drawing.GroupId, drawing.Occurrences, drawing.Kind, drawing.TextValue, drawing.Hash),
                });

            var verification = await new PdfSharpDocumentVerifier().VerifyAsync(
                _samples.FormDrawnShapesPath, destination,
                removedGroupHashes: new[] { drawing.Hash },
                retainedGroupHashes: Array.Empty<string>());

            Assert.True(verification.RemovedImagesGoneFromResources);
            Assert.True(verification.NoDoOperatorsForRemovedImages);
            Assert.Empty(verification.Warnings);
        }
        finally
        {
            if (File.Exists(destination)) File.Delete(destination);
        }
    }

    // Every Form XObject a page still names in its resources.
    static List<string> FormEntriesOf(PdfPage page)
    {
        var names = new List<string>();
        var xobjects = page.Elements.GetDictionary("/Resources")?.Elements.GetDictionary("/XObject");
        if (xobjects is null) return names;

        foreach (var kv in xobjects.Elements)
        {
            var dict = kv.Value switch
            {
                PdfDictionary d => d,
                PdfReference r => r.Value as PdfDictionary,
                _ => null,
            };
            if (dict?.Elements.GetName("/Subtype") == "/Form") names.Add(kv.Key);
        }
        return names;
    }
}
