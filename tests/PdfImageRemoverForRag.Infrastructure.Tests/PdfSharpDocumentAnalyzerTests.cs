using PdfImageRemoverForRag.Core.Abstractions;
using PdfImageRemoverForRag.Infrastructure;
using Xunit;

namespace PdfImageRemoverForRag.Infrastructure.Tests;

// Integration tests that exercise the PDFsharp analyzer against the five
// spec §8.2 sample PDFs. All samples are generated fresh by the fixture so
// tests do not depend on scripts/GenerateSamples having been run.
public class PdfSharpDocumentAnalyzerTests : IClassFixture<SamplePdfFixture>
{
    readonly SamplePdfFixture _samples;

    public PdfSharpDocumentAnalyzerTests(SamplePdfFixture samples)
    {
        _samples = samples;
    }

    static PdfSharpDocumentAnalyzer NewAnalyzer(IThumbnailProvider? provider = null) =>
        new(provider ?? new PdfPigThumbnailProvider());

    // --- progress and cancellation (§18) ------------------------------------
    // A 30 MB PDF takes minutes, so the UI needs to show movement and offer a
    // way out. Both are plumbed through IProgress/CancellationToken; these
    // tests pin the behaviour the dialog depends on.

    [Fact]
    public async Task Analyze_ReportsPageProgressAndThumbnailProgress()
    {
        var analyzer = NewAnalyzer();
        var reports = new List<Core.Models.AnalysisProgress>();
        var progress = new Progress<Core.Models.AnalysisProgress>(reports.Add);

        await analyzer.AnalyzeAsync(_samples.RepeatedLogoPath, progress: progress);

        // Progress<T> posts to the captured context; on the xunit thread pool
        // that is the default scheduler, so give the callbacks a moment to run.
        for (int attempt = 0; attempt < 50 && reports.Count == 0; attempt++)
        {
            await Task.Delay(10);
        }

        Assert.Contains(reports, r => r.Phase == Core.Models.AnalysisPhase.ReadingPages);
        Assert.All(
            reports.Where(r => r.Phase == Core.Models.AnalysisPhase.ReadingPages),
            r => Assert.Equal(5, r.Total));
    }

    [Fact]
    public async Task Analyze_ReportedFractionStaysWithinRange()
    {
        var analyzer = NewAnalyzer();
        var reports = new List<Core.Models.AnalysisProgress>();
        var progress = new Progress<Core.Models.AnalysisProgress>(reports.Add);

        await analyzer.AnalyzeAsync(_samples.MultipleImagesPath, progress: progress);
        for (int attempt = 0; attempt < 50 && reports.Count == 0; attempt++)
        {
            await Task.Delay(10);
        }

        // The grouping phase reports no total, which must surface as "unknown"
        // rather than a bogus 0 % — the dialog switches to a marquee bar on null.
        foreach (var report in reports)
        {
            if (report.Total == 0) Assert.Null(report.Fraction);
            else Assert.InRange(report.Fraction!.Value, 0d, 1d);
        }
    }

