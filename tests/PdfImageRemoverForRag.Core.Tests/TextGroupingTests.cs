using PdfImageRemoverForRag.Core.Grouping;
using PdfImageRemoverForRag.Core.Models;
using Xunit;

namespace PdfImageRemoverForRag.Core.Tests;

// Grouping behavior specific to text objects (RemovableKind.Text): kind-aware
// IDs, image-before-text ordering, and cross-file text merging.
public class TextGroupingTests
{
    static ImageDiscovery Image(string hash, int usage = 1)
    {
        var occ = Enumerable.Range(1, usage)
            .Select(p => new PdfImageOccurrence(p, "1 0 R", "/Im1", 0, 0, 100, 60)).ToArray();
        return new ImageDiscovery("1 0 R", hash, 100, 60, "/DeviceRGB", 8, "/FlateDecode",
            1000, false, true, null, null, occ);
    }

    static ImageDiscovery Text(string value, int usage = 2)
    {
        var occ = Enumerable.Range(1, usage)
            .Select(p => new PdfImageOccurrence(p, "", "", 0, 0, 0, 0)).ToArray();
        return new ImageDiscovery("", "TEXT:" + value, 0, 0, "Text", 0, "Text",
            value.Length, false, true, null, null, occ,
            RemovableKind.Text, value);
    }

    static ImageGroupBuilder NewBuilder() =>
        new(new FullPageImageDetector(new[]
        {
            new PageDimensions(1, 595, 842),
            new PageDimensions(2, 595, 842),
            new PageDimensions(3, 595, 842),
        }));

    [Fact]
    public void TextDiscoveries_GroupByValue_AndCarryKindAndValue()
    {
        var groups = NewBuilder().Build(new[] { Text("CONFIDENTIAL", usage: 3) });
        var group = Assert.Single(groups);
        Assert.Equal(RemovableKind.Text, group.Kind);
        Assert.Equal("CONFIDENTIAL", group.TextValue);
        Assert.Equal(3, group.UsageCount);
    }

    [Fact]
    public void ImagesSortBeforeText_AndGetKindSpecificIds()
    {
        var groups = NewBuilder().Build(new[]
        {
            Text("FOOTER", usage: 3),
            Image("HASH_LOGO", usage: 2),
        });

        Assert.Equal(2, groups.Count);
        Assert.Equal(RemovableKind.Image, groups[0].Kind);
        Assert.Equal("IMG_001", groups[0].GroupId);
        Assert.Equal(RemovableKind.Text, groups[1].Kind);
        Assert.Equal("TXT_001", groups[1].GroupId);
    }

    [Fact]
    public void CrossFile_MergesSameTextAcrossFiles()
    {
        var perFile = new[]
        {
            ("a.pdf", (IReadOnlyList<PdfImageGroup>)NewBuilder().Build(new[] { Text("CONFIDENTIAL", 2) })),
            ("b.pdf", NewBuilder().Build(new[] { Text("CONFIDENTIAL", 3) })),
        };
        var merged = CrossFileImageGroupBuilder.Build(perFile);
        var group = Assert.Single(merged);
        Assert.Equal(RemovableKind.Text, group.Kind);
        Assert.Equal("CONFIDENTIAL", group.TextValue);
        Assert.Equal(5, group.UsageCount);
        Assert.Equal("TXT_001", group.GroupId);
    }
}
