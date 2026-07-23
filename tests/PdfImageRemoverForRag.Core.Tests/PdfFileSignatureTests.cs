using PdfImageRemoverForRag.Core.Validation;
using Xunit;

namespace PdfImageRemoverForRag.Core.Tests;

/// <summary>
/// The gate that stops a file whose only claim to being a PDF is its name.
/// </summary>
public class PdfFileSignatureTests : IDisposable
{
    readonly string _folder = Directory.CreateTempSubdirectory("pdf-signature-tests").FullName;

    string WriteFile(string name, byte[] content)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllBytes(path, content);
        return path;
    }

    static byte[] Padded(string header) =>
        System.Text.Encoding.ASCII.GetBytes(header.PadRight(200, ' '));

    [Fact]
    public void AcceptsAFileBeginningWithThePdfHeader()
    {
        Assert.True(PdfFileSignature.LooksLikePdf(
            WriteFile("real.pdf", Padded("%PDF-1.7\n1 0 obj\n"))));
    }

    [Fact]
    public void AcceptsAHeaderPrecededByJunk()
    {
        // The spec allows leading bytes before the header and real readers scan
        // for it, so a file that would open elsewhere must open here too.
        Assert.True(PdfFileSignature.LooksLikePdf(
            WriteFile("offset.pdf", Padded("garbage bytes first\n%PDF-1.4\n"))));
    }

    [Fact]
    public void RejectsAFileRenamedToPdf()
    {
        // The case that motivated the check: an executable with the right
        // extension used to go straight into the parser.
        Assert.False(PdfFileSignature.LooksLikePdf(
            WriteFile("fake.pdf", Padded("MZ\0this is a PE image"))));
    }

    [Fact]
    public void RejectsAHeaderTooDeepToBeReal()
    {
        var content = new byte[4096];
        System.Text.Encoding.ASCII.GetBytes("%PDF-1.7").CopyTo(content, 3000);
        Assert.False(PdfFileSignature.LooksLikePdf(WriteFile("deep.pdf", content)));
    }

    [Fact]
    public void RejectsEmptyAndTruncatedFiles()
    {
        Assert.False(PdfFileSignature.LooksLikePdf(WriteFile("empty.pdf", Array.Empty<byte>())));
        Assert.False(PdfFileSignature.LooksLikePdf(
            WriteFile("stub.pdf", System.Text.Encoding.ASCII.GetBytes("%PDF-1.7"))));
    }

    [Fact]
    public void RejectsAMissingFileInsteadOfThrowing()
    {
        // The file can vanish between the dialog and the open; that is a "no",
        // not a crash.
        Assert.False(PdfFileSignature.LooksLikePdf(Path.Combine(_folder, "gone.pdf")));
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try { Directory.Delete(_folder, recursive: true); }
        catch (IOException) { }
    }
}