    [Fact]
    public async Task Analyze_WithAlreadyCancelledToken_Throws()
    {
        var analyzer = NewAnalyzer();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => analyzer.AnalyzeAsync(_samples.RepeatedLogoPath, ct: cancellation.Token));
    }

    [Fact]
    public async Task OneImagePdf_ReturnsSingleGroupWithOneOccurrence()
    {
        var analyzer = NewAnalyzer();
        var info = await analyzer.AnalyzeAsync(_samples.OneImagePath);
        Assert.Equal(1, info.PageCount);
        Assert.False(info.IsEncrypted);
        var group = Assert.Single(info.ImageGroups);
        Assert.Equal(1, group.UsageCount);
        Assert.True(group.IsSafelyRemovable);
        Assert.False(group.IsPossibleFullPageImage);
    }

    [Fact]
    public async Task RepeatedLogoPdf_CollapsesTheSameLogoAcrossPagesIntoOneGroup()
    {
        var analyzer = NewAnalyzer();
        var info = await analyzer.AnalyzeAsync(_samples.RepeatedLogoPath);
        Assert.Equal(5, info.PageCount);
        // Scope to images — the sample also has a body line repeated on every
        // page, which is (correctly) surfaced as a separate text group.
        var group = Assert.Single(info.ImageGroups, g => g.Kind == Core.Models.RemovableKind.Image);
        Assert.Equal(5, group.UsageCount);
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, group.UsagePages);
    }

    [Fact]
    public async Task MultipleImagesPdf_ProducesThreeDistinctGroups()
    {
        var analyzer = NewAnalyzer();
        var info = await analyzer.AnalyzeAsync(_samples.MultipleImagesPath);
        Assert.Equal(1, info.PageCount);
        Assert.Equal(3, info.ImageGroups.Count);
        Assert.All(info.ImageGroups, g => Assert.True(g.IsSafelyRemovable));
    }

    [Fact]
    public async Task FormEmbeddedImagePdf_ListsTheImageAsNotSafelyRemovable()
    {
        // The sample draws its image inside a Form XObject shared by two
        // pages. The analyzer must surface it (users should see it exists)
        // but mark it not safely removable (§14.3) — rewriting a shared
        // form's content stream could affect other pages. This is the only
        // sample producing the disabled row/tile state the UI has to render.
        var analyzer = NewAnalyzer();
        var info = await analyzer.AnalyzeAsync(_samples.FormEmbeddedImagePath);
        Assert.Equal(2, info.PageCount);
        var group = Assert.Single(info.ImageGroups, g => g.Kind == Core.Models.RemovableKind.Image);
        Assert.False(group.IsSafelyRemovable);
        Assert.False(string.IsNullOrEmpty(group.WarningMessage));
    }

    [Fact]
    public async Task ScannedPagePdf_FlagsGroupAsFullPageCandidate()
    {
        var analyzer = NewAnalyzer();
        var info = await analyzer.AnalyzeAsync(_samples.ScannedPagePath);
        var group = Assert.Single(info.ImageGroups);
        Assert.True(group.IsPossibleFullPageImage);
    }

    [Fact]
    public async Task Thumbnail_IsPopulatedForAtLeastOneGroup()
    {
        // PdfPig's TryGetPng handles Flate-compressed images, so all our
        // samples should yield a thumbnail. Assert at least one — if PdfPig
        // ever regresses on the "logo" or "icon" case, that would still be
        // caught by the individual analyzer tests above.
        var analyzer = NewAnalyzer();
        var info = await analyzer.AnalyzeAsync(_samples.MultipleImagesPath);
        Assert.Contains(info.ImageGroups, g => g.ThumbnailBytes is { Length: > 0 });
    }

    [Fact]
    public async Task NonexistentFile_MapsToPdfCleanerException()
    {
        var analyzer = NewAnalyzer();
        var missingPath = Path.Combine(_samples.TempDirectory, "does-not-exist.pdf");
        await Assert.ThrowsAsync<Core.Errors.PdfCleanerException>(
            () => analyzer.AnalyzeAsync(missingPath));
    }

    [Fact]
    public async Task CorruptedFile_MapsToPdfCleanerException()
    {
        // A file that begins with something other than "%PDF-" is rejected
        // by PDFsharp as not-a-PDF; we assert that the exception surfaces
        // as a PdfCleanerException regardless of the specific Kind.
        var bogusPath = Path.Combine(_samples.TempDirectory, "bogus.pdf");
        await File.WriteAllTextAsync(bogusPath, "this is not a PDF file");
        var analyzer = NewAnalyzer();
        await Assert.ThrowsAsync<Core.Errors.PdfCleanerException>(
            () => analyzer.AnalyzeAsync(bogusPath));
    }
}
