using PdfImageRemoverForRag.Scripts.GenerateSamples;

namespace PdfImageRemoverForRag.Infrastructure.Tests;

/// <summary>
/// xUnit class fixture that generates the sample PDFs into a
/// fresh temp directory once per test class. Generation is delegated to
/// <see cref="SamplePdfWriter"/> — the same code the GenerateSamples console
/// uses — so the fixtures and the manually generated samples/ directory can
/// never drift apart.
/// </summary>
public sealed class SamplePdfFixture : IDisposable
{
    public string TempDirectory { get; }
    public string OneImagePath => Path.Combine(TempDirectory, "one-image.pdf");
    public string RepeatedLogoPath => Path.Combine(TempDirectory, "repeated-logo.pdf");
    public string MultipleImagesPath => Path.Combine(TempDirectory, "multiple-images.pdf");
    public string ImageAndTextPath => Path.Combine(TempDirectory, "image-and-text.pdf");
    public string ScannedPagePath => Path.Combine(TempDirectory, "scanned-page.pdf");
    public string JpegImagePath => Path.Combine(TempDirectory, "jpeg-image.pdf");
    public string RepeatedTextPath => Path.Combine(TempDirectory, "repeated-text.pdf");
    public string RepeatedShapesPath => Path.Combine(TempDirectory, "repeated-shapes.pdf");
    public string FormEmbeddedImagePath => Path.Combine(TempDirectory, "form-embedded-image.pdf");

    public SamplePdfFixture()
    {
        // Fresh temp dir per fixture instance so parallel test runs are isolated.
        TempDirectory = Path.Combine(Path.GetTempPath(),
            "PdfImageRemoverForRag.Tests." + Guid.NewGuid().ToString("N"));
        SamplePdfWriter.WriteAll(TempDirectory);
    }

    public void Dispose()
    {
        try { Directory.Delete(TempDirectory, recursive: true); }
        catch { /* best-effort cleanup */ }
    }
}
