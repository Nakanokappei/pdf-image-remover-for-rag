using PdfImageRemoverForRag.Core.Models;

namespace PdfImageRemoverForRag.Core.Grouping;

/// <summary>
/// Merging and splitting flatten units by hand.
///
/// <see cref="OverlapDetector"/> decides units from geometry, and it is right
/// almost every time — but "almost" leaves a user stuck: a unit that grouped
/// one thing too many cannot be flattened without taking that thing with it,
/// and two units that should have been one have to be flattened separately,
/// which draws two pictures where one was wanted.
///
/// Both operations work on the CHECKED objects rather than on whole units, so
/// they are each other's opposite: merge gathers what is checked into a unit of
/// its own, split takes what is checked out of the unit it sits in. Whatever is
/// not checked stays where it was.
///
/// One page at a time. A unit is a rectangle to rasterise from one page, and
/// there is no such rectangle across two.
/// </summary>
public static class FlattenUnitEditing
{
    /// <summary>
    /// Whether the checked objects can be merged: they have to span at least
    /// two units, all on one page. Objects already alone in one unit are
    /// nothing to merge.
    /// </summary>
    public static bool CanMerge(
        IReadOnlyList<OverlapRegion> units, IReadOnlyCollection<PlacedObject> selection)
    {
        if (selection.Count < 2) return false;
        var holding = UnitsHolding(units, selection);
        return holding.Count >= 2 && holding.Select(u => u.PageNumber).Distinct().Count() == 1;
    }

    /// <summary>
    /// Whether the checked objects can be split off: they must sit in exactly
    /// one unit and leave something behind, or there is nothing to separate
    /// them from.
    /// </summary>
    public static bool CanSplit(
        IReadOnlyList<OverlapRegion> units, IReadOnlyCollection<PlacedObject> selection)
    {
        if (selection.Count == 0) return false;
        var holding = UnitsHolding(units, selection);
        if (holding.Count != 1) return false;

        var unit = holding[0];
        var checkedHere = unit.Members.Count(selection.Contains);
        return checkedHere > 0 && checkedHere < unit.Members.Count;
    }

    /// <summary>
    /// The units after merging: one new unit holding the checked objects, and
    /// the units they came from keeping whatever was left. A source unit with
    /// nothing left disappears — everything it held is in the new one.
    /// </summary>
    public static IReadOnlyList<OverlapRegion> Merge(
        IReadOnlyList<OverlapRegion> units, IReadOnlyCollection<PlacedObject> selection)
    {
        if (!CanMerge(units, selection)) return units;

        var sources = UnitsHolding(units, selection);
        var page = sources[0].Page;
        // In the order the units are listed, so the merged unit reads down the
        // page like everything else does.
        var merged = OverlapDetector.RegionCovering(
            page, sources.SelectMany(u => u.Members).Where(selection.Contains).ToArray());

        return Rebuild(units, sources, selection, extra: new[] { merged });
    }

    /// <summary>
    /// The units after splitting: the checked objects become a unit of their
    /// own and the rest of their unit stays as another.
    /// </summary>
    public static IReadOnlyList<OverlapRegion> Split(
        IReadOnlyList<OverlapRegion> units, IReadOnlyCollection<PlacedObject> selection)
    {
        if (!CanSplit(units, selection)) return units;

        var source = UnitsHolding(units, selection)[0];
        var taken = OverlapDetector.RegionCovering(
            source.Page, source.Members.Where(selection.Contains).ToArray());

        return Rebuild(units, new[] { source }, selection, extra: new[] { taken });
    }

    /// <summary>
    /// Put the list back together: the units that were not touched as they
    /// were, the touched ones minus what was taken from them, and the new ones.
    /// Reading order is applied at the end so a hand-made unit lands where the
    /// page says it should rather than at the bottom of the list.
    /// </summary>
    static IReadOnlyList<OverlapRegion> Rebuild(
        IReadOnlyList<OverlapRegion> units,
        IReadOnlyList<OverlapRegion> touched,
        IReadOnlyCollection<PlacedObject> selection,
        IReadOnlyList<OverlapRegion> extra)
    {
        var result = new List<OverlapRegion>(units.Count + extra.Count);
        foreach (var unit in units)
        {
            if (!touched.Contains(unit))
            {
                result.Add(unit);
                continue;
            }

            var left = unit.Members.Where(m => !selection.Contains(m)).ToArray();
            if (left.Length > 0) result.Add(OverlapDetector.RegionCovering(unit.Page, left));
        }

        result.AddRange(extra);
        return ReadingOrder.Sort(result);
    }

    /// <summary>The units holding at least one of the checked objects.</summary>
    static List<OverlapRegion> UnitsHolding(
        IReadOnlyList<OverlapRegion> units, IReadOnlyCollection<PlacedObject> selection) =>
        units.Where(u => u.Members.Any(selection.Contains)).ToList();
}
