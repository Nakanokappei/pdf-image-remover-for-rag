using PdfImageRemoverForRag.Core.Abstractions;
using PdfImageRemoverForRag.Core.Errors;
using PdfImageRemoverForRag.Core.Models;
using PdfImageRemoverForRag.Infrastructure;
using PdfImageRemoverForRag.Infrastructure.Internal;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Xunit;

namespace PdfImageRemoverForRag.Infrastructure.Tests;

// Images redrawn at the size the user asked them to be. Too many pixels is file
// size and nothing else, and file size is what stops a document reaching a RAG
// pipeline: the customer's upload limit is 15 MB. Too few and the pipeline
// misreads the page's own text, which nothing downstream reports.
public class ImageReductionTests : IClassFixture<SamplePdfFixture>
{
    readonly SamplePdfFixture _samples;

    /// <summary>
    /// The lowest rung of the list, and the descendant of the ceiling this app
    /// applied to everything before any of it became a setting. Most of the
    /// tests below were written against that, so they go on asking for it.
    /// </summary>
    static readonly ImageReduction ScreenLimit = new(
        Enabled: true, ImageSizeLimit.Screen, ImageReduction.DefaultJpegQuality);

    public ImageReductionTests(SamplePdfFixture samples)
    {
        _samples = samples;
    }

    /// <summary>
    /// Stands in for the operating system's imaging library, which the layer
    /// under test does not have. It answers with an image of exactly the size it
    /// was asked for — the contract the caller relies on — made of the first
    /// samples of the original, so the result compresses like a picture rather
    /// than like a block of nothing.
    /// </summary>
    sealed class SizeOnlyResampler : IImageResampler
    {
        public List<(int Width, int Height)> Asked { get; } = new();

        // Not what these tests are about; the picture is stored as it came.
        public byte[] ChooseStorageForm(byte[] renderedPng, int jpegQuality) => renderedPng;

        public StoredImage? Resize(StoredImage image, int width, int height, int jpegQuality)
        {
            Asked.Add((width, height));
            return image with
            {
                Width = width,
                Height = height,
                Data = image.Data[..(width * height * image.Components)],
                SoftMask = image.SoftMask is null
                    ? null
                    : new StoredMask(width, height, new byte[width * height]),
            };
        }
    }

    /// <summary>
    /// Answers with noise, which is the case where fewer pixels take MORE bytes
    /// than the picture they replace.
    /// </summary>
    sealed class NoiseResampler : IImageResampler
    {
        public byte[] ChooseStorageForm(byte[] renderedPng, int jpegQuality) => renderedPng;

        public StoredImage? Resize(StoredImage image, int width, int height, int jpegQuality)
        {
            var noise = new byte[width * height * image.Components];
            new Random(1).NextBytes(noise);
            return image with { Width = width, Height = height, Data = noise, SoftMask = null };
        }
    }

    /// <summary>Every image the file holds, by (width, height) in pixels.</summary>
    static List<(int Width, int Height)> ImageSizesIn(string path)
    {
        using var document = PdfReader.Open(path, PdfDocumentOpenMode.Import);
        var sizes = new List<(int, int)>();
        for (int i = 0; i < document.PageCount; i++)
        {
            foreach (var entry in ImageXObjectCollector.EnumerateImageEntries(document.Pages[i].Resources))
            {
                sizes.Add((
                    entry.Dictionary.Elements.GetInteger("/Width"),
                    entry.Dictionary.Elements.GetInteger("/Height")));
            }
        }
        return sizes;
    }

    [Fact]
    public async Task AnImageLargerThanTheResolutionAllowsIsRedrawnToFitIt()
    {
        // The scan is 800x1100 and the page allows fewer than that at 92 dpi,
        // so it is redrawn down to what the page allows. The ceiling is asked
        // for rather than written out here: it is a resolution against a page
        // size now, and a number copied into the assertion would only record
        // what this page happened to be.
        var resampler = new SizeOnlyResampler();
        var destination = Path.Combine(_samples.TempDirectory, "screen-fit.pdf");
        await new PdfSharpDocumentCleaner(resampler: resampler).CleanAsync(
            _samples.ScannedPagePath, destination,
            new[] { new ObjectRemovalSelection("IMG_999", Array.Empty<ObjectOccurrence>()) },
            regionsToFlatten: null, imageReduction: ScreenLimit, isFinalOutput: true);

        var (pageWidth, pageHeight) = PageSizeOf(_samples.ScannedPagePath);
        var ceiling = ScreenLimit.CeilingFor(pageWidth, pageHeight);

        var asked = Assert.Single(resampler.Asked);
        Assert.True(asked.Width <= ceiling.Width, $"{asked.Width} > {ceiling.Width}");
        Assert.True(asked.Height <= ceiling.Height, $"{asked.Height} > {ceiling.Height}");

        // One of the two edges is right up against the ceiling, or the image
        // was made smaller than it had to be.
        Assert.True(asked.Width == ceiling.Width || asked.Height == ceiling.Height);

        // And the shape survived.
        Assert.Equal(800.0 / 1100.0, asked.Width / (double)asked.Height, precision: 2);
        Assert.Contains(asked, ImageSizesIn(destination));
    }

