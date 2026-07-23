using System.Text;
using PdfImageRemoverForRag.Core.Models;
using PdfSharp.Pdf.Content.Objects;

namespace PdfImageRemoverForRag.Infrastructure.Internal;

/// <summary>
/// Walks a parsed content stream (<see cref="CSequence"/>) with a
/// <see cref="TransformStack"/> so <c>Do</c> hits carry their on-page
/// bounding box. Also exposes helpers used by the verifier.
/// </summary>
internal static class ContentStreamWalker
{
    internal readonly record struct DrawCall(string ResourceName,
        double X, double Y, double Width, double Height);

    /// <summary>
    /// Scan the whole sequence and emit one <see cref="DrawCall"/> per
    /// <c>Do</c> operator, including the resource name and the AABB derived
    /// from the CTM at the point of the operator.
    /// </summary>
    public static List<DrawCall> FindDrawCalls(CSequence sequence)
    {
        var hits = new List<DrawCall>();
        var stack = new TransformStack();
        foreach (var obj in sequence)
        {
            if (obj is not COperator op) continue;
            switch (op.OpCode.Name)
            {
                case "q":
                    stack.Push();
                    break;
                case "Q":
                    stack.Pop();
                    break;
                case "cm":
                    if (op.Operands.Count == 6)
                    {
                        stack.Concat(new AffineMatrix(
                            Num(op.Operands[0]), Num(op.Operands[1]),
                            Num(op.Operands[2]), Num(op.Operands[3]),
                            Num(op.Operands[4]), Num(op.Operands[5])));
                    }
                    break;
                case "Do":
                    if (op.Operands.Count == 1 && op.Operands[0] is CName name)
                    {
                        var box = stack.Current.MapUnitBoundingBox();
                        hits.Add(new DrawCall(name.Name, box.X, box.Y, box.W, box.H));
                    }
                    break;
            }
        }
        return hits;
    }

