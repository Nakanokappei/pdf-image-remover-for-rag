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
    RgbColor? FillColor = null);
