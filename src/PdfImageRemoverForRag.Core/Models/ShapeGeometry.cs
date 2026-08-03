namespace PdfImageRemoverForRag.Core.Models;

/// <summary>A 2D point in a shape's local (bounding-box origin) coordinates.</summary>
public readonly record struct PointD(double X, double Y);

/// <summary>
/// An 8-bit RGB color. Infrastructure converts any PDF color space
/// (RGB / Gray / CMYK) to this so the App can render without knowing color
/// spaces.
/// </summary>
public readonly record struct RgbColor(byte R, byte G, byte B)
{
    /// <summary>Perceived brightness (ITU-R 601 luma), 0–255.</summary>
    public double Luminance => 0.299 * R + 0.587 * G + 0.114 * B;
}

/// <summary>
/// One path-construction operator and its points, in shape-local coordinates
/// (the path's bounding box starts at the origin). Operator is the PDF path op
/// (m/l/c/v/y/re/h).
/// </summary>
public sealed record ShapePathElement(string Operator, IReadOnlyList<PointD> Points);

/// <summary>
/// The renderable geometry of a vector shape, produced by Infrastructure (no
/// GDI dependency) and drawn into a thumbnail by the App. Coordinates are
/// position-independent (bounding-box origin), matching the shape's grouping
/// signature. <see cref="PaintOperator"/> tells the drawer whether to stroke
/// or fill.
/// </summary>
public sealed record ShapeGeometry(
    IReadOnlyList<ShapePathElement> Elements,
    double Width,
    double Height,
    string PaintOperator,
    double LineWidth,
    RgbColor? StrokeColor = null,
    RgbColor? FillColor = null)
{
    /// <summary>
    /// Whether the path is painted with a fill — which is what decides, for
    /// overlap detection, whether the shape hides what is drawn under it.
    ///
    /// This is the paint operator's doing, not an alpha value: real
    /// transparency (an ExtGState <c>/ca</c>, a soft mask) is not tracked, so a
    /// shape filled with white counts as filled even though it may look like
    /// background.
    /// </summary>
    public bool IsFilled => PaintOperator is "f" or "F" or "f*" or "B" or "B*" or "b" or "b*";
}

/// <summary>
/// The renderable geometry of a <see cref="RemovableKind.Drawing"/>: every path
/// one Form XObject paints, kept together because that is the unit the user
/// sees and the unit that can be removed.
///
/// The parts share ONE coordinate box — the drawing's, sized
/// <see cref="Width"/> x <see cref="Height"/> from its origin — rather than each
/// sitting in its own. That is the difference from a lone
/// <see cref="ShapeGeometry"/>, whose coordinates start at its own bounding box,
/// and it is what lets a head, a body and a speech bubble be drawn in the
/// arrangement they actually have. Each part still carries its own paint
/// operator, colors and line width, so a drawing may mix fills and strokes.
/// </summary>
public sealed record DrawingGeometry(
    IReadOnlyList<ShapeGeometry> Parts,
    double Width,
    double Height);
