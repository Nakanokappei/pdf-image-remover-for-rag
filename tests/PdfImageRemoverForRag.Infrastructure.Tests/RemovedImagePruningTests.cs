using PdfImageRemoverForRag.Core.Models;
using PdfImageRemoverForRag.Infrastructure;
using PdfImageRemoverForRag.Infrastructure.Internal;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;
using Xunit;

namespace PdfImageRemoverForRag.Infrastructure.Tests;

// Removing an image has to take it OUT OF THE FILE, not merely stop it being
// painted. The product exists to keep images away from a RAG pipeline, and such
// a pipeline reads a PDF by enumerating its objects rather than by rendering
// pages — so an image whose Do operator is gone but whose XObject remains is
// still delivered to exactly the consumer the user was protecting against.
//
// Found on a real 39-page manual: 27 "removed" images and 26 of their soft
// masks were all still present and extractable in the cleaned file.
public class RemovedImagePruningTests : IClassFixture<SamplePdfFixture>
{
    readonly SamplePdfFixture _samples;

    public RemovedImagePruningTests(SamplePdfFixture samples)
    {
        _samples = samples;
    }

    static PdfSharpDocumentAnalyzer NewAnalyzer() => new(new PdfPigThumbnailProvider());

    [Fact]
    public async Task RemovingAnImage_TakesItsXObjectOutOfEveryPage()
    {
        var info = await NewAnalyzer().AnalyzeAsync(_samples.RepeatedLogoPath);
        var group = info.ImageGroups.Single(g => g.Kind == RemovableKind.Image);
        var dest = Path.Combine(_samples.TempDirectory, "pruning-logo_cleaned.pdf");

        await new PdfSharpDocumentCleaner().CleanAsync(
            _samples.RepeatedLogoPath, dest,
            new[] { new ImageRemovalSelection(group.GroupId, group.Occurrences, Hash: group.Hash) });

        // The logo is drawn on five pages, so this also covers "removed from
        // every page's resources", not just the first one that mentioned it.
        Assert.DoesNotContain(group.Hash, HashesInPageResources(dest));
    }

    [Fact]
    public async Task RemovingAnImage_LeavesTheOtherImagesAlone()
    {
        // The pruning walks every page and deletes by hash, so the guard that
        // matters is that it deletes ONLY what was selected.
        var info = await NewAnalyzer().AnalyzeAsync(_samples.MultipleImagesPath);
        var images = info.ImageGroups.Where(g => g.Kind == RemovableKind.Image).ToArray();
        Assert.True(images.Length > 1, "sample must hold more than one image to make this meaningful");

        var doomed = images[0];
        var dest = Path.Combine(_samples.TempDirectory, "pruning-multiple_cleaned.pdf");
        await new PdfSharpDocumentCleaner().CleanAsync(
            _samples.MultipleImagesPath, dest,
            new[] { new ImageRemovalSelection(doomed.GroupId, doomed.Occurrences, Hash: doomed.Hash) });

        var remaining = HashesInPageResources(dest);
        Assert.DoesNotContain(doomed.Hash, remaining);
        foreach (var kept in images.Skip(1))
        {
            Assert.Contains(kept.Hash, remaining);
        }
    }

    [Fact]
    public async Task ReopeningACleanedFile_FindsNoLeftoverImages()
    {
        // The analyzer filters out images no Do operator references, which is
        // what made this defect invisible from inside the app: the leftovers
        // never appeared in its own object list. Re-analysis must now agree
        // with the file rather than paper over it.
        var info = await NewAnalyzer().AnalyzeAsync(_samples.RepeatedLogoPath);
        var group = info.ImageGroups.Single(g => g.Kind == RemovableKind.Image);
        var dest = Path.Combine(_samples.TempDirectory, "pruning-reopen_cleaned.pdf");

        await new PdfSharpDocumentCleaner().CleanAsync(
            _samples.RepeatedLogoPath, dest,
            new[] { new ImageRemovalSelection(group.GroupId, group.Occurrences, Hash: group.Hash) });

        var reopened = await NewAnalyzer().AnalyzeAsync(dest);
        Assert.DoesNotContain(reopened.ImageGroups, g => g.Kind == RemovableKind.Image);
        Assert.Empty(HashesInPageResources(dest));
        // And not merely unlisted. Both the resource check above and the
        // verifier's own ask "does the page still name it"; the promise being
        // made is that the bytes have left the file, which is a question about
        // the object table. An extractor walking xrefs reads that, not
        // resources, so this is the assertion that matches the claim.
        Assert.Empty(ImageObjectsInFile(dest));
    }