    /// <summary>True if any Do operator in the sequence references <paramref name="name"/>.</summary>
    public static bool ContainsDoFor(CSequence sequence, string name)
    {
        foreach (var obj in sequence)
        {
            if (obj is COperator op && op.OpCode.Name == "Do"
                && op.Operands.Count == 1 && op.Operands[0] is CName n
                && n.Name == name)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// In-place removal of Do operators whose operand name is in
    /// <paramref name="namesToDrop"/>. Returns the number of operators removed.
    /// The surrounding q/cm/Q graphics-state operators are left untouched — they
    /// become inert no-ops without the Do call and are safe to keep.
    /// </summary>
    public static int RemoveDoOperators(CSequence sequence, IReadOnlySet<string> namesToDrop)
    {
        int removed = 0;
        for (int i = sequence.Count - 1; i >= 0; i--)
        {
            if (sequence[i] is COperator op
                && op.OpCode.Name == "Do"
                && op.Operands.Count == 1
                && op.Operands[0] is CName n
                && namesToDrop.Contains(n.Name))
            {
                sequence.RemoveAt(i);
                removed++;
            }
        }
        return removed;
    }

    static double Num(CObject o) => o switch
    {
        CReal r => r.Value,
        CInteger i => i.Value,
        _ => 0,
    };

    // -----------------------------------------------------------------------
    // Vector shapes (path construction + paint operators)
    // -----------------------------------------------------------------------

    /// <summary>
    /// One vector path object: the operators from the first path-construction
    /// operator through the paint operator, plus a signature (path shape in
    /// page coordinates) and an on-page bounding box. Index range is inclusive.
    /// </summary>
    public sealed record ShapeHit(string Signature, double X, double Y, double Width, double Height,
        int StartIndex, int EndIndex, ShapeGeometry Geometry);

    static readonly HashSet<string> PathConstructionOps = new(StringComparer.Ordinal)
        { "m", "l", "c", "v", "y", "re", "h" };
    static readonly HashSet<string> PathPaintOps = new(StringComparer.Ordinal)
        { "S", "s", "f", "F", "f*", "B", "B*", "b", "b*", "n" };

    /// <summary>
    /// Find every paintable vector path. A path runs from its first
    /// construction operator to its paint operator. Its signature is
    /// <b>position-independent</b>: the path's points (mapped through the CTM
    /// then translated so the bounding box starts at the origin) plus the paint
    /// operator, line width, and stroke/fill color. So the same-shaped, same-
    /// styled path counts as one group even when drawn at different positions,
    /// while a different width or color makes it a different group. Paths that
    /// also set a clip (W / W*) are skipped — removing them could change how
    /// unrelated content is clipped.
    /// </summary>
    public static List<ShapeHit> FindShapes(CSequence sequence)
    {
        var hits = new List<ShapeHit>();
        var ctm = new TransformStack();
        var gs = new GraphicsStateStack();
        var elements = new List<(string Op, double[] Points)>();
        var xs = new List<double>();
        var ys = new List<double>();
        int startIndex = -1;
        bool inPath = false;
        bool hasClip = false;

        for (int i = 0; i < sequence.Count; i++)
        {
            if (sequence[i] is not COperator op) continue;
            var name = op.OpCode.Name;

            switch (name)
            {
                case "q": ctm.Push(); gs.Push(); break;
                case "Q": ctm.Pop(); gs.Pop(); break;
                case "cm":
                    if (op.Operands.Count == 6)
                    {
                        ctm.Concat(new AffineMatrix(
                            Num(op.Operands[0]), Num(op.Operands[1]),
                            Num(op.Operands[2]), Num(op.Operands[3]),
                            Num(op.Operands[4]), Num(op.Operands[5])));
                    }
                    break;

                // Graphics-state operators that make a shape's identity.
                case "w": if (op.Operands.Count >= 1) gs.SetLineWidth(Num(op.Operands[0])); break;
                case "RG": if (op.Operands.Count >= 3) gs.SetStrokeColor(RgbFromRgb(op)); break;
                case "rg": if (op.Operands.Count >= 3) gs.SetFillColor(RgbFromRgb(op)); break;
                case "G": if (op.Operands.Count >= 1) gs.SetStrokeColor(RgbFromGray(op)); break;
                case "g": if (op.Operands.Count >= 1) gs.SetFillColor(RgbFromGray(op)); break;
                case "K": if (op.Operands.Count >= 4) gs.SetStrokeColor(RgbFromCmyk(op)); break;
                case "k": if (op.Operands.Count >= 4) gs.SetFillColor(RgbFromCmyk(op)); break;

                case "W":
                case "W*":
                    if (inPath) hasClip = true;
                    break;

                default:
                    if (PathConstructionOps.Contains(name))
                    {
                        if (!inPath)
                        {
                            inPath = true;
                            startIndex = i;
                            hasClip = false;
                            elements.Clear();
                            xs.Clear();
                            ys.Clear();
                        }
                        var points = MapPathPoints(name, op, ctm.Current);
                        elements.Add((name, points));
                        for (int k = 0; k + 1 < points.Length; k += 2)
                        {
                            xs.Add(points[k]);
                            ys.Add(points[k + 1]);
                        }
                    }
                    else if (inPath && PathPaintOps.Contains(name))
                    {
                        if (!hasClip && xs.Count > 0)
                        {
                            double minX = xs.Min(), maxX = xs.Max();
                            double minY = ys.Min(), maxY = ys.Max();
                            var signature = BuildShapeSignature(elements, minX, minY, name, gs);
                            var geometry = BuildShapeGeometry(
                                elements, minX, minY, maxX - minX, maxY - minY, name, gs);
                            hits.Add(new ShapeHit(signature,
                                minX, minY, maxX - minX, maxY - minY, startIndex, i, geometry));
                        }
                        inPath = false;
                    }
                    break;
            }
        }
        return hits;
    }

    /// <summary>
    /// Remove the operator range of every shape whose signature is selected.
    /// Ranges are deleted back-to-front so earlier indices stay valid.
    /// Returns the number of shapes removed.
    /// </summary>
    public static int RemoveShapes(CSequence sequence, IReadOnlySet<string> signatures)
    {
        var targets = FindShapes(sequence)
            .Where(s => signatures.Contains(s.Signature))
            .OrderByDescending(s => s.StartIndex)
            .ToList();
        foreach (var shape in targets)
        {
            for (int i = shape.EndIndex; i >= shape.StartIndex; i--) sequence.RemoveAt(i);
        }
        return targets.Count;
    }

    /// <summary>
    /// Map a construction operator's points through the CTM into page space,
    /// returned as a flat x,y,x,y… array.
    /// </summary>
    static double[] MapPathPoints(string name, COperator op, AffineMatrix ctm)
    {
        var ops = op.Operands;
        var points = new List<double>(8);
        void Add(double x, double y)
        {
            var (px, py) = ctm.Apply(x, y);
            points.Add(px);
            points.Add(py);
        }

        switch (name)
        {
            case "m":
            case "l":
                if (ops.Count >= 2) Add(Num(ops[0]), Num(ops[1]));
                break;
            case "c":
                if (ops.Count >= 6)
                {
                    Add(Num(ops[0]), Num(ops[1]));
                    Add(Num(ops[2]), Num(ops[3]));
                    Add(Num(ops[4]), Num(ops[5]));
                }
                break;
            case "v":
            case "y":
                if (ops.Count >= 4)
                {
                    Add(Num(ops[0]), Num(ops[1]));
                    Add(Num(ops[2]), Num(ops[3]));
                }
                break;
            case "re":
                if (ops.Count >= 4)
                {
                    double x = Num(ops[0]), y = Num(ops[1]), w = Num(ops[2]), h = Num(ops[3]);
                    Add(x, y);
                    Add(x + w, y);
                    Add(x + w, y + h);
                    Add(x, y + h);
                }
                break;
            case "h":
                break;
        }
        return points.ToArray();
    }

    /// <summary>
    /// Build the position-independent shape signature: each construction
    /// operator's points translated by (-minX, -minY) and rounded, followed by
    /// the paint operator, line width, and stroke/fill color.
    /// </summary>
    static string BuildShapeSignature(List<(string Op, double[] Points)> elements,
        double minX, double minY, string paintOp, GraphicsStateStack gs)
    {
        var sb = new StringBuilder();
        foreach (var (opName, points) in elements)
        {
            sb.Append(opName);
            for (int k = 0; k + 1 < points.Length; k += 2)
            {
                sb.Append(Math.Round(points[k] - minX, 1)).Append(',')
                  .Append(Math.Round(points[k + 1] - minY, 1)).Append(';');
            }
        }
        sb.Append('|').Append(paintOp);
        sb.Append("|w:").Append(gs.LineWidth);
        sb.Append("|s:").Append(ColorKey(gs.StrokeColor));
        sb.Append("|f:").Append(ColorKey(gs.FillColor));
        return sb.ToString();
    }

    static string ColorKey(RgbColor? color) =>
        color is { } c ? $"{c.R},{c.G},{c.B}" : "-";

    /// <summary>
    /// Build renderable geometry from the same relative points used for the
    /// signature: each element's points translated by (-minX, -minY). The App
    /// draws this into a thumbnail (Infrastructure has no GDI dependency).
    /// </summary>
    static ShapeGeometry BuildShapeGeometry(List<(string Op, double[] Points)> elements,
        double minX, double minY, double width, double height, string paintOp, GraphicsStateStack gs)
    {
        var pathElements = new List<ShapePathElement>(elements.Count);
        foreach (var (opName, points) in elements)
        {
            var localPoints = new List<PointD>(points.Length / 2);
            for (int k = 0; k + 1 < points.Length; k += 2)
            {
                localPoints.Add(new PointD(
                    Math.Round(points[k] - minX, 2),
                    Math.Round(points[k + 1] - minY, 2)));
            }
            pathElements.Add(new ShapePathElement(opName, localPoints));
        }
        return new ShapeGeometry(pathElements, width, height, paintOp, gs.LineWidth,
            gs.StrokeColor, gs.FillColor);
    }

    // PDF color components are 0..1; convert each space to 8-bit RGB.
    static RgbColor RgbFromRgb(COperator op) => new(
        ToByte(Num(op.Operands[0])), ToByte(Num(op.Operands[1])), ToByte(Num(op.Operands[2])));

    static RgbColor RgbFromGray(COperator op)
    {
        byte g = ToByte(Num(op.Operands[0]));
        return new RgbColor(g, g, g);
    }

    static RgbColor RgbFromCmyk(COperator op)
    {
        double c = Num(op.Operands[0]), m = Num(op.Operands[1]),
               y = Num(op.Operands[2]), k = Num(op.Operands[3]);
        return new RgbColor(
            ToByte((1 - c) * (1 - k)),
            ToByte((1 - m) * (1 - k)),
            ToByte((1 - y) * (1 - k)));
    }

    static byte ToByte(double component) => (byte)Math.Clamp(component * 255.0, 0, 255);

    // -----------------------------------------------------------------------
    // Text-showing operators (Tj / TJ / ' / ")
    // -----------------------------------------------------------------------

    /// <summary>
    /// Collect the string shown by every text-showing operator on the page,
    /// decoded to readable Unicode via <paramref name="decoder"/> (needed for
    /// Identity-H / CJK fonts). A TJ array's string elements are concatenated
    /// (spacing numbers ignored) so the value matches what a reader sees. The
    /// current font is tracked from <c>Tf</c> operators.
    /// </summary>
    public static List<string> FindShownTexts(CSequence sequence, PdfTextDecoder decoder)
    {
        var texts = new List<string>();
        string? currentFont = null;
        foreach (var obj in sequence)
        {
            if (obj is not COperator op) continue;
            if (TryGetFontName(op, out var fontName)) currentFont = fontName;
            else if (TryGetShownText(op, decoder, currentFont, out var value)) texts.Add(value);
        }
        return texts;
    }

    /// <summary>
    /// Remove every text-showing operator whose decoded string is in
    /// <paramref name="textValues"/>. Surrounding text-positioning operators
    /// (Td/Tm/Tf) are left in place — without the show operator they simply
    /// move the (unused) text cursor, which does not affect other content.
    /// Returns the number of operators removed.
    /// </summary>
    public static int RemoveTextOperators(CSequence sequence, IReadOnlySet<string> textValues,
        PdfTextDecoder decoder)
    {
        // Font is tracked forward, but removal walks back-to-front; collect
        // matching indices in a forward pass, then delete them in reverse.
        var indicesToRemove = new List<int>();
        string? currentFont = null;
        for (int i = 0; i < sequence.Count; i++)
        {
            if (sequence[i] is not COperator op) continue;
            if (TryGetFontName(op, out var fontName)) currentFont = fontName;
            else if (TryGetShownText(op, decoder, currentFont, out var value)
                     && textValues.Contains(value))
            {
                indicesToRemove.Add(i);
            }
        }
        for (int j = indicesToRemove.Count - 1; j >= 0; j--) sequence.RemoveAt(indicesToRemove[j]);
        return indicesToRemove.Count;
    }

    /// <summary>Read the font name from a <c>Tf</c> operator (/Name size Tf).</summary>
    static bool TryGetFontName(COperator op, out string fontName)
    {
        if (op.OpCode.Name == "Tf" && op.Operands.Count >= 1 && op.Operands[0] is CName name)
        {
            fontName = name.Name;
            return true;
        }
        fontName = string.Empty;
        return false;
    }

    /// <summary>
    /// Extract and decode the shown string from a text operator. The string
    /// operand is always last: Tj/'(string), "(aw ac string), TJ(array).
    /// </summary>
    static bool TryGetShownText(COperator op, PdfTextDecoder decoder, string? currentFont, out string value)
    {
        value = string.Empty;
        switch (op.OpCode.Name)
        {
            case "Tj":
            case "'":
            case "\"":
                if (op.Operands.Count >= 1 && op.Operands[^1] is CString cs)
                {
                    value = decoder.Decode(currentFont, cs.Value);
                    return value.Length > 0;
                }
                return false;
            case "TJ":
                if (op.Operands.Count >= 1 && op.Operands[0] is CArray array)
                {
                    var builder = new StringBuilder();
                    foreach (var element in array)
                    {
                        if (element is CString elementString)
                        {
                            builder.Append(decoder.Decode(currentFont, elementString.Value));
                        }
                    }
                    value = builder.ToString();
                    return value.Length > 0;
                }
                return false;
            default:
                return false;
        }
    }
}
