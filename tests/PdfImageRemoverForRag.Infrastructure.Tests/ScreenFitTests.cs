using PdfImageRemoverForRag.Core.Abstractions;
using PdfImageRemoverForRag.Core.Errors;
using PdfImageRemoverForRag.Core.Models;
using PdfImageRemoverForRag.Infrastructure;
using PdfImageRemoverForRag.Infrastructure.Internal;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Xunit;

namespace PdfImageRemoverForRag.Infrastructure.Tests;

// Images redrawn at the size they will be looked at. A picture wider than the
// reader's screen is file size and nothing else, and file size is what stops a
// document reaching a RAG pipeline: the customer's upload limit is 15 MB.
public class ScreenFitTests : IClassFixture<SamplePdfFixture>
{
    readonly SamplePdfFixture _samples;

    public ScreenFitTests(SamplePdfFixture samples)
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
    public async Task AnImageTallerThanTheScreenIsRedrawnToFitIt()
    {
        // The scan is 800x1100: inside 1920 across, past 1080 down. So the
        // height decides it, and the width follows to keep the shape.
        var resampler = new SizeOnlyResampler();
        var destination = Path.Combine(_samples.TempDirectory, "screen-fit.pdf");
        await new PdfSharpDocumentCleaner(resampler: resampler).CleanAsync(
            _samples.ScannedPagePath, destination,
            new[] { new ObjectRemovalSelection("IMG_999", Array.Empty<ObjectOccurrence>()) },
            regionsToFlatten: null, fitImagesToScreen: true);

        var asked = Assert.Single(resampler.Asked);
        Assert.Equal(1080, asked.Height);
        Assert.Equal(785, asked.Width);            // 800 * 1080/1100, rounded
        Assert.Contains((785, 1080), ImageSizesIn(destination));
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
            regionsToFlatten: null, fitImagesToScreen: true);

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
            regionsToFlatten: null, fitImagesToScreen: true);

        Assert.Single(resampler.Asked);
        Assert.Contains((785, 1080), ImageSizesIn(destination));
    }

    [Fact]
    public async Task ARunWithNothingToDoAtAllIsStillRefused()
    {
        // Fitting is the only thing that makes an empty run meaningful. Without
        // it, being asked to change nothing is a caller's mistake and saying so
        // is how it gets found.
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
            regionsToFlatten: null, fitImagesToScreen: true);

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

        var resized = ScreenFitPass.Apply(document, resampler, 1920, 1080, 85, CancellationToken.None);

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

        var resized = ScreenFitPass.Apply(document, resampler, 1920, 1080, 85, CancellationToken.None);

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
            regionsToFlatten: null, fitImagesToScreen: true);

        Assert.NotNull(result.ResizedImageHashes);
        Assert.Single(result.ResizedImageHashes!);
    }
}
