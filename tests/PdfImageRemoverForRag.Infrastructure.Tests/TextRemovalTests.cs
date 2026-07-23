using PdfImageRemoverForRag.Core.Models;
using PdfImageRemoverForRag.Infrastructure;
using Xunit;
using PdfPigDoc = UglyToad.PdfPig.PdfDocument;

namespace PdfImageRemoverForRag.Infrastructure.Tests;

// End-to-end text-object removal against the repeated-text sample: the shared
// header/footer are detected, one selection removes them from every page, and
// the unique body text survives.
public class TextRemovalTests : IClassFixture<SamplePdfFixture>
{
    readonly SamplePdfFixture _samples;

    public TextRemovalTests(SamplePdfFixture samples)
    {
        _samples = samples;
    }

    static PdfSharpDocumentAnalyzer NewAnalyzer() =>
        new(new PdfPigThumbnailProvider());

    [Fact]
    public async Task RepeatedHeaderAndFooter_AreDetectedAsTextGroups()
    {
        var info = await NewAnalyzer().AnalyzeAsync(_samples.RepeatedTextPath);
        var textGroups = info.ImageGroups.Where(g => g.Kind == RemovableKind.Text).ToArray();

        // "CONFIDENTIAL" (3x) and "Company Footer 2026" (3x) repeat; the
        // per-page unique body line appears once and must NOT be listed.
        Assert.Contains(textGroups, g => g.TextValue == "CONFIDENTIAL" && g.UsageCount == 3);
        Assert.Contains(textGroups, g => g.TextValue == "Company Footer 2026" && g.UsageCount == 3);
        Assert.DoesNotContain(textGroups, g => g.TextValue!.StartsWith("Body paragraph"));
    }

    [Fact]
    public async Task RemovingTheHeader_DropsItFromEveryPage_AndKeepsBody()
    {
        var analyzer = NewAnalyzer();
        var info = await analyzer.AnalyzeAsync(_samples.RepeatedTextPath);
        var header = info.ImageGroups.Single(
            g => g.Kind == RemovableKind.Text && g.TextValue == "CONFIDENTIAL");

        var dest = Path.Combine(_samples.TempDirectory, "repeated-text_cleaned.pdf");
        var cleaner = new PdfSharpDocumentCleaner();
        var result = await cleaner.CleanAsync(_samples.RepeatedTextPath, dest, new[]
        {
            new ImageRemovalSelection(header.GroupId, header.Occurrences,
                RemovableKind.Text, header.TextValue),
        });

        Assert.Equal(3, result.PagesModified);
        Assert.Equal(3, result.DrawCallsRemoved);

        // Verify with an independent parser: header gone everywhere, body kept.
        using var pig = PdfPigDoc.Open(dest);
        var allText = string.Concat(pig.GetPages().Select(p => p.Text));
        Assert.DoesNotContain("CONFIDENTIAL", allText);
        Assert.Contains("Body paragraph unique to page", allText);
        Assert.Contains("Company Footer 2026", allText); // unselected text survives
    }

    [Fact]
    public async Task ReanalyzingCleanedFile_NoLongerListsRemovedText()
    {
        var analyzer = NewAnalyzer();
        var info = await analyzer.AnalyzeAsync(_samples.RepeatedTextPath);
        var footer = info.ImageGroups.Single(
            g => g.Kind == RemovableKind.Text && g.TextValue == "Company Footer 2026");

        var dest = Path.Combine(_samples.TempDirectory, "repeated-text_footer_cleaned.pdf");
        await new PdfSharpDocumentCleaner().CleanAsync(_samples.RepeatedTextPath, dest, new[]
        {
            new ImageRemovalSelection(footer.GroupId, footer.Occurrences,
                RemovableKind.Text, footer.TextValue),
        });

        var reanalyzed = await analyzer.AnalyzeAsync(dest);
        Assert.DoesNotContain(reanalyzed.ImageGroups,
            g => g.Kind == RemovableKind.Text && g.TextValue == "Company Footer 2026");
    }
}
