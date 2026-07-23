using PdfImageRemoverForRag.Infrastructure;
using Xunit;

namespace PdfImageRemoverForRag.Infrastructure.Tests;

public class PdfPigThumbnailProviderTests : IClassFixture<SamplePdfFixture>
{
    readonly SamplePdfFixture _samples;

    public PdfPigThumbnailProviderTests(SamplePdfFixture samples)
    {
        _samples = samples;
    }

    [Fact]
    public async Task ExtractThumbnails_ReturnsAtLeastOneEntry()
    {
        var provider = new PdfPigThumbnailProvider();
        var dict = await provider.ExtractThumbnailsAsync(_samples.MultipleImagesPath, 160, 120);
        Assert.NotEmpty(dict);
    }

    [Fact]
    public async Task EveryReturnedEntry_IsPngOrJpeg()
    {
        // The provider yields PNG (Flate conversions) or raw JPEG
        // (DCTDecode passthrough) — both decodable by standard image APIs.
        var provider = new PdfPigThumbnailProvider();
        var dict = await provider.ExtractThumbnailsAsync(_samples.MultipleImagesPath, 160, 120);
        Assert.All(dict.Values, bytes =>
        {
            Assert.True(bytes.Length >= 8);
            bool isPng = bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47;
            bool isJpeg = bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF;
            Assert.True(isPng || isJpeg, "thumbnail bytes are neither PNG nor JPEG");
        });
    }

    [Fact]
    public async Task JpegImage_GetsRawJpegPassthroughThumbnail()
    {
        // PdfPig's TryGetPng always fails for DCTDecode images (documented
        // behavior) — the "?" placeholder bug. The passthrough branch must
        // return the raw JPEG bytes instead of dropping the image.
        var provider = new PdfPigThumbnailProvider();
        var dict = await provider.ExtractThumbnailsAsync(_samples.JpegImagePath, 160, 120);
        var entry = Assert.Single(dict);
        Assert.True(entry.Value[0] == 0xFF && entry.Value[1] == 0xD8 && entry.Value[2] == 0xFF,
            "expected raw JPEG bytes for the DCTDecode image");
    }

    [Fact]
    public async Task JpegImage_ThumbnailJoinsIntoAnalyzerOutput()
    {
        // End-to-end: the analyzer keys thumbnails by PDFsharp's stream hash;
        // the provider keys by PdfPig's RawBytes hash. This test proves the
        // two sides agree for DCTDecode streams too.
        var analyzer = new PdfSharpDocumentAnalyzer(new PdfPigThumbnailProvider());
        var info = await analyzer.AnalyzeAsync(_samples.JpegImagePath);
        var group = Assert.Single(info.ImageGroups);
        Assert.NotNull(group.ThumbnailBytes);
        Assert.Equal("/DCTDecode", group.Compression);
    }

    [Fact]
    public async Task NonexistentFile_ReturnsEmptyDictionary()
    {
        // Per spec §12 the provider absorbs failures — the caller sees an
        // empty dictionary rather than an exception.
        var provider = new PdfPigThumbnailProvider();
        var missing = Path.Combine(_samples.TempDirectory, "no-such-file.pdf");
        var dict = await provider.ExtractThumbnailsAsync(missing, 160, 120);
        Assert.Empty(dict);
    }
}
