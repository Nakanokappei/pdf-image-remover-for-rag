using System.Text;
using PdfImageRemoverForRag.Core.Models;
using PdfImageRemoverForRag.Infrastructure;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;
using Xunit;

namespace PdfImageRemoverForRag.Infrastructure.Tests;

// Pins the gap reported against a real customer document: a person silhouette
// and a speech bubble, both drawn inside a Form XObject, never appear in the
// object list. Analysis reads a page's own content stream for shapes and text
// and enters a form only to collect the images inside it, so a form that paints
// nothing but paths contributes nothing to the list.
//
// These assertions describe what the analyzer does TODAY, not what it should
// do. They are the tests to invert once form-drawn artwork becomes a listable
// object.
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
    public async Task ShapesDrawnInsideAForm_AreNotDiscovered()
    {
        var info = await NewAnalyzer().AnalyzeAsync(_samples.FormDrawnShapesPath);

        // The page-level border rectangle is found, on both pages — proof the
        // shape detector ran and worked on this very document.
        var shapes = info.ImageGroups.Where(g => g.Kind == RemovableKind.Shape).ToArray();
        var border = Assert.Single(shapes);
        Assert.Equal(2, border.UsageCount);
        Assert.Equal(new[] { 1, 2 }, border.UsagePages);

        // The form's head, body and bubble are absent, and nothing else stands
        // in for them: the border is the whole list.
        Assert.Single(info.ImageGroups);
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
