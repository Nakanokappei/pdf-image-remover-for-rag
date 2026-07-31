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
///
/// Transitivity is also what makes this easy to get wrong, and both corrections
/// so far have been of the same shape: ONE object that touches everything drags
/// the entire page into a single region. A stroked page border did it
/// (see <c>Intersects</c>) and a filled page background did it
/// (see <c>IsPageFurniture</c>). When a region turns out to be implausibly
/// large, look for the object that joined it, not at the pairs that look wrong.
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
    /// <param name="page">
    /// The page's number and size. The size is not decoration: a shape as big as
    /// the paper is the page's background, and without knowing how big the paper
    /// is there is no way to tell one from a drawing (see
    /// <see cref="IsPageFurniture"/>).
    /// </param>
    public static IReadOnlyList<OverlapRegion> Detect(
        PageDimensions page, IReadOnlyList<PlacedObject> objects)
    {
        if (objects.Count < 2) return Array.Empty<OverlapRegion>();

        // Union-find over the objects: every intersecting pair joins one set.
        // The page counts are small (tens to low hundreds), so the O(n^2) sweep
        // is cheaper than any structure that would need building.
        var parent = new int[objects.Count];
        for (int i = 0; i < parent.Length; i++) parent[i] = i;

        // Decided once per object rather than inside the O(n^2) sweep.
        var furniture = new bool[objects.Count];
        for (int i = 0; i < objects.Count; i++) furniture[i] = IsPageFurniture(objects[i], page);

        for (int i = 0; i < objects.Count; i++)
        {
            for (int j = i + 1; j < objects.Count; j++)
            {
                if (furniture[i] || furniture[j]) continue;
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
            regions.Add(BuildRegion(page.PageNumber, members));
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

    /// <summary>
    /// The region a given set of objects covers, without asking whether they
    /// overlap. Flattening needs this because the user chooses which of a
    /// region's objects to bake in: what gets rasterized, and what gets deleted,
    /// is the union of the checked ones — never the whole region as detected.
    /// </summary>
    public static OverlapRegion RegionCovering(
        int pageNumber, IReadOnlyList<PlacedObject> members)
    {
        if (members.Count == 0)
        {
            throw new ArgumentException(
                "A region needs at least one member to cover.", nameof(members));
        }
        return BuildRegion(pageNumber, members);
    }

    /// <summary>The union of the members' rectangles, as the region to raster.</summary>
    static OverlapRegion BuildRegion(int pageNumber, IReadOnlyList<PlacedObject> members)
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
    /// True when the two objects are close enough to belong to the same region.
    ///
    /// For anything that paints over what is behind it — an image, a string, a
    /// filled shape — sharing any area is enough: a heading on a shaded table
    /// band, a caption over a photo. Touching edges alone does not count, though
    /// <see cref="MinimumExtent"/> gives a zero-height rule enough thickness for
    /// a label drawn across it to register.
    ///
    /// A shape that only strokes its path is different. It hides nothing, so
    /// meeting it means nothing: a page border crosses every paragraph on the
    /// page, and treating that as an overlap made one region out of most of a
    /// document. Such a shape joins only when it lies entirely inside the other
    /// object — an arrow drawn on a photograph is part of the photograph, while
    /// a frame around a paragraph is furniture that happens to surround it.
    /// </summary>
    static bool Intersects(PlacedObject a, PlacedObject b)
    {
        if (!a.HidesWhatIsBehind) return IsInside(a, b);
        if (!b.HidesWhatIsBehind) return IsInside(b, a);

        var (al, ab, ar, at) = Padded(a);
        var (bl, bb, br, bt) = Padded(b);
        return al < br && bl < ar && ab < bt && bb < at;
    }

    /// <summary>
    /// True when the object is part of the page rather than something drawn on
    /// it: a shape covering essentially the whole sheet.
    ///
    /// A slide's background panel is such a shape, and it is a fill, so the rule
    /// above — anything that paints over what is behind it joins on any shared
    /// area — makes it touch every object on the page. On a real 29-page deck
    /// that turned each page into ONE region of 118 objects, which is not a
    /// place where a picture and some text overlap; it is the page. The
    /// stroke-only rule already handles the same disease in a page BORDER, and
    /// this is the filled form of it.
    ///
    /// Only shapes are treated this way. A full-page image with text over it —
    /// a scan, a full-bleed photograph on a slide — is exactly what flattening
    /// is for, and is left alone.
    ///
    /// Such a shape is not merely prevented from joining: it cannot be a member
    /// either, because a region needs two objects and nothing else will pull it
    /// in. That is the intent — rasterizing it would rasterize the whole page,
    /// and no text on that page would stay text.
    /// </summary>
    static bool IsPageFurniture(PlacedObject o, PageDimensions page)
    {
        if (o.Kind != RemovableKind.Shape) return false;
        if (page.WidthPoints <= 0 || page.HeightPoints <= 0) return false;

        // The same threshold, and the same reasoning, as a full-page image:
        // "covers the page" cannot mean exactly 100 % when a couple of points of
        // margin are normal.
        var (left, bottom, right, top) = Padded(o);
        return (right - left) / page.WidthPoints >= FullPageImageDetector.CoverageThreshold
            && (top - bottom) / page.HeightPoints >= FullPageImageDetector.CoverageThreshold;
    }

    /// <summary>True when <paramref name="inner"/>'s rectangle sits within <paramref name="outer"/>'s.</summary>
    static bool IsInside(PlacedObject inner, PlacedObject outer)
    {
        var (il, ib, ir, it) = Padded(inner);
        var (ol, ob, orr, ot) = Padded(outer);
        return il >= ol && ir <= orr && ib >= ob && it <= ot;
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
