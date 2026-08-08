using System.Text;
using PdfImageRemoverForRag.Core.Models;
using PdfImageRemoverForRag.Infrastructure;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;
using Xunit;

namespace PdfImageRemoverForRag.Infrastructure.Tests;

// The gap reported against a real customer document, now closed: a person
// silhouette and a speech bubble drawn inside a Form XObject did not appear in
// the object list, because analysis read a page's own content stream for shapes
// and text and entered a form only to collect the images inside it.
//
// A form's artwork is one Drawing however many paths it holds, because the
// form's content stream is shared by every page that draws it and only the
// page's own Do call can be removed safely.
public class FormDrawnShapeTests : IClassFixture<SamplePdfFixture>
{
    readonly SamplePdfFixture _samples;

    public FormDrawnShapeTests(SamplePdfFixture samples)
    {
        _samples = samples;
    }

    static PdfSharpDocumentAnalyzer NewAnalyzer() => new(new PdfPigThumbnailProvider());

    [Fact]
    public void TheSample_DrawsPathsInsideAFormAndNoImageWithIt()
    {
        // Without this, the "nothing is discovered" test below would also pass
        // against an empty document — it would assert nothing at all. Everything
        // here is read straight out of the PDF, with no help from the analyzer.
        using var doc = PdfReader.Open(_samples.FormDrawnShapesPath, PdfDocumentOpenMode.Import);
        var page = doc.Pages[0];

        // The page must name a Form XObject...
        var xObjects = page.Elements.GetDictionary("/Resources")?.Elements.GetDictionary("/XObject");
        Assert.NotNull(xObjects);
        var form = xObjects!.Elements
            .Select(kv => (Name: kv.Key, Dictionary: Resolve(kv.Value)))
            .Single(entry => entry.Dictionary?.Elements.GetName("/Subtype") == "/Form");

        // ...and must actually invoke it, since naming a form in the resources
        // draws nothing on its own.
        var pageContent = new StringBuilder();
        foreach (var stream in page.Contents)
            pageContent.Append(Encoding.Latin1.GetString(stream.Stream.UnfilteredValue));
        Assert.Contains($"{form.Name} Do", pageContent.ToString(), StringComparison.Ordinal);

        // The form paints paths: filled and stroked, which is exactly what the
        // page-level shape detector would have found had they been on the page.
        var formContent = Encoding.Latin1.GetString(
            form.Dictionary!.Stream?.UnfilteredValue ?? Array.Empty<byte>());
        Assert.True(HasOperator(formContent, "f"), "the form should fill at least one path");
        Assert.True(HasOperator(formContent, "S"), "the form should stroke at least one path");

        // And it holds no image, so nothing about it can reach the list through
        // the one form-entering path analysis does have.
        var formXObjects = form.Dictionary.Elements
            .GetDictionary("/Resources")?.Elements.GetDictionary("/XObject");
        var imagesInForm = formXObjects is null
            ? 0
            : formXObjects.Elements.Count(kv => Resolve(kv.Value)?.Elements.GetName("/Subtype") == "/Image");
        Assert.Equal(0, imagesInForm);
    }

    [Fact]
    public async Task ShapesDrawnInsideAForm_AreOneDrawing()
    {
        var info = await NewAnalyzer().AnalyzeAsync(_samples.FormDrawnShapesPath);

        // The page-level border rectangle stays a Shape — the new kind must not
        // swallow the paths that were already found.
        var shapes = info.ObjectGroups.Where(g => g.Kind == RemovableKind.Shape).ToArray();
        var border = Assert.Single(shapes);
        Assert.Equal(2, border.UsageCount);

        // The form's head, body and bubble are ONE object, not three, drawn on
        // both pages.
        var drawings = info.ObjectGroups.Where(g => g.Kind == RemovableKind.Drawing).ToArray();
        var drawing = Assert.Single(drawings);
        Assert.Equal("DRW_001", drawing.GroupId);
        Assert.Equal(2, drawing.UsageCount);
        Assert.Equal(new[] { 1, 2 }, drawing.UsagePages);
        Assert.True(drawing.IsSafelyRemovable);

        // All three paths are carried, with both paint operators.
        Assert.NotNull(drawing.DrawingGeometry);
        Assert.Equal(3, drawing.DrawingGeometry!.Parts.Count);
        Assert.Equal(2, drawing.DrawingGeometry.Parts.Count(p => p.IsFilled));
    }

    [Fact]
    public async Task ADrawing_LandsWhereTheFormWasPlacedOnThePage()
    {
        // The arithmetic this checks is the reason the kind needed the CTM at
        // the Do call: a form is not drawn in the unit square, so its rectangle
        // is its own box mapped through /Matrix and then through the placement.
        //
        // The sample paints, in the form's 120-point box, an ellipse at
        // (30,10)-(70,50), a rectangle at (20,58)-(80,98) and a bubble at
        // (75,15)-(115,45), all in XGraphics' top-left coordinates. Their union
        // is x 20..115 and, flipped into PDF's bottom-up space, y 22..110. The
        // form is placed at (60,600) from the top of an 842-point page, so its
        // origin sits at y 122 and the artwork covers (80,144) to (175,232).
        var info = await NewAnalyzer().AnalyzeAsync(_samples.FormDrawnShapesPath);
        var drawing = info.ObjectGroups.Single(g => g.Kind == RemovableKind.Drawing);

        var first = drawing.Occurrences.First(o => o.PageNumber == 1);
        Assert.Equal(80, first.X, precision: 0);
        Assert.Equal(144, first.Y, precision: 0);
        Assert.Equal(95, first.Width, precision: 0);
        Assert.Equal(88, first.Height, precision: 0);

        // The size shown in the list is that rectangle, in points.
        Assert.Equal(95, drawing.PixelWidth);
        Assert.Equal(88, drawing.PixelHeight);
    }

    // A PDF operator is a token bounded by whitespace; "f" must not match the
    // "f" inside a resource name.
    static bool HasOperator(string content, string op)
    {
        for (var i = 0; i <= content.Length - op.Length; i++)
        {
            if (string.CompareOrdinal(content, i, op, 0, op.Length) != 0) continue;
            var before = i == 0 || char.IsWhiteSpace(content[i - 1]);
            var afterIndex = i + op.Length;
            var after = afterIndex >= content.Length || char.IsWhiteSpace(content[afterIndex]);
            if (before && after) return true;
        }
        return false;
    }

    static PdfDictionary? Resolve(PdfItem? item) => item switch
    {
        PdfDictionary d => d,
        PdfReference r => r.Value as PdfDictionary,
        _ => null,
    };
}
