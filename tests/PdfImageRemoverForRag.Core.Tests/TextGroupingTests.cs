using PdfImageRemoverForRag.Core.Grouping;
using PdfImageRemoverForRag.Core.Models;
using Xunit;

namespace PdfImageRemoverForRag.Core.Tests;

// Grouping behavior specific to text objects (RemovableKind.Text): kind-aware
// IDs, image-before-text ordering, and cross-file text merging.
public class TextGroupingTests
{
    // The discovery factories live in Discoveries so a change to the
    // ObjectDiscovery record lands in one place.
    static ObjectDiscovery Image(string hash, int usage = 1) => Discoveries.Image(hash, usage);
    static ObjectDiscovery Text(string value, int usage = 2) => Discoveries.Text(value, usage);
    static ObjectGroupBuilder NewBuilder() => Discoveries.NewBuilder();

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
            ("a.pdf", (IReadOnlyList<ObjectGroup>)NewBuilder().Build(new[] { Text("CONFIDENTIAL", 2) })),
            ("b.pdf", NewBuilder().Build(new[] { Text("CONFIDENTIAL", 3) })),
        };
        var merged = CrossFileObjectGroupBuilder.Build(perFile);
        var group = Assert.Single(merged);
        Assert.Equal(RemovableKind.Text, group.Kind);
        Assert.Equal("CONFIDENTIAL", group.TextValue);
        Assert.Equal(5, group.UsageCount);
        Assert.Equal("TXT_001", group.GroupId);
    }
}
