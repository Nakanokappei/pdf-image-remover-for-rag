using PdfImageRemoverForRag.Core.Grouping;
using PdfImageRemoverForRag.Core.Models;
using Xunit;

namespace PdfImageRemoverForRag.Core.Tests;

// Cross-file grouping: the same image (same stream hash) opened in several
// PDFs must collapse into one selectable row.
public class CrossFileImageGroupBuilderTests
{
    static PdfImageGroup MakeGroup(string hash, int usageCount = 1,
        bool isSafelyRemovable = true, string? warningMessage = null,
        bool isPossibleFullPage = false, byte[]? thumbnailBytes = null)
    {
        var occurrences = Enumerable.Range(1, usageCount)
            .Select(page => new PdfImageOccurrence(page, "1 0 R", "/Im1", 0, 0, 100, 60))
            .ToArray();
        return new PdfImageGroup(
            GroupId: "IMG_001", Hash: hash,
            PixelWidth: 100, PixelHeight: 60,
            ColorSpace: "/DeviceRGB", BitsPerComponent: 8,
            Compression: "/FlateDecode", EstimatedSize: 1000,
            IsImageMask: false,
            IsPossibleFullPageImage: isPossibleFullPage,
            IsSafelyRemovable: isSafelyRemovable,
            WarningMessage: warningMessage,
            ThumbnailBytes: thumbnailBytes,
            Occurrences: occurrences);
    }

    [Fact]
    public void SameHashInTwoFiles_MergesIntoOneGroup_AndSumsUsage()
    {
        var groups = CrossFileImageGroupBuilder.Build(new[]
        {
            ("a.pdf", (IReadOnlyList<PdfImageGroup>)new[] { MakeGroup("HASH_LOGO", usageCount: 5) }),
            ("b.pdf", new[] { MakeGroup("HASH_LOGO", usageCount: 3) }),
        });

        var merged = Assert.Single(groups);
        Assert.Equal("HASH_LOGO", merged.Hash);
        Assert.Equal(8, merged.UsageCount);
        Assert.Equal(2, merged.FileCount);
        Assert.Equal(new[] { "a.pdf", "b.pdf" },
            merged.FileOccurrences.Select(f => f.FilePath).ToArray());
    }

    [Fact]
    public void DifferentHashes_StaySeparate()
    {
        var groups = CrossFileImageGroupBuilder.Build(new[]
        {
            ("a.pdf", (IReadOnlyList<PdfImageGroup>)new[] { MakeGroup("HASH_A") }),
            ("b.pdf", new[] { MakeGroup("HASH_B") }),
        });

        Assert.Equal(2, groups.Count);
        Assert.Distinct(groups.Select(g => g.Hash));
    }

    [Fact]
    public void UnsafeInOneFile_MakesWholeCrossFileGroupUnsafe()
    {
        // Same rationale as the single-file AND rule (§14.3): one checkbox
        // must never remove only the "safe half" of an image's placements.
        var groups = CrossFileImageGroupBuilder.Build(new[]
        {
            ("a.pdf", (IReadOnlyList<PdfImageGroup>)new[] { MakeGroup("HASH_X", isSafelyRemovable: true) }),
            ("b.pdf", new[] { MakeGroup("HASH_X", isSafelyRemovable: false, warningMessage: "unsafe in b") }),
        });

        var merged = Assert.Single(groups);
        Assert.False(merged.IsSafelyRemovable);
        Assert.Equal("unsafe in b", merged.WarningMessage);
    }

    [Fact]
    public void FullPageInAnyFile_FlagsTheMergedGroup()
    {
        var groups = CrossFileImageGroupBuilder.Build(new[]
        {
            ("a.pdf", (IReadOnlyList<PdfImageGroup>)new[] { MakeGroup("HASH_X", isPossibleFullPage: false) }),
            ("b.pdf", new[] { MakeGroup("HASH_X", isPossibleFullPage: true) }),
        });

        Assert.True(Assert.Single(groups).IsPossibleFullPageImage);
    }

    [Fact]
    public void Thumbnail_ComesFromFirstFileThatHasOne()
    {
        var png = new byte[] { 1, 2, 3 };
        var groups = CrossFileImageGroupBuilder.Build(new[]
        {
            ("a.pdf", (IReadOnlyList<PdfImageGroup>)new[] { MakeGroup("HASH_X", thumbnailBytes: null) }),
            ("b.pdf", new[] { MakeGroup("HASH_X", thumbnailBytes: png) }),
        });

        Assert.Equal(png, Assert.Single(groups).ThumbnailBytes);
    }

    [Fact]
    public void GroupsSortedByTotalUsage_GetSequentialIds()
    {
        var groups = CrossFileImageGroupBuilder.Build(new[]
        {
            ("a.pdf", (IReadOnlyList<PdfImageGroup>)new[]
            {
                MakeGroup("HASH_RARE", usageCount: 1),
                MakeGroup("HASH_COMMON", usageCount: 2),
            }),
            ("b.pdf", new[] { MakeGroup("HASH_COMMON", usageCount: 4) }),
        });

        Assert.Equal("IMG_001", groups[0].GroupId);
        Assert.Equal("HASH_COMMON", groups[0].Hash);
        Assert.Equal(6, groups[0].UsageCount);
        Assert.Equal("IMG_002", groups[1].GroupId);
    }
}
