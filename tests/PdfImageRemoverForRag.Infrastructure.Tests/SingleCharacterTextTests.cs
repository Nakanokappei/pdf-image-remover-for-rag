using PdfImageRemoverForRag.Core.Models;
using PdfImageRemoverForRag.Infrastructure;
using Xunit;

namespace PdfImageRemoverForRag.Infrastructure.Tests;

// What makes a short string removable. A one-character confidentiality marking
// repeated on every page is exactly the kind of noise the feature is for, so
// length alone cannot be the gate — but a run of spaces is not an object anyone
// means to remove, and a row showing nothing would be worse than no row.
//
// The rule these pin: at least one character a reader can see, shown at least
// twice in the file.
public class SingleCharacterTextTests : IClassFixture<SamplePdfFixture>
{
    readonly SamplePdfFixture _samples;

    public SingleCharacterTextTests(SamplePdfFixture samples)
    {
        _samples = samples;
    }

    static PdfSharpDocumentAnalyzer NewAnalyzer() => new(new PdfPigThumbnailProvider());

    [Fact]
    public async Task ARepeatedSingleCharacter_IsRemovable()
    {
        var info = await NewAnalyzer().AnalyzeAsync(_samples.SingleCharacterTextPath);

        var marking = Assert.Single(info.ImageGroups,
            g => g.Kind == RemovableKind.Text && g.TextValue == "S");
        Assert.Equal(3, marking.UsageCount);
        Assert.Equal(new[] { 1, 2, 3 }, marking.UsagePages);
        Assert.True(marking.IsSafelyRemovable);
    }

    [Fact]
    public async Task AWhitespaceOnlyString_IsNotOffered()
    {
        var info = await NewAnalyzer().AnalyzeAsync(_samples.SingleCharacterTextPath);

        // Shown on all three pages, so only the readable-character rule keeps
        // it out — the repetition rule would have let it through.
        Assert.DoesNotContain(info.ImageGroups, g =>
            g.Kind == RemovableKind.Text
            && !string.IsNullOrEmpty(g.TextValue)
            && g.TextValue!.All(char.IsWhiteSpace));
    }

    [Fact]
    public async Task ASingleCharacterShownOnce_IsStillFiltered()
    {
        var info = await NewAnalyzer().AnalyzeAsync(_samples.SingleCharacterTextPath);

        // Lowering the character count did not touch the repetition rule: one
        // showing is not noise, it is content.
        Assert.DoesNotContain(info.ImageGroups,
            g => g.Kind == RemovableKind.Text && g.TextValue == "X");
    }
}
