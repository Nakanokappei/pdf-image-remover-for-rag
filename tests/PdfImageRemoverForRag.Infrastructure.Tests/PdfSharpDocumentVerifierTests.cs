using PdfImageRemoverForRag.Core.Models;
using PdfImageRemoverForRag.Infrastructure;
using Xunit;

namespace PdfImageRemoverForRag.Infrastructure.Tests;

public class PdfSharpDocumentVerifierTests : IClassFixture<SamplePdfFixture>
{
    readonly SamplePdfFixture _samples;

    public PdfSharpDocumentVerifierTests(SamplePdfFixture samples)
    {
        _samples = samples;
    }

    [Fact]
    public async Task GoodCleanedPdf_VerifiesAsOverallOk()
    {
        // Analyze → clean → verify — the golden path.
        var analyzer = new PdfSharpDocumentAnalyzer(new PdfPigThumbnailProvider());
        var info = await analyzer.AnalyzeAsync(_samples.MultipleImagesPath);
        var target = info.ImageGroups[0];
        var retained = info.ImageGroups.Skip(1).Select(g => g.Hash).ToArray();
        var dest = Path.Combine(_samples.TempDirectory, "verify-good_cleaned.pdf");

        var cleaner = new PdfSharpDocumentCleaner();
        var result = await cleaner.CleanAsync(_samples.MultipleImagesPath, dest,
            new[] { new ImageRemovalSelection(target.GroupId, target.Occurrences, Hash: target.Hash) });

        var verifier = new PdfSharpDocumentVerifier();
        var report = await verifier.VerifyAsync(_samples.MultipleImagesPath, dest,
            result.RemovedGroupHashes, retained);

        Assert.True(report.IsOverallOk, string.Join("; ", report.Warnings));
        Assert.True(report.PageCountMatches);
        Assert.True(report.NoDoOperatorsForRemovedImages);
        Assert.True(report.NonRemovedImageGroupsRetained);
    }

    [Fact]
    public async Task NonexistentCleanedFile_ReportsExceptionsAsWarnings()
    {
        // Verifier must not throw for missing/bogus cleaned files — it
        // returns a report with NoRuntimeExceptions = false and warnings.
        var verifier = new PdfSharpDocumentVerifier();
        var report = await verifier.VerifyAsync(_samples.OneImagePath,
            Path.Combine(_samples.TempDirectory, "missing.pdf"),
            Array.Empty<string>(), Array.Empty<string>());

        Assert.False(report.IsOverallOk);
        Assert.False(report.NoRuntimeExceptions);
        Assert.NotEmpty(report.Warnings);
    }
}
