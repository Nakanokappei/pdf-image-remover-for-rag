using PdfImageRemoverForRag.Core.Formatting;
using Xunit;

namespace PdfImageRemoverForRag.Core.Tests;

// Spec §24 "保存ファイル名生成", "元ファイル上書き防止" and §15.
public class CleanedFileNamerTests
{
    [Fact]
    public void DefaultDestination_AppendsCleanedSuffix()
    {
        // Matches the spec example: manual.pdf → manual_cleaned.pdf.
        var src = Path.Combine("/tmp", "manual.pdf");
        var dst = CleanedFileNamer.BuildDefaultDestination(src);
        Assert.Equal(Path.Combine("/tmp", "manual_cleaned.pdf"), dst);
    }

    [Fact]
    public void DefaultDestination_PreservesExtensionCasing()
    {
        // Users on case-preserving filesystems expect MANUAL.PDF to stay .PDF.
        var src = Path.Combine("/tmp", "MANUAL.PDF");
        var dst = CleanedFileNamer.BuildDefaultDestination(src);
        Assert.Equal(Path.Combine("/tmp", "MANUAL_cleaned.PDF"), dst);
    }

    [Fact]
    public void DefaultDestination_KeepsSourceDirectory()
    {
        var src = Path.Combine("/some/nested/dir", "book.pdf");
        var dst = CleanedFileNamer.BuildDefaultDestination(src);
        Assert.Equal(Path.Combine("/some/nested/dir", "book_cleaned.pdf"), dst);
    }

    [Fact]
    public void WouldOverwriteSource_ReturnsTrue_WhenPathsResolveToSameFile()
    {
        // Full-path comparison catches "./manual.pdf" == "/pwd/manual.pdf".
        var cwd = Directory.GetCurrentDirectory();
        var relative = Path.Combine(".", "manual.pdf");
        var absolute = Path.Combine(cwd, "manual.pdf");
        Assert.True(CleanedFileNamer.WouldOverwriteSource(relative, absolute));
    }

    [Fact]
    public void WouldOverwriteSource_ReturnsFalse_ForDifferentFilenames()
    {
        var src = Path.Combine("/tmp", "manual.pdf");
        var dst = Path.Combine("/tmp", "manual_cleaned.pdf");
        Assert.False(CleanedFileNamer.WouldOverwriteSource(src, dst));
    }

    [Fact]
    public void WouldOverwriteSource_HandlesEmptyPaths_AsFalse()
    {
        // Defensive: empty strings mean "no path chosen yet" — not an overwrite.
        Assert.False(CleanedFileNamer.WouldOverwriteSource(string.Empty, "/tmp/x.pdf"));
        Assert.False(CleanedFileNamer.WouldOverwriteSource("/tmp/x.pdf", string.Empty));
    }

    [Fact]
    public void BuildDefaultDestination_ThrowsForEmptySource()
    {
        Assert.Throws<ArgumentException>(() => CleanedFileNamer.BuildDefaultDestination(""));
    }
}
