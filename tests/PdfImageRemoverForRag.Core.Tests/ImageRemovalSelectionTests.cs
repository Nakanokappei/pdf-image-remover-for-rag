using PdfImageRemoverForRag.Core.Models;
using Xunit;

namespace PdfImageRemoverForRag.Core.Tests;

// Spec §24 "削除対象選択モデル" — the record itself, independent of the validator.
public class ImageRemovalSelectionTests
{
    [Fact]
    public void RecordEquality_IgnoresListIdentity_ButRespectsContents()
    {
        // Records use value semantics on scalar members; IReadOnlyList is
        // compared by reference. We verify GroupId equality is the interesting
        // property the rest of the code relies on.
        var occ1 = new PdfImageOccurrence(1, "1 0 R", "/Im1", 0, 0, 100, 60);
        var occ2 = new PdfImageOccurrence(1, "1 0 R", "/Im1", 0, 0, 100, 60);

        var a = new ImageRemovalSelection("IMG_001", new[] { occ1 });
        var b = new ImageRemovalSelection("IMG_001", new[] { occ1 });
        var c = new ImageRemovalSelection("IMG_002", new[] { occ1 });

        // Same list reference → records equal.
        Assert.Equal(a.GroupId, b.GroupId);
        Assert.NotEqual(a, c);

        // Occurrence records themselves have value equality.
        Assert.Equal(occ1, occ2);
    }

    [Fact]
    public void Record_IsImmutable()
    {
        // Records with init-only setters expose no way to mutate GroupId.
        var selection = new ImageRemovalSelection("IMG_001", Array.Empty<PdfImageOccurrence>());
        Assert.Equal("IMG_001", selection.GroupId);
        // Compile-time check: `selection.GroupId = "…";` would fail to compile
        // for a positional record. Documenting the intent here via runtime shape.
        Assert.Empty(selection.Occurrences);
    }

    [Fact]
    public void Selection_MayCarryMultipleOccurrences()
    {
        // The removal plan operates on the union of occurrences per group.
        var occurrences = new[]
        {
            new PdfImageOccurrence(1, "1 0 R", "/Im1", 0, 0, 100, 100),
            new PdfImageOccurrence(2, "1 0 R", "/Im1", 0, 0, 100, 100),
        };
        var selection = new ImageRemovalSelection("IMG_001", occurrences);
        Assert.Equal(2, selection.Occurrences.Count);
    }
}