    /// <summary>
    /// The first page's size in points, which is what a ceiling is measured
    /// against now that every limit on the list is a resolution.
    /// </summary>
    static (double Width, double Height) PageSizeOf(string path)
    {
        using var document = PdfReader.Open(path, PdfDocumentOpenMode.Import);
        return (document.Pages[0].Width.Point, document.Pages[0].Height.Point);
    }

    [Fact]
    public async Task AnImageThatAlreadyFitsIsLeftAlone()
    {
        // The logo is 240x80. Nothing about it costs the reader anything, and
        // re-encoding it could only lose detail.
        var resampler = new SizeOnlyResampler();
        var destination = Path.Combine(_samples.TempDirectory, "screen-fit-small.pdf");
        await new PdfSharpDocumentCleaner(resampler: resampler).CleanAsync(
            _samples.OneImagePath, destination,
            new[] { new ObjectRemovalSelection("IMG_999", Array.Empty<ObjectOccurrence>()) },
            regionsToFlatten: null, imageReduction: ScreenLimit, isFinalOutput: true);

        Assert.Empty(resampler.Asked);
        Assert.Contains((240, 80), ImageSizesIn(destination));
    }

    [Fact]
    public async Task NothingIsRedrawnUnlessTheCallerAsksForIt()
    {
        // The working copy a flatten writes is an intermediate: re-encoding its
        // pictures would only mean encoding them again on the next rebuild.
        var resampler = new SizeOnlyResampler();
        var destination = Path.Combine(_samples.TempDirectory, "screen-fit-off.pdf");
        await new PdfSharpDocumentCleaner(resampler: resampler).CleanAsync(
            _samples.ScannedPagePath, destination,
            new[] { new ObjectRemovalSelection("IMG_999", Array.Empty<ObjectOccurrence>()) });

        Assert.Empty(resampler.Asked);
        Assert.Contains((800, 1100), ImageSizesIn(destination));
    }

    [Fact]
    public async Task ARunThatOnlyFitsIsAccepted()
    {
        // A save that only flattened has nothing ticked and nothing hidden: the
        // flattening is already in the working copy it reads from, and the one
        // thing still owed to the file the user keeps is the fitting. Refusing
        // this run is what made the App copy the bytes instead — on a customer
        // document (2026-08-08) two 2513x1270 JPEGs reached the output
        // untouched, and the log said "copied the working copy".
        var resampler = new SizeOnlyResampler();
        var destination = Path.Combine(_samples.TempDirectory, "screen-fit-only.pdf");
        await new PdfSharpDocumentCleaner(resampler: resampler).CleanAsync(
            _samples.ScannedPagePath, destination,
            Array.Empty<ObjectRemovalSelection>(),
            regionsToFlatten: null, imageReduction: ScreenLimit, isFinalOutput: true);

        var asked = Assert.Single(resampler.Asked);
        Assert.Contains(asked, ImageSizesIn(destination));
    }

    [Fact]
    public async Task ARunWithNothingToDoAtAllIsStillRefused()
    {
        // For an intermediate, being asked to change nothing is a caller's
        // mistake and saying so is how it gets found. The file the user keeps is
        // the exception, and the test below is that one.
        var destination = Path.Combine(_samples.TempDirectory, "screen-fit-nothing.pdf");
        await Assert.ThrowsAsync<PdfCleanerException>(() =>
            new PdfSharpDocumentCleaner().CleanAsync(
                _samples.ScannedPagePath, destination,
                Array.Empty<ObjectRemovalSelection>()));
    }

