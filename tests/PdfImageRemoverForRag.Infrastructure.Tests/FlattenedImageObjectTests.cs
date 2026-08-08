using PdfImageRemoverForRag.Core.Grouping;
using PdfImageRemoverForRag.Core.Models;
using PdfImageRemoverForRag.Infrastructure;
using PdfImageRemoverForRag.Infrastructure.Internal;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Xunit;

namespace PdfImageRemoverForRag.Infrastructure.Tests;

// What flattening leaves behind in the FILE, which is a different question from
// what it leaves on the page.
//
// Flattening replaces a place on the page with a picture of itself, and the
// draw calls inside it go. The objects behind them were staying in the file:
// nothing painted them any more, so re-analysis reported the page as clean and
// every test passed, while a reader that enumerates objects still handed the
// original image to whatever consumed the document. That is the same fault
// that forced a released build to be withdrawn on the removal side, and it
// reached a user as "the parts I flattened are still there".
public class FlattenedImageObjectTests : IClassFixture<SamplePdfFixture>
{
    readonly SamplePdfFixture _samples;

    public FlattenedImageObjectTests(SamplePdfFixture samples)
    {
        _samples = samples;
    }

    static PdfSharpDocumentAnalyzer NewAnalyzer() => new(new PdfPigThumbnailProvider());

    string Destination(string name) => Path.Combine(_samples.TempDirectory, name);

    /// <summary>
    /// Every image stream the file holds, whether or not anything draws it —
    /// the view an object-enumerating reader has.
    /// </summary>
    static List<string> ImageHashesInFile(string path)
    {
        var doc = PdfReader.Open(path, PdfDocumentOpenMode.Import);
        var hashes = new List<string>();
        foreach (var obj in doc.Internals.GetAllObjects())
        {
            if (obj is not PdfDictionary dict) continue;
            if (dict.Elements.GetName("/Subtype") != "/Image") continue;
            hashes.Add(ImageXObjectCollector.ComputeStreamHash(dict));
        }
        return hashes;
    }

    [Fact]
    public async Task AFlattenedImage_IsGoneFromTheFile_NotJustFromThePage()
    {
        var info = await NewAnalyzer().AnalyzeAsync(_samples.ImageAndTextPath);
        var region = Assert.Single(info.OverlapRegions);
        var flattenedHash = region.Members.First(m => m.Kind == RemovableKind.Image).Identity;
        Assert.Contains(flattenedHash, ImageHashesInFile(_samples.ImageAndTextPath));

        var dest = Destination("image-and-text_flattened_object.pdf");
        await new PdfSharpDocumentCleaner(new FlatColorRasterizer())
            .CleanAsync(_samples.ImageAndTextPath, dest, Array.Empty<ObjectRemovalSelection>(),
                new[] { region });

        // The picture that replaced it is there; the original is not, and
        // neither is its entry in the page's resources.
        var remaining = ImageHashesInFile(dest);
        Assert.DoesNotContain(flattenedHash, remaining);
        Assert.Single(remaining);

        using var saved = PdfReader.Open(dest, PdfDocumentOpenMode.Import);
        var names = ImageXObjectCollector.EnumerateImageEntries(saved.Pages[0].Resources)
            .Select(e => ImageXObjectCollector.ComputeStreamHash(e.Dictionary));
        Assert.DoesNotContain(flattenedHash, names);
    }

    [Fact]
    public async Task AnImageDrawnOnOtherPagesToo_SurvivesBeingFlattenedOnOne()
    {
        // The reason the objects were being left alone in the first place. One
        // logo, five pages: flattening it on page 1 must take it off page 1 and
        // leave the other four untouched, object included.
        var info = await NewAnalyzer().AnalyzeAsync(_samples.RepeatedLogoPath);
        var logo = Assert.Single(info.ObjectGroups, g => g.Kind == RemovableKind.Image);
        Assert.Equal(5, logo.UsageCount);

        var firstPlacement = logo.Occurrences.First(o => o.PageNumber == 1);
        var region = OverlapDetector.RegionCovering(
            new PageDimensions(1, 595, 842),
            new[]
            {
                new PlacedObject(
                    RemovableKind.Image, logo.Hash,
                    firstPlacement.X, firstPlacement.Y,
                    firstPlacement.Width, firstPlacement.Height),
            });

        var dest = Destination("repeated-logo_flattened_page1.pdf");
        await new PdfSharpDocumentCleaner(new FlatColorRasterizer())
            .CleanAsync(_samples.RepeatedLogoPath, dest, Array.Empty<ObjectRemovalSelection>(),
                new[] { region });

        Assert.Contains(logo.Hash, ImageHashesInFile(dest));

        var reanalyzed = await NewAnalyzer().AnalyzeAsync(dest);
        var afterLogo = Assert.Single(reanalyzed.ObjectGroups, g => g.Hash == logo.Hash);
        Assert.Equal(new[] { 2, 3, 4, 5 }, afterLogo.UsagePages);
    }
}
