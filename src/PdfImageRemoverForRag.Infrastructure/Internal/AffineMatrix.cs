namespace PdfImageRemoverForRag.Infrastructure.Internal;

/// <summary>
/// Minimal 2D affine matrix used to track the PDF current transformation
/// matrix (CTM) as we walk a content stream. Matches the PDF spec §8.3.3
/// where a matrix is (a, b, c, d, e, f) and points map as
/// (x', y') = (a·x + c·y + e, b·x + d·y + f).
/// </summary>
internal readonly record struct AffineMatrix(double A, double B, double C, double D, double E, double F)
{
    public static AffineMatrix Identity => new(1, 0, 0, 1, 0, 0);

    /// <summary>
    /// Pre-multiply this matrix by <paramref name="other"/>. Follows the PDF
    /// convention where <c>cm</c> pre-multiplies user space with the
    /// operator's matrix, i.e. new CTM = other × old CTM.
    /// </summary>
    public AffineMatrix Multiply(AffineMatrix o) => new(
        A * o.A + B * o.C,
        A * o.B + B * o.D,
        C * o.A + D * o.C,
        C * o.B + D * o.D,
        E * o.A + F * o.C + o.E,
        E * o.B + F * o.D + o.F);

    /// <summary>Apply the matrix to a single point.</summary>
    public (double X, double Y) Apply(double x, double y) =>
        (A * x + C * y + E, B * x + D * y + F);

    /// <summary>
    /// Map the unit square (where images are drawn before the CTM) to the
    /// axis-aligned bounding box in page space.
    /// </summary>
    public (double X, double Y, double W, double H) MapUnitBoundingBox()
    {
        var p0 = Apply(0, 0);
        var p1 = Apply(1, 0);
        var p2 = Apply(0, 1);
        var p3 = Apply(1, 1);
        double xMin = Math.Min(Math.Min(p0.X, p1.X), Math.Min(p2.X, p3.X));
        double xMax = Math.Max(Math.Max(p0.X, p1.X), Math.Max(p2.X, p3.X));
        double yMin = Math.Min(Math.Min(p0.Y, p1.Y), Math.Min(p2.Y, p3.Y));
        double yMax = Math.Max(Math.Max(p0.Y, p1.Y), Math.Max(p2.Y, p3.Y));
        return (xMin, yMin, xMax - xMin, yMax - yMin);
    }
}