    [Fact]
    public async Task AnImageThatWouldGrowIsLeftAsItWas()
    {
        // Fewer pixels are not always fewer bytes. When the redrawn picture
        // would take more room than the one it replaces there is nothing to
        // gain, and the original is the better of the two.
        var destination = Path.Combine(_samples.TempDirectory, "screen-fit-grew.pdf");
        var result = await new PdfSharpDocumentCleaner(resampler: new NoiseResampler()).CleanAsync(
            _samples.ScannedPagePath, destination,
            new[] { new ObjectRemovalSelection("IMG_999", Array.Empty<ObjectOccurrence>()) },
            regionsToFlatten: null, imageReduction: ScreenLimit, isFinalOutput: true);

        Assert.Empty(result.ResizedImageHashes!);
        Assert.Contains((800, 1100), ImageSizesIn(destination));
    }

    /// <summary>
    /// Answers with the same picture in half the bytes. A JPEG's data is the
    /// file itself, so the size-only fake above (which cuts to
    /// width × height × components) cannot stand in for one.
    /// </summary>
    sealed class HalfTheBytesResampler : IImageResampler
    {
        public List<(int Width, int Height)> Asked { get; } = new();

        public byte[] ChooseStorageForm(byte[] renderedPng, int jpegQuality) => renderedPng;

        public StoredImage? Resize(StoredImage image, int width, int height, int jpegQuality)
        {
            Asked.Add((width, height));
            return image with
            {
                Width = width,
                Height = height,
                Data = image.Data[..(image.Data.Length / 2)],
            };
        }
    }

    /// <summary>
    /// A one-page document holding one JPEG image, written at a quality this
    /// controls. Built by hand rather than taken from the samples because what
    /// is under test is the quality, and a sample carries whatever quality the
    /// generator happened to use.
    /// </summary>
    static PdfDocument DocumentWithJpeg(int quality, int width, int height)
    {
        var document = new PdfDocument();
        var page = document.AddPage();

        var image = new PdfDictionary(document);
        image.Elements.SetName("/Type", "/XObject");
        image.Elements.SetName("/Subtype", "/Image");
        image.Elements.SetInteger("/Width", width);
        image.Elements.SetInteger("/Height", height);
        image.Elements.SetInteger("/BitsPerComponent", 8);
        image.Elements.SetName("/ColorSpace", "/DeviceRGB");
        image.Elements.SetName("/Filter", "/DCTDecode");
        image.CreateStream(JpegBytesAtQuality(quality));
        document.Internals.AddObject(image);

        var xObjects = new PdfDictionary(document);
        xObjects.Elements["/Im1"] = image.Reference;
        page.Resources.Elements["/XObject"] = xObjects;
        return document;
    }

    /// <summary>
    /// A JPEG carrying the quantization table an encoder would write for that
    /// quality, which is where the quality is read from. Padded so halving it
    /// still leaves something to store.
    /// </summary>
    static byte[] JpegBytesAtQuality(int quality)
    {
        int[] standard =
        {
            16, 11, 10, 16, 24, 40, 51, 61,
            12, 12, 14, 19, 26, 58, 60, 55,
            14, 13, 16, 24, 40, 57, 69, 56,
            14, 17, 22, 29, 51, 87, 80, 62,
            18, 22, 37, 56, 68, 109, 103, 77,
            24, 35, 55, 64, 81, 104, 113, 92,
            49, 64, 78, 87, 103, 121, 120, 101,
            72, 92, 95, 98, 112, 100, 103, 99,
        };
        int scale = quality < 50 ? 5000 / quality : 200 - (quality * 2);

        var bytes = new List<byte> { 0xFF, 0xD8, 0xFF, 0xDB, 0x00, 0x43, 0x00 };
        foreach (int entry in standard)
        {
            bytes.Add((byte)Math.Clamp(((entry * scale) + 50) / 100, 1, 255));
        }
        bytes.AddRange(new byte[512]);
        bytes.AddRange(new byte[] { 0xFF, 0xD9 });
        return bytes.ToArray();
    }

    [Fact]
    public void AJpegAboveTheQualityCeilingIsWrittenAgainAtItsOwnSize()
    {
        // A manual full of screenshots exported at 95 is already inside the
        // screen, so nothing about its size would touch it — and the quality
        // alone is worth megabytes.
        using var document = DocumentWithJpeg(quality: 95, width: 240, height: 80);
        var resampler = new HalfTheBytesResampler();

        var resized = ImageReductionPass.Apply(
            document, resampler, ScreenLimit, CancellationToken.None);

        Assert.Equal((240, 80), Assert.Single(resampler.Asked));
        Assert.Single(resized);
    }

