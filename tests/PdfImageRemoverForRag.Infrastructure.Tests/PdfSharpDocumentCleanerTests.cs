using PdfImageRemoverForRag.Core.Errors;
using PdfImageRemoverForRag.Core.Models;
using PdfImageRemoverForRag.Infrastructure;
using Xunit;

namespace PdfImageRemoverForRag.Infrastructure.Tests;

public class PdfSharpDocumentCleanerTests : IClassFixture<SamplePdfFixture>
{
    readonly SamplePdfFixture _samples;

    public PdfSharpDocumentCleanerTests(SamplePdfFixture samples)
    {
        _samples = samples;
    }

    [Fact]
    public async Task CleaningARemovableGroup_ProducesAFileAndRecordsResult()
    {
        // Full round-trip: analyze → build selection for the (only) group →
        // clean → assert the destination exists and reports coherent metrics.
        var analyzer = new PdfSharpDocumentAnalyzer(new PdfPigThumbnailProvider());
        var info = await analyzer.AnalyzeAsync(_samples.OneImagePath);
        var group = info.ImageGroups.Single();
        var selection = new ImageRemovalSelection(
            group.GroupId, group.Occurrences, Hash: group.Hash);

        var dest = Path.Combine(_samples.TempDirectory, "one-image_cleaned.pdf");
        var cleaner = new PdfSharpDocumentCleaner();
        var result = await cleaner.CleanAsync(_samples.OneImagePath, dest,
            new[] { selection });

        Assert.True(File.Exists(dest));
        Assert.Equal(_samples.OneImagePath, result.SourcePath);
        Assert.Equal(dest, result.DestinationPath);
        Assert.Equal(1, result.PagesModified);
        Assert.Equal(1, result.DrawCallsRemoved);
        Assert.Contains(group.Hash, result.RemovedGroupHashes);
    }

    [Fact]
    public async Task CleaningTheSharedLogo_DropsAllFiveOccurrences()
    {
        var analyzer = new PdfSharpDocumentAnalyzer(new PdfPigThumbnailProvider());
        var info = await analyzer.AnalyzeAsync(_samples.RepeatedLogoPath);
        // The sample also has repeated body text; select only the logo image.
        var group = info.ImageGroups.Single(g => g.Kind == RemovableKind.Image);
        var dest = Path.Combine(_samples.TempDirectory, "repeated-logo_cleaned.pdf");
        var cleaner = new PdfSharpDocumentCleaner();

        var result = await cleaner.CleanAsync(_samples.RepeatedLogoPath, dest,
            new[] { new ImageRemovalSelection(group.GroupId, group.Occurrences, Hash: group.Hash) });

        Assert.Equal(5, result.PagesModified);
        Assert.Equal(5, result.DrawCallsRemoved);

        // Re-analyze the cleaned PDF: no image groups should remain (text stays).
        var reanalyzed = await analyzer.AnalyzeAsync(dest);
        Assert.DoesNotContain(reanalyzed.ImageGroups, g => g.Kind == RemovableKind.Image);
    }

    [Fact]
    public async Task CleaningOneOfThreeGroups_KeepsTheOtherTwo()
    {
        // A per-image removal — the other two images must remain intact.
        var analyzer = new PdfSharpDocumentAnalyzer(new PdfPigThumbnailProvider());
        var info = await analyzer.AnalyzeAsync(_samples.MultipleImagesPath);
        Assert.Equal(3, info.ImageGroups.Count);
        var target = info.ImageGroups[0];
        var dest = Path.Combine(_samples.TempDirectory, "multi_cleaned.pdf");
        var cleaner = new PdfSharpDocumentCleaner();

        await cleaner.CleanAsync(_samples.MultipleImagesPath, dest,
            new[] { new ImageRemovalSelection(target.GroupId, target.Occurrences, Hash: target.Hash) });

        var reanalyzed = await analyzer.AnalyzeAsync(dest);
        Assert.Equal(2, reanalyzed.ImageGroups.Count);
        Assert.DoesNotContain(reanalyzed.ImageGroups, g => g.Hash == target.Hash);
    }

    [Fact]
    public async Task ImagesAreMatchedByHash_NotByTheOccurrenceObjectIds()
    {
        // Identity for an image is its stream hash — the same thing the list
        // groups by and the verifier checks. It is deliberately NOT the
        // indirect-object id recorded on each occurrence.
        //
        // This is a regression test for a save that failed on a real 176-page
        // document: the same image bytes were stored as several objects, the
        // occurrence list named only some of them, and every page that used a
        // different copy kept its image. Passing occurrences whose object ids
        // are meaningless reproduces that shape — removal must still be
        // complete, because the hash still identifies the image.
        var analyzer = new PdfSharpDocumentAnalyzer(new PdfPigThumbnailProvider());
        var info = await analyzer.AnalyzeAsync(_samples.RepeatedLogoPath);
        var group = info.ImageGroups.Single(g => g.Kind == RemovableKind.Image);

        var occurrencesWithUnusableIds = group.Occurrences
            .Select(o => o with { ObjectId = "not-an-object-id" })
            .ToArray();

        var dest = Path.Combine(_samples.TempDirectory, "hash-match_cleaned.pdf");
        var result = await new PdfSharpDocumentCleaner().CleanAsync(
            _samples.RepeatedLogoPath, dest,
            new[] { new ImageRemovalSelection(
                group.GroupId, occurrencesWithUnusableIds, Hash: group.Hash) });

        Assert.Equal(5, result.PagesModified);
        Assert.Equal(5, result.DrawCallsRemoved);

        var reanalyzed = await analyzer.AnalyzeAsync(dest);
        Assert.DoesNotContain(reanalyzed.ImageGroups, g => g.Hash == group.Hash);
    }

    [Fact]
    public async Task DestinationEqualToSource_ThrowsPdfCleanerException()
    {
        // Spec §15 hard rule — never overwrite the source PDF.
        var cleaner = new PdfSharpDocumentCleaner();
        var occurrences = Array.Empty<PdfImageOccurrence>();
        var selection = new ImageRemovalSelection("IMG_001", occurrences);
        var ex = await Assert.ThrowsAsync<PdfCleanerException>(() =>
            cleaner.CleanAsync(_samples.OneImagePath, _samples.OneImagePath,
                new[] { selection }));
        Assert.Equal(PdfCleanerErrorKind.DestinationNotWritable, ex.Kind);
    }

    [Fact]
    public async Task EmptySelectionList_ThrowsPdfCleanerException()
    {
        var cleaner = new PdfSharpDocumentCleaner();
        var dest = Path.Combine(_samples.TempDirectory, "unused.pdf");
        await Assert.ThrowsAsync<PdfCleanerException>(() =>
            cleaner.CleanAsync(_samples.OneImagePath, dest,
                Array.Empty<ImageRemovalSelection>()));
    }
}
