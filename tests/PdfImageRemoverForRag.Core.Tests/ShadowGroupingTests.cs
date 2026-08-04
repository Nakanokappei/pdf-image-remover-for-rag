using PdfImageRemoverForRag.Core.Grouping;
using PdfImageRemoverForRag.Core.Models;
using Xunit;

namespace PdfImageRemoverForRag.Core.Tests;

// Grouping behavior specific to shadows (RemovableKind.Shadow): the flat
// coloured layer a drop shadow becomes when it is exported to PDF. A shadow is
// an Image XObject, so it groups by stream hash exactly as an image does; what
// it must not do is share an image's id sequence, because the whole point of
// the kind is that a user can pick the shadows out of a list of pictures.
public class ShadowGroupingTests
{
    static ImageGroupBuilder NewBuilder() => Discoveries.NewBuilder();

    [Fact]
    public void TheSameShadowOnTwoPages_IsOneObjectWithTwoPlacements()
    {
        var groups = NewBuilder().Build(new[]
        {
            Discoveries.Shadow("HASH_SHADOW", usage: 2),
        });

        var shadow = Assert.Single(groups);
        Assert.Equal(RemovableKind.Shadow, shadow.Kind);
        Assert.Equal("SHD_001", shadow.GroupId);
        Assert.Equal(2, shadow.UsageCount);
    }

    [Fact]
    public void ShadowsSortAfterEveryOtherKind_AndGetTheirOwnIds()
    {
        // The enum's order is the display order and a shadow is the newest
        // kind, so it comes last. Its ids run apart from the images' — a
        // document where every other row is a shadow is exactly the document
        // this kind was added for.
        var groups = NewBuilder().Build(new[]
        {
            Discoveries.Shadow("HASH_SHADOW"),
            Discoveries.Drawing("HASH_ICON", page: 1),
            Discoveries.Shape("border"),
            Discoveries.Text("CONFIDENTIAL"),
            Discoveries.Image("HASH_LOGO"),
        });

        Assert.Equal(
            new[]
            {
                RemovableKind.Image, RemovableKind.Text, RemovableKind.Shape,
                RemovableKind.Drawing, RemovableKind.Shadow,
            },
            groups.Select(g => g.Kind).ToArray());
        Assert.Equal(
            new[] { "IMG_001", "TXT_001", "SHP_001", "DRW_001", "SHD_001" },
            groups.Select(g => g.GroupId).ToArray());
    }

    [Fact]
    public void AShadow_IsMatchedByItsStreamHash()
    {
        // What the cleaner is handed. A shadow has no shown string and no path
        // signature, so a kind missing from the stream-hash list would hand the
        // cleaner an empty key and remove nothing at all — silently, since the
        // save itself would still succeed.
        var groups = CrossFileImageGroupBuilder.Build(new[]
        {
            ("a.pdf", (IReadOnlyList<PdfImageGroup>)NewBuilder()
                .Build(new[] { Discoveries.Shadow("HASH_SHADOW") }).ToArray()),
        });

        var shadow = Assert.Single(groups);
        Assert.Equal("SHD_001", shadow.GroupId);
        Assert.Equal("HASH_SHADOW", shadow.MatchKey);
    }

    [Fact]
    public void AShadow_IsDrawnByAnImageXObject()
    {
        // The other half of the same agreement: the cleaner looks for a shadow
        // among the page's image entries, never among its forms. Reading it off
        // the kind keeps that decision in one place.
        Assert.True(RemovableKind.Shadow.IsIdentifiedByStreamHash());
        Assert.True(RemovableKind.Shadow.IsImageXObject());
        Assert.False(RemovableKind.Drawing.IsImageXObject());
    }
}