    [Fact]
    public void AJpegAtOrBelowTheCeilingIsNotWrittenAgain()
    {
        // Re-encoding it would cost detail and return nothing: the quality it
        // was saved at is already the one being asked for.
        using var document = DocumentWithJpeg(quality: 75, width: 240, height: 80);
        var resampler = new HalfTheBytesResampler();

        var resized = ImageReductionPass.Apply(
            document, resampler, ScreenLimit, CancellationToken.None);

        Assert.Empty(resampler.Asked);
        Assert.Empty(resized);
    }

    [Fact]
    public async Task TheHashesOfWhatWasRedrawnComeBack()
    {
        // A resized image is not the stream it was, so the caller has to know
        // not to look for the old bytes in the output — the save verifies that
        // retained images are still present, and they are, at a new size.
        var resampler = new SizeOnlyResampler();
        var destination = Path.Combine(_samples.TempDirectory, "screen-fit-hashes.pdf");
        var result = await new PdfSharpDocumentCleaner(resampler: resampler).CleanAsync(
            _samples.ScannedPagePath, destination,
            new[] { new ObjectRemovalSelection("IMG_999", Array.Empty<ObjectOccurrence>()) },
            regionsToFlatten: null, imageReduction: ScreenLimit, isFinalOutput: true);

        Assert.NotNull(result.ResizedImageHashes);
        Assert.Single(result.ResizedImageHashes!);
    }

    [Fact]
    public void NoCeilingOnOfferEverEnlargesAnImage()
    {
        // The clamp at 1 is the whole guarantee, and raising the ceilings from
        // one screen-shaped box to four resolutions is exactly the change that
        // could quietly lose it. Asked of every entry the list offers, because
        // the logo (240x80) is under all of them.
        foreach (var limit in Enum.GetValues<ImageSizeLimit>())
        {
            using var document = PdfReader.Open(_samples.OneImagePath, PdfDocumentOpenMode.Modify);
            var resampler = new SizeOnlyResampler();

            var resized = ImageReductionPass.Apply(
                document,
                resampler,
                new ImageReduction(true, limit, ImageReduction.DefaultJpegQuality),
                CancellationToken.None);

            Assert.Empty(resampler.Asked);
            Assert.Empty(resized);
        }
    }

    [Fact]
    public void AHighEnoughCeilingLeavesAnImageAlone()
    {
        // The scan is 800x1100 on an A4 page. At 300 dpi that page allows
        // 2480x3508, so the image is already inside it and nothing is asked of
        // the resampler at all — where the lowest rung, 92 dpi, cuts it down.
        using var document = PdfReader.Open(_samples.ScannedPagePath, PdfDocumentOpenMode.Modify);
        var resampler = new SizeOnlyResampler();

        var resized = ImageReductionPass.Apply(
            document,
            resampler,
            new ImageReduction(true, ImageSizeLimit.RagFinePrint, ImageReduction.DefaultJpegQuality),
            CancellationToken.None);

        Assert.Empty(resampler.Asked);
        Assert.Empty(resized);
    }

    [Fact]
    public async Task ReductionSwitchedOffLeavesEveryImageAsItWas()
    {
        var resampler = new SizeOnlyResampler();
        var destination = Path.Combine(_samples.TempDirectory, "reduction-off.pdf");
        await new PdfSharpDocumentCleaner(resampler: resampler).CleanAsync(
            _samples.ScannedPagePath, destination,
            new[] { new ObjectRemovalSelection("IMG_999", Array.Empty<ObjectOccurrence>()) },
            imageReduction: ImageReduction.Off, isFinalOutput: true);

        Assert.Empty(resampler.Asked);
        Assert.Contains((800, 1100), ImageSizesIn(destination));
    }

    [Fact]
    public async Task ASaveWithNothingToRemoveAndNoReductionStillWritesTheFile()
    {
        // A run that only flattened arrives here with nothing selected, nothing
        // to flatten again and nothing to reduce — the flattening is already in
        // the working copy this reads from. It used to be refused, and was only
        // ever let through because the reduction was always asked for; making
        // the reduction the user's to switch off turned that into a save that
        // failed with "Nothing was given to remove".
        var destination = Path.Combine(_samples.TempDirectory, "nothing-to-remove.pdf");

        await new PdfSharpDocumentCleaner().CleanAsync(
            _samples.OneImagePath, destination,
            Array.Empty<ObjectRemovalSelection>(),
            imageReduction: ImageReduction.Off, isFinalOutput: true);

        Assert.True(File.Exists(destination));
        Assert.Contains((240, 80), ImageSizesIn(destination));
    }
}
