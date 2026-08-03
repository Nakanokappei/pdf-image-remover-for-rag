using PdfImageRemoverForRag.Core.Models;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Content;

namespace PdfImageRemoverForRag.Infrastructure.Internal;

/// <summary>
/// Reads the vector artwork a Form XObject paints in its own content stream —
/// the objects behind <see cref="RemovableKind.Drawing"/>.
///
/// Nothing else in analysis looks here. A page's own stream is read for shapes
/// and text, and a form is entered only to collect the images inside it, so a
/// form that paints nothing but paths was invisible: a person silhouette and a
/// speech bubble in a real document never reached the object list.
///
/// Two coordinate spaces meet in this class and keeping them apart is the whole
/// job. A form's paths are written in the form's own space; its <c>/Matrix</c>
/// is part of its definition and always applies; the CTM at each <c>Do</c> is
/// the placement and differs per page. So this reads the form ONCE, through
/// <c>/Matrix</c> only, and leaves the placement to the caller — which is what
/// lets one form drawn on eleven pages be one object with eleven rectangles.
/// </summary>
internal static class FormDrawingReader
{
    /// <summary>
    /// One form's artwork. <see cref="Geometry"/> has its origin at the
    /// drawing's own box for rendering; <see cref="BoxX"/>/<see cref="BoxY"/>
    /// keep where that box sits in the form's space, which is what the caller
    /// maps through a placement's CTM to get the rectangle on the page.
    /// </summary>
    internal sealed record FormDrawing(
        string StreamHash,
        DrawingGeometry Geometry,
        double BoxX, double BoxY)
    {
        /// <summary>The box's size, which is the drawing's own — one source, not two.</summary>
        public double BoxWidth => Geometry.Width;

        public double BoxHeight => Geometry.Height;
    }

    /// <summary>
    /// Read a form's artwork, or null when it paints no paths of its own —
    /// a form holding only images, or only text, is not a drawing.
    /// </summary>
    public static FormDrawing? Read(PdfDictionary formDict)
    {
        var bytes = formDict.Stream?.UnfilteredValue;
        if (bytes is null || bytes.Length == 0) return null;

        var hits = ContentStreamWalker.FindShapes(ContentReader.ReadContent(bytes));
        if (hits.Count == 0) return null;

        var matrix = ReadMatrix(formDict);

        // First pass: put every path into the form's placed space and find the
        // one box they all share. These are the lists that get handed out — the
        // second pass slides them to the origin in place rather than copying
        // every point again.
        var mapped = new List<(ContentStreamWalker.ShapeHit Hit, List<PointD>[] Points)>(hits.Count);
        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;

        foreach (var hit in hits)
        {
            var perElement = new List<PointD>[hit.Geometry.Elements.Count];
            for (var e = 0; e < hit.Geometry.Elements.Count; e++)
            {
                var element = hit.Geometry.Elements[e];
                var points = new List<PointD>(element.Points.Count);
                foreach (var point in element.Points)
                {
                    // A geometry's points are relative to its own path box, so
                    // the hit's origin puts them back into form space first.
                    var (x, y) = matrix.Apply(hit.X + point.X, hit.Y + point.Y);
                    points.Add(new PointD(x, y));
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
                perElement[e] = points;
            }
            mapped.Add((hit, perElement));
        }

        // Every path could have been empty of points (an "h" on its own).
        if (minX > maxX || minY > maxY) return null;

        // Second pass: slide the shared box to the origin, rewriting each point
        // where it lies. Each part keeps its own paint operator and colours, so
        // a drawing may mix fills and strokes the way the silhouette and its
        // speech bubble do, and its width and height are re-measured here
        // because the hit's own were taken before the matrix applied.
        var parts = new List<ShapeGeometry>(mapped.Count);
        foreach (var (hit, perElement) in mapped)
        {
            var elements = new ShapePathElement[perElement.Length];
            double partMinX = double.MaxValue, partMinY = double.MaxValue;
            double partMaxX = double.MinValue, partMaxY = double.MinValue;

            for (var e = 0; e < perElement.Length; e++)
            {
                var points = perElement[e];
                for (var i = 0; i < points.Count; i++)
                {
                    var x = Math.Round(points[i].X - minX, 2);
                    var y = Math.Round(points[i].Y - minY, 2);
                    points[i] = new PointD(x, y);
                    if (x < partMinX) partMinX = x;
                    if (x > partMaxX) partMaxX = x;
                    if (y < partMinY) partMinY = y;
                    if (y > partMaxY) partMaxY = y;
                }
                elements[e] = new ShapePathElement(hit.Geometry.Elements[e].Operator, points);
            }

            parts.Add(hit.Geometry with
            {
                Elements = elements,
                Width = partMaxX > partMinX ? partMaxX - partMinX : 0,
                Height = partMaxY > partMinY ? partMaxY - partMinY : 0,
            });
        }

        return new FormDrawing(
            ImageXObjectCollector.ComputeStreamHash(formDict),
            new DrawingGeometry(parts, maxX - minX, maxY - minY),
            minX, minY);
    }

    /// <summary>
    /// A form's <c>/Matrix</c>, or the identity when it has none. Six numbers
    /// or it is not a matrix — a malformed one is ignored rather than guessed
    /// at, which leaves the artwork unscaled instead of somewhere arbitrary.
    /// </summary>
    static AffineMatrix ReadMatrix(PdfDictionary formDict)
    {
        var array = formDict.Elements.GetArray("/Matrix");
        if (array is null || array.Elements.Count < 6) return AffineMatrix.Identity;
        return new AffineMatrix(
            array.Elements.GetReal(0), array.Elements.GetReal(1),
            array.Elements.GetReal(2), array.Elements.GetReal(3),
            array.Elements.GetReal(4), array.Elements.GetReal(5));
    }
}
