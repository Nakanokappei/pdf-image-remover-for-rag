using PdfImageRemoverForRag.Core.Models;

namespace PdfImageRemoverForRag.Core.Grouping;

/// <summary>
/// Finds the places on a page where drawn objects of different kinds overlap,
/// so they can be flattened into one raster image.
///
/// Why only mixed kinds: flattening exists to get text out of the text layer
/// where it sits on top of a picture or a drawing — a chart's axis labels, a
/// caption over a photo, a stamp over a rule. Two texts overlapping each other
/// are still ordinary text and there is nothing to gain by rasterizing them, so
/// a region is only reported when it contains at least two different
/// <see cref="RemovableKind"/>s. That yields exactly the four useful
/// combinations: image+text, image+shape, text+shape, and image+text+shape.
///
/// "Overlap" means the rectangles intersect at all — one containing another is
/// simply the case where the intersection equals the smaller rectangle.
/// Overlap is transitive here: if a label overlaps a bar and the bar overlaps
/// the axis rule, all three flatten together, because rasterizing part of a
/// drawing and leaving the rest vector would be visible.
/// </summary>
public static class OverlapDetector
{
    /// <summary>
    /// Smallest extent, in points, any object is given for the purposes of
    /// intersection. Rules and axis lines are drawn as paths of zero height
    /// (the analyzer reports "495x0 pt" for them), and a rectangle of zero
    /// height intersects nothing at all, so a hairline would never be found to
    /// overlap the label sitting on it. One point is about the thickness such a
    /// line actually paints.
    /// </summary>
    public const double MinimumExtent = 1.0;

    /// <summary>
    /// Group the page's objects into overlap regions. Objects that do not touch
    /// anything of another kind are not returned.
    /// </summary>
    public static IReadOnlyList<OverlapRegion> Detect(
        int pageNumber, IReadOnlyList<PlacedObject> objects)
    {
        if (objects.Count < 2) return Array.Empty<OverlapRegion>();

        // Union-find over the objects: every intersecting pair joins one set.
        // The page counts are small (tens to low hundreds), so the O(n^2) sweep
        // is cheaper than any structure that would need building.
        var parent = new int[objects.Count];
        for (int i = 0; i < parent.Length; i++) parent[i] = i;

        for (int i = 0; i < objects.Count; i++)
        {
            for (int j = i + 1; j < objects.Count; j++)
            {
                if (Intersects(objects[i], objects[j])) Union(parent, i, j);
            }
        }

        // Collect the members of each set, then keep only the mixed-kind ones.
        var sets = new Dictionary<int, List<PlacedObject>>();
        for (int i = 0; i < objects.Count; i++)
        {
            int root = Find(parent, i);
            if (!sets.TryGetValue(root, out var members))
            {
                members = new List<PlacedObject>();
                sets[root] = members;
            }
            members.Add(objects[i]);
        }

        var regions = new List<OverlapRegion>();
        foreach (var members in sets.Values)
        {
            if (members.Count < 2) continue;
            if (members.Select(m => m.Kind).Distinct().Count() < 2) continue;
            regions.Add(BuildRegion(pageNumber, members));
        }

        // Stable order: top-left first, reading order down the page (PDF Y grows
        // upward, so the largest Y is the top).
        return regions
            .OrderByDescending(r => r.Y + r.Height)
            .ThenBy(r => r.X)
            .ToArray();
    }

    /// <summary>
    /// True when a rectangle overlaps the region, under the same padding rule
    /// detection used. The cleaner asks this to decide which instances on a page
    /// belong to the region it is flattening: the same string may be shown
    /// elsewhere on the page and must survive.
    /// </summary>
    public static bool RegionOverlaps(
        OverlapRegion region, double x, double y, double width, double height)
    {
        var candidate = new PlacedObject(RemovableKind.Image, string.Empty, x, y, width, height);
        var whole = new PlacedObject(
            RemovableKind.Image, string.Empty, region.X, region.Y, region.Width, region.Height);
        return Intersects(candidate, whole);
    }

    /// <summary>The union of the members' rectangles, as the region to raster.</summary>
    static OverlapRegion BuildRegion(int pageNumber, List<PlacedObject> members)
    {
        double left = double.MaxValue, bottom = double.MaxValue;
        double right = double.MinValue, top = double.MinValue;
        foreach (var member in members)
        {
            var (l, b, r, t) = Padded(member);
            if (l < left) left = l;
            if (b < bottom) bottom = b;
            if (r > right) right = r;
            if (t > top) top = t;
        }
        // Members in a stable order so the region's signature does not depend on
        // the order the analyzer happened to walk the content stream in.
        var ordered = members
            .OrderBy(m => m.Kind)
            .ThenBy(m => m.Identity, StringComparer.Ordinal)
            .ThenBy(m => m.X)
            .ThenBy(m => m.Y)
            .ToArray();
        return new OverlapRegion(pageNumber, left, bottom, right - left, top - bottom, ordered);
    }

    /// <summary>
    /// True when the two objects' rectangles share any area. Touching edges do
    /// not count: a caption sitting exactly on a rule's edge is adjacent, not
    /// overlapping — but see <see cref="MinimumExtent"/>, which gives a
    /// zero-height rule enough thickness for a label drawn across it to count.
    /// </summary>
    static bool Intersects(PlacedObject a, PlacedObject b)
    {
        var (al, ab, ar, at) = Padded(a);
        var (bl, bb, br, bt) = Padded(b);
        return al < br && bl < ar && ab < bt && bb < at;
    }

    /// <summary>
    /// The object's edges, with any extent below <see cref="MinimumExtent"/>
    /// grown symmetrically to it. Negative width or height (a rectangle given
    /// by its far corner) is normalised here too.
    /// </summary>
    static (double Left, double Bottom, double Right, double Top) Padded(PlacedObject o)
    {
        double left = Math.Min(o.X, o.X + o.Width);
        double right = Math.Max(o.X, o.X + o.Width);
        double bottom = Math.Min(o.Y, o.Y + o.Height);
        double top = Math.Max(o.Y, o.Y + o.Height);

        if (right - left < MinimumExtent)
        {
            double centre = (left + right) / 2;
            left = centre - (MinimumExtent / 2);
            right = centre + (MinimumExtent / 2);
        }
        if (top - bottom < MinimumExtent)
        {
            double centre = (bottom + top) / 2;
            bottom = centre - (MinimumExtent / 2);
            top = centre + (MinimumExtent / 2);
        }
        return (left, bottom, right, top);
    }

    static int Find(int[] parent, int index)
    {
        while (parent[index] != index)
        {
            parent[index] = parent[parent[index]];   // path halving
            index = parent[index];
        }
        return index;
    }

    static void Union(int[] parent, int a, int b)
    {
        int rootA = Find(parent, a);
        int rootB = Find(parent, b);
        if (rootA != rootB) parent[rootB] = rootA;
    }
}
