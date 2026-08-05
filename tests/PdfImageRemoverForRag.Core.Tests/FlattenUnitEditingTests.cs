using PdfImageRemoverForRag.Core.Grouping;
using PdfImageRemoverForRag.Core.Models;
using Xunit;

namespace PdfImageRemoverForRag.Core.Tests;

// Merging and splitting flatten units by hand. Detection is right almost every
// time, and these are for the rest: a unit that took in one object too many,
// and two units that should have been one.
public class FlattenUnitEditingTests
{
    static readonly PageDimensions Page = new(1, 600, 800);
    static readonly PageDimensions OtherPage = new(2, 600, 800);

    static PlacedObject Object(string identity, double x, double y) =>
        new(RemovableKind.Text, identity, x, y, 40, 12);

    static OverlapRegion Unit(PageDimensions page, params PlacedObject[] members) =>
        OverlapDetector.RegionCovering(page, members);

    static string[] Identities(OverlapRegion unit) =>
        unit.Members.Select(m => m.Identity).OrderBy(i => i, StringComparer.Ordinal).ToArray();

    [Fact]
    public void MergingTakesTheCheckedObjectsIntoAUnitOfTheirOwn()
    {
        var a = Object("a", 10, 700);
        var b = Object("b", 100, 700);
        var c = Object("c", 10, 400);
        var units = new[] { Unit(Page, a, b), Unit(Page, c, Object("d", 100, 400)) };

        var merged = FlattenUnitEditing.Merge(units, new[] { a, c });

        Assert.Equal(3, merged.Count);
        // The checked pair is one unit now; what was left behind stays.
        Assert.Contains(merged, u => Identities(u).SequenceEqual(new[] { "a", "c" }));
        Assert.Contains(merged, u => Identities(u).SequenceEqual(new[] { "b" }));
        Assert.Contains(merged, u => Identities(u).SequenceEqual(new[] { "d" }));
    }

    [Fact]
    public void MergingEverythingInTwoUnits_LeavesOnlyTheMergedOne()
    {
        var a = Object("a", 10, 700);
        var b = Object("b", 100, 700);
        var c = Object("c", 10, 400);
        var units = new[] { Unit(Page, a, b), Unit(Page, c) };

        var merged = FlattenUnitEditing.Merge(units, new[] { a, b, c });

        var single = Assert.Single(merged);
        Assert.Equal(new[] { "a", "b", "c" }, Identities(single));
    }

    [Fact]
    public void SplittingSeparatesTheCheckedFromTheRest()
    {
        var caption = Object("caption", 10, 700);
        var figure = Object("figure", 12, 690);
        var stray = Object("stray", 300, 690);
        var units = new[] { Unit(Page, caption, figure, stray) };

        var split = FlattenUnitEditing.Split(units, new[] { stray });

        Assert.Equal(2, split.Count);
        Assert.Contains(split, u => Identities(u).SequenceEqual(new[] { "stray" }));
        Assert.Contains(split, u => Identities(u).SequenceEqual(new[] { "caption", "figure" }));
    }

    [Fact]
    public void AMergedUnitCoversEverythingItHolds()
    {
        var left = Object("left", 10, 700);
        var right = Object("right", 500, 400);
        var units = new[] { Unit(Page, left, Object("x", 20, 700)), Unit(Page, right) };

        var merged = FlattenUnitEditing.Merge(units, new[] { left, right });
        var unit = Assert.Single(merged, u => u.Members.Count == 2);

        // The rectangle is what gets rasterised, so it has to reach both.
        Assert.True(unit.X <= left.X && unit.Y <= right.Y);
        Assert.True(unit.X + unit.Width >= right.X + right.Width);
        Assert.True(unit.Y + unit.Height >= left.Y + left.Height);
    }

    [Fact]
    public void NothingIsMergedAcrossPages()
    {
        var here = Object("here", 10, 700);
        var there = Object("there", 10, 700);
        var units = new[] { Unit(Page, here, Object("x", 60, 700)), Unit(OtherPage, there) };

        Assert.False(FlattenUnitEditing.CanMerge(units, new[] { here, there }));
        Assert.Equal(units, FlattenUnitEditing.Merge(units, new[] { here, there }));
    }

    [Fact]
    public void MergingNeedsTwoUnits_AndSplittingNeedsSomethingToLeaveBehind()
    {
        var a = Object("a", 10, 700);
        var b = Object("b", 60, 700);
        var units = new[] { Unit(Page, a, b) };

        // Both objects are already in the one unit: nothing to gather.
        Assert.False(FlattenUnitEditing.CanMerge(units, new[] { a, b }));
        // And taking both out would leave an empty unit behind, which is not a
        // split — it is the same unit with a new name.
        Assert.False(FlattenUnitEditing.CanSplit(units, new[] { a, b }));
        Assert.True(FlattenUnitEditing.CanSplit(units, new[] { a }));
    }

    [Fact]
    public void TheUnitsComeBackInReadingOrder()
    {
        var lower = Object("lower", 10, 100);
        var upper = Object("upper", 10, 700);
        var units = new[] { Unit(Page, lower, Object("x", 60, 100)), Unit(Page, upper) };

        var merged = FlattenUnitEditing.Merge(units, new[] { lower, upper });

        // The merged unit reaches the top of the page, so it leads.
        Assert.Equal(2, merged.Count);
        Assert.Contains("upper", merged[0].Members.Select(m => m.Identity));
    }
}
