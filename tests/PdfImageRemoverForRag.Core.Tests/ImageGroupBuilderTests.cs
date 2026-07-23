using PdfImageRemoverForRag.Core.Grouping;
using PdfImageRemoverForRag.Core.Models;
using Xunit;

namespace PdfImageRemoverForRag.Core.Tests;

// Spec §24: "同一画像のグループ化", "使用ページ一覧生成", "ページ全体画像候補判定".
public class ImageGroupBuilderTests
{
    static ImageDiscovery MakeDiscovery(string objectId, string streamHash,
        int width = 100, int height = 60, bool isSafelyRemovable = true, string? unsafeReason = null,
        params PdfImageOccurrence[] occurrences)
    {
        return new ImageDiscovery(
            ObjectId: objectId,
            StreamHash: streamHash,
            PixelWidth: width,
            PixelHeight: height,
            ColorSpace: "/DeviceRGB",
            BitsPerComponent: 8,
            Compression: "/FlateDecode",
            StreamByteCount: 1024,
            IsImageMask: false,
            IsSafelyRemovable: isSafelyRemovable,
            UnsafeReason: unsafeReason,
            ThumbnailBytes: null,
            Occurrences: occurrences);
    }

    static PdfImageOccurrence MakeOccurrence(int page, double w = 100, double h = 60,
        double x = 0, double y = 0, string name = "/Im1", string objectId = "1 0 R")
    {
        return new PdfImageOccurrence(page, objectId, name, x, y, w, h);
    }

    static FullPageImageDetector Detector(params (int page, double w, double h)[] pages) =>
        new(pages.Select(p => new PageDimensions(p.page, p.w, p.h)));

    [Fact]
    public void SameStreamHash_CollapsesIntoOneGroup()
    {
        // Two Image XObjects with identical bytes must appear as one group and
        // their occurrences must be unioned — this is the "logo on every page"
        // case that motivates grouping in the first place.
        var pages = Detector((1, 595, 842), (2, 595, 842));
        var builder = new ImageGroupBuilder(pages);

        var groups = builder.Build(new[]
        {
            MakeDiscovery("1 0 R", "HASH_A", occurrences: MakeOccurrence(page: 1)),
            MakeDiscovery("2 0 R", "HASH_A", occurrences: MakeOccurrence(page: 2)),
        });

        var single = Assert.Single(groups);
        Assert.Equal("HASH_A", single.Hash);
        Assert.Equal(2, single.UsageCount);
        Assert.Equal(new[] { 1, 2 }, single.UsagePages);
    }

    [Fact]
    public void DifferentStreamHashes_ProduceSeparateGroups()
    {
        var pages = Detector((1, 595, 842));
        var builder = new ImageGroupBuilder(pages);

        var groups = builder.Build(new[]
        {
            MakeDiscovery("1 0 R", "HASH_A", occurrences: MakeOccurrence(page: 1)),
            MakeDiscovery("2 0 R", "HASH_B", occurrences: MakeOccurrence(page: 1, name: "/Im2")),
        });

        Assert.Equal(2, groups.Count);
        Assert.Distinct(groups.Select(g => g.Hash));
    }

    [Fact]
    public void GroupsSortedByDescendingUsageCount_GetSequentialIds()
    {
        // Group with more usages must sort first and receive IMG_001.
        var pages = Detector((1, 595, 842), (2, 595, 842), (3, 595, 842));
        var builder = new ImageGroupBuilder(pages);

        var groups = builder.Build(new[]
        {
            MakeDiscovery("1 0 R", "HASH_ONE_USE", occurrences: MakeOccurrence(page: 1)),
            MakeDiscovery("2 0 R", "HASH_THREE_USES",
                occurrences: new[] { MakeOccurrence(page: 1), MakeOccurrence(page: 2), MakeOccurrence(page: 3) }),
        });

        Assert.Equal("IMG_001", groups[0].GroupId);
        Assert.Equal("HASH_THREE_USES", groups[0].Hash);
        Assert.Equal("IMG_002", groups[1].GroupId);
        Assert.Equal("HASH_ONE_USE", groups[1].Hash);
    }

    [Fact]
    public void FullPageImage_FlagsGroupAsPossibleFullPage()
    {
        // 90 %-in-both-dimensions rule — a 600x800 image on a 595x842 page.
        var pages = Detector((1, 595, 842));
        var builder = new ImageGroupBuilder(pages);

        var groups = builder.Build(new[]
        {
            MakeDiscovery("1 0 R", "HASH_SCAN", occurrences: MakeOccurrence(page: 1, w: 590, h: 830)),
        });

        Assert.True(groups[0].IsPossibleFullPageImage);
    }

    [Fact]
    public void SmallImage_IsNotFlaggedAsFullPage()
    {
        var pages = Detector((1, 595, 842));
        var builder = new ImageGroupBuilder(pages);

        var groups = builder.Build(new[]
        {
            MakeDiscovery("1 0 R", "HASH_LOGO", occurrences: MakeOccurrence(page: 1, w: 240, h: 80)),
        });

        Assert.False(groups[0].IsPossibleFullPageImage);
    }

    [Fact]
    public void AnyUnsafeDiscoveryInGroup_MakesWholeGroupUnsafe()
    {
        // Grouping "OR"s occurrences but "AND"s safety — if one placement is
        // inside a shared Form XObject, we cannot remove the group at all.
        var pages = Detector((1, 595, 842), (2, 595, 842));
        var builder = new ImageGroupBuilder(pages);

        var groups = builder.Build(new[]
        {
            MakeDiscovery("1 0 R", "HASH_SHARED",
                isSafelyRemovable: true, occurrences: MakeOccurrence(page: 1)),
            MakeDiscovery("2 0 R", "HASH_SHARED",
                isSafelyRemovable: false,
                unsafeReason: "複雑なPDF構造のため、この画像は安全に削除できません。",
                occurrences: MakeOccurrence(page: 2)),
        });

        var group = Assert.Single(groups);
        Assert.False(group.IsSafelyRemovable);
        Assert.Contains("削除できません", group.WarningMessage ?? string.Empty);
    }
}
