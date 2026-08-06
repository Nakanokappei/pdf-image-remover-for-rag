using PdfImageRemoverForRag.Core.Abstractions;
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
            new[] { new ImageRemovalSelection("IMG_999", Array.Empty<PdfImageOccurrence>()) },
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
            new[] { new ImageRemovalSelection("IMG_999", Array.Empty<PdfImageOccurrence>()) },
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
            new[] { new ImageRemovalSelection("IMG_999", Array.Empty<PdfImageOccurrence>()) });

        Assert.Empty(resampler.Asked);
        Assert.Contains((800, 1100), ImageSizesIn(destination));
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
            new[] { new ImageRemovalSelection("IMG_999", Array.Empty<PdfImageOccurrence>()) },
            regionsToFlatten: null, fitImagesToScreen: true);

        Assert.Empty(result.ResizedImageHashes!);
        Assert.Contains((800, 1100), ImageSizesIn(destination));
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
            new[] { new ImageRemovalSelection("IMG_999", Array.Empty<PdfImageOccurrence>()) },
            regionsToFlatten: null, fitImagesToScreen: true);

        Assert.NotNull(result.ResizedImageHashes);
        Assert.Single(result.ResizedImageHashes!);
    }
}