    [Fact]
    public async Task TheVerifier_FailsAFileThatStillListsARemovedImage()
    {
        // The original file still holds the image, so verifying it AS IF it
        // were the cleaned output is the cheapest way to prove the new check
        // actually fires — and that it would have caught the shipped defect.
        var info = await NewAnalyzer().AnalyzeAsync(_samples.RepeatedLogoPath);
        var group = info.ImageGroups.Single(g => g.Kind == RemovableKind.Image);

        var report = await new PdfSharpDocumentVerifier().VerifyAsync(
            _samples.RepeatedLogoPath, _samples.RepeatedLogoPath,
            removedGroupHashes: new[] { group.Hash },
            retainedGroupHashes: Array.Empty<string>());

        Assert.False(report.RemovedImagesGoneFromResources);
        Assert.False(report.IsOverallOk);
        Assert.Contains(report.Warnings, w => w.Contains("/XObject", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheVerifier_PassesAProperlyPrunedFile()
    {
        var info = await NewAnalyzer().AnalyzeAsync(_samples.MultipleImagesPath);
        var images = info.ImageGroups.Where(g => g.Kind == RemovableKind.Image).ToArray();
        var doomed = images[0];
        var dest = Path.Combine(_samples.TempDirectory, "pruning-verified_cleaned.pdf");

        await new PdfSharpDocumentCleaner().CleanAsync(
            _samples.MultipleImagesPath, dest,
            new[] { new ImageRemovalSelection(doomed.GroupId, doomed.Occurrences, Hash: doomed.Hash) });

        var report = await new PdfSharpDocumentVerifier().VerifyAsync(
            _samples.MultipleImagesPath, dest,
            removedGroupHashes: new[] { doomed.Hash },
            retainedGroupHashes: images.Skip(1).Select(g => g.Hash).ToArray());

        Assert.True(report.RemovedImagesGoneFromResources);
        Assert.True(report.IsOverallOk, string.Join(" | ", report.Warnings));
    }

    /// <summary>
    /// Every image hash reachable from a page's <c>/XObject</c> resources —
    /// what an object-enumerating reader would find, as opposed to what the
    /// page paints.
    /// </summary>
    /// <summary>
    /// Every image object in the saved file, however it is reached — the object
    /// table, not the page resources. This is what a tool that walks xrefs
    /// sees, and therefore what "the image is out of the file" has to mean.
    /// </summary>
    static List<string> ImageObjectsInFile(string path)
    {
        var found = new List<string>();
        using var doc = PdfReader.Open(path, PdfDocumentOpenMode.Import);
        foreach (var o in doc.Internals.GetAllObjects())
        {
            if (o is PdfDictionary d && d.Elements.GetName("/Subtype") == "/Image")
            {
                found.Add(d.Internals.ObjectID.ToString());
            }
        }
        return found;
    }

    static HashSet<string> HashesInPageResources(string path)
    {
        var hashes = new HashSet<string>(StringComparer.Ordinal);
        using var doc = PdfReader.Open(path, PdfDocumentOpenMode.Import);
        for (int i = 0; i < doc.PageCount; i++)
        {
            foreach (var entry in ImageXObjectCollector.EnumerateImageEntries(doc.Pages[i].Resources))
            {
                hashes.Add(ImageXObjectCollector.ComputeStreamHash(entry.Dictionary));
            }
        }
        return hashes;
    }
}
