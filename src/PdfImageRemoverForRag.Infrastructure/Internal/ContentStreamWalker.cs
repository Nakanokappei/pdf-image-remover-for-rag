using System.Text;
using PdfImageRemoverForRag.Core.Grouping;
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
    /// <summary>
    /// <see cref="Index"/> is the operator's position in the sequence.
    /// <see cref="Ctm"/> is the transform in force at the operator: the box is
    /// enough for an image, which is drawn in the unit square, but a Form
    /// XObject's content has coordinates of its own and needs the matrix to be
    /// placed on the page.
    /// </summary>
    internal readonly record struct DrawCall(string ResourceName,
        double X, double Y, double Width, double Height, int Index, AffineMatrix Ctm);

    /// <summary>
    /// Scan the whole sequence and emit one <see cref="DrawCall"/> per
    /// <c>Do</c> operator, including the resource name and the AABB derived
    /// from the CTM at the point of the operator.
    /// </summary>
    public static List<DrawCall> FindDrawCalls(CSequence sequence)
    {
        var hits = new List<DrawCall>();
        var stack = new TransformStack();
        for (int index = 0; index < sequence.Count; index++)
        {
            if (sequence[index] is not COperator op) continue;
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
                        hits.Add(new DrawCall(
                            name.Name, box.X, box.Y, box.W, box.H, index, stack.Current));
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
    /// Remove only the instances that sit inside <paramref name="region"/> — the
    /// draw calls, shown strings and painted paths that the region's members
    /// name, and only where they overlap it.
    ///
    /// This is what flattening an overlap needs and plain removal does not.
    /// Removing a text group deletes every showing of that string in the file,
    /// which is right when the user asked for the string to go; a flattened
    /// region has replaced one place with pixels, so the identical header on the
    /// next page must survive. Instances are matched by geometry rather than by
    /// a stored operator index because the file is re-read from disk here and
    /// the indices from analysis no longer apply.
    ///
    /// Returns the number of operators (or operator ranges) removed.
    /// </summary>
    public static int RemoveInRegion(
        CSequence sequence,
        OverlapRegion region,
        IReadOnlySet<string> imageResourceNames,
        PdfTextDecoder decoder,
        PdfFontMetrics metrics,
        ICollection<(RemovableKind Kind, string Key)>? deleted = null)
    {
        var textValues = new HashSet<string>(
            region.Members.Where(m => m.Kind == RemovableKind.Text).Select(m => m.Identity),
            StringComparer.Ordinal);
        var shapeSignatures = new HashSet<string>(
            region.Members.Where(m => m.Kind == RemovableKind.Shape).Select(m => m.Identity),
            StringComparer.Ordinal);

        // Collect (start, end) ranges in one forward pass, then delete
        // back-to-front so earlier indices stay valid.
        var ranges = new List<(int Start, int End)>();

        if (imageResourceNames.Count > 0)
        {
            foreach (var call in FindDrawCalls(sequence))
            {
                if (!imageResourceNames.Contains(call.ResourceName)) continue;
                if (!OverlapDetector.RegionOverlaps(region, call.X, call.Y, call.Width, call.Height)) continue;
                ranges.Add((call.Index, call.Index));
                // Reported by resource name; the caller holds the mapping back
                // to the stream hash, and re-hashing here to avoid passing it
                // would be deciding image identity in a second place.
                deleted?.Add((RemovableKind.Image, call.ResourceName));
            }
        }

        if (textValues.Count > 0)
        {
            foreach (var hit in FindTexts(sequence, decoder, metrics))
            {
                if (!textValues.Contains(hit.Value)) continue;
                if (!OverlapDetector.RegionOverlaps(region, hit.X, hit.Y, hit.Width, hit.Height)) continue;
                ranges.Add((hit.Index, hit.Index));
                deleted?.Add((RemovableKind.Text, hit.Value));
            }
        }

        if (shapeSignatures.Count > 0)
        {
            foreach (var hit in FindShapes(sequence))
            {
                if (!shapeSignatures.Contains(hit.Signature)) continue;
                if (!OverlapDetector.RegionOverlaps(region, hit.X, hit.Y, hit.Width, hit.Height)) continue;
                ranges.Add((hit.StartIndex, hit.EndIndex));
                deleted?.Add((RemovableKind.Shape, hit.Signature));
            }
        }

        foreach (var range in ranges.OrderByDescending(r => r.Start))
        {
            for (int i = range.End; i >= range.Start; i--) sequence.RemoveAt(i);
        }
        return ranges.Count;
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
    /// One shown string with the rectangle it covers on the page, in points
    /// with the origin at the page's bottom-left — the same space image
    /// occurrences use. <see cref="Index"/> is the operator's position in the
    /// sequence, which is what lets one instance be removed while identical
    /// strings elsewhere stay.
    /// </summary>
    public sealed record TextHit(
        string Value, double X, double Y, double Width, double Height, int Index);

    /// <summary>Fraction of the font size a glyph box rises above the baseline.</summary>
    const double AscenderFraction = 0.85;

    /// <summary>Fraction of the font size a glyph box drops below the baseline.</summary>
    const double DescenderFraction = 0.25;

    /// <summary>
    /// Find every shown string together with its rectangle on the page.
    ///
    /// This runs the text state machine of PDF §9.4: the text matrix and line
    /// matrix (BT/Td/TD/T*/Tm), the font and size (Tf), and the spacing
    /// parameters that change how far a string advances (Tc, Tw, Tz, TL, Ts).
    /// The width of the string itself comes from <paramref name="metrics"/>;
    /// the height is the font size, from a nominal descender to a nominal
    /// ascender, because the real per-glyph extents are not worth reading the
    /// font programs for.
    ///
    /// Only overlap detection needs the rectangles — plain removal matches on
    /// the string value alone.
    /// </summary>
    public static List<TextHit> FindTexts(
        CSequence sequence, PdfTextDecoder decoder, PdfFontMetrics metrics)
    {
        var hits = new List<TextHit>();
        var ctm = new TransformStack();

        // Text state. Everything except the matrices survives BT/ET.
        var textMatrix = AffineMatrix.Identity;
        var lineMatrix = AffineMatrix.Identity;
        string? font = null;
        double fontSize = 0, charSpacing = 0, wordSpacing = 0, leading = 0, rise = 0;
        double horizontalScale = 1.0;

        for (int i = 0; i < sequence.Count; i++)
        {
            if (sequence[i] is not COperator op) continue;
            var name = op.OpCode.Name;

            switch (name)
            {
                case "q": ctm.Push(); break;
                case "Q": ctm.Pop(); break;
                case "cm":
                    if (op.Operands.Count == 6)
                    {
                        ctm.Concat(new AffineMatrix(
                            Num(op.Operands[0]), Num(op.Operands[1]),
                            Num(op.Operands[2]), Num(op.Operands[3]),
                            Num(op.Operands[4]), Num(op.Operands[5])));
                    }
                    break;

                case "BT":
                    textMatrix = lineMatrix = AffineMatrix.Identity;
                    break;

                case "Tf":
                    if (op.Operands.Count >= 2 && op.Operands[0] is CName fontName)
                    {
                        font = fontName.Name;
                        fontSize = Num(op.Operands[1]);
                    }
                    break;

                case "Tc": if (op.Operands.Count >= 1) charSpacing = Num(op.Operands[0]); break;
                case "Tw": if (op.Operands.Count >= 1) wordSpacing = Num(op.Operands[0]); break;
                case "TL": if (op.Operands.Count >= 1) leading = Num(op.Operands[0]); break;
                case "Ts": if (op.Operands.Count >= 1) rise = Num(op.Operands[0]); break;
                case "Tz": if (op.Operands.Count >= 1) horizontalScale = Num(op.Operands[0]) / 100.0; break;

                case "Tm":
                    if (op.Operands.Count == 6)
                    {
                        textMatrix = lineMatrix = new AffineMatrix(
                            Num(op.Operands[0]), Num(op.Operands[1]),
                            Num(op.Operands[2]), Num(op.Operands[3]),
                            Num(op.Operands[4]), Num(op.Operands[5]));
                    }
                    break;

                case "Td":
                    if (op.Operands.Count >= 2)
                    {
                        textMatrix = lineMatrix = NextLine(
                            lineMatrix, Num(op.Operands[0]), Num(op.Operands[1]));
                    }
                    break;

                case "TD":
                    if (op.Operands.Count >= 2)
                    {
                        leading = -Num(op.Operands[1]);
                        textMatrix = lineMatrix = NextLine(
                            lineMatrix, Num(op.Operands[0]), Num(op.Operands[1]));
                    }
                    break;

                case "T*":
                    textMatrix = lineMatrix = NextLine(lineMatrix, 0, -leading);
                    break;

                default:
                    if (name is not ("Tj" or "TJ" or "'" or "\"")) break;

                    // The quote operators move to the next line first, and the
                    // double quote also sets the two spacing parameters.
                    if (name is "'" or "\"")
                    {
                        if (name == "\"" && op.Operands.Count >= 3)
                        {
                            wordSpacing = Num(op.Operands[0]);
                            charSpacing = Num(op.Operands[1]);
                        }
                        textMatrix = lineMatrix = NextLine(lineMatrix, 0, -leading);
                    }

                    if (!TryGetShownText(op, decoder, font, out var value)) break;

                    var state = new TextState(
                        fontSize, charSpacing, wordSpacing, horizontalScale, rise);
                    double advance = AdvanceOf(op, font, metrics, state);
                    hits.Add(BuildTextHit(value, textMatrix.Multiply(ctm.Current), state, advance, i));
                    textMatrix = new AffineMatrix(1, 0, 0, 1, advance, 0).Multiply(textMatrix);
                    break;
            }
        }
        return hits;
    }

    /// <summary>Text-state values that affect where a string lands and how wide it is.</summary>
    readonly record struct TextState(
        double FontSize, double CharSpacing, double WordSpacing, double HorizontalScale, double Rise);

    /// <summary>Move the line matrix down/along by (tx, ty), as Td does.</summary>
    static AffineMatrix NextLine(AffineMatrix lineMatrix, double tx, double ty) =>
        new AffineMatrix(1, 0, 0, 1, tx, ty).Multiply(lineMatrix);

    /// <summary>
    /// How far the text cursor moves when this operator is shown, in unscaled
    /// text space. A TJ array's numbers move the cursor back (or forward) by
    /// thousandths of the font size and are part of the run's width.
    /// </summary>
    static double AdvanceOf(COperator op, string? font, PdfFontMetrics metrics, TextState state)
    {
        double advance = 0;
        if (op.OpCode.Name == "TJ")
        {
            if (op.Operands.Count >= 1 && op.Operands[^1] is CArray array)
            {
                foreach (var element in array)
                {
                    if (element is CString piece) advance += RunAdvance(piece.Value, font, metrics, state);
                    else advance -= Num(element) / 1000.0 * state.FontSize * state.HorizontalScale;
                }
            }
            return advance;
        }

        if (op.Operands.Count >= 1 && op.Operands[^1] is CString cs)
        {
            advance = RunAdvance(cs.Value, font, metrics, state);
        }
        return advance;
    }

    /// <summary>
    /// Advance of one string: the glyphs' widths plus character spacing, plus
    /// word spacing on every single-byte space, all scaled horizontally.
    /// </summary>
    static double RunAdvance(string raw, string? font, PdfFontMetrics metrics, TextState state)
    {
        double glyphs = metrics.MeasureWidth(font, raw) / 1000.0 * state.FontSize;

        // Character spacing applies once per code, and a composite font spends
        // two bytes on each.
        int codeCount = metrics.IsComposite(font) ? raw.Length / 2 : raw.Length;
        double spacing = state.CharSpacing * codeCount;

        // Word spacing applies to byte 32 only, and (per the spec) never to a
        // 2-byte composite code.
        if (!metrics.IsComposite(font) && state.WordSpacing != 0)
        {
            foreach (var c in raw)
            {
                if (c == ' ') spacing += state.WordSpacing;
            }
        }
        return (glyphs + spacing) * state.HorizontalScale;
    }

    /// <summary>
    /// Map the run's box — baseline to ascender/descender, zero to its advance —
    /// through the text and current transformation matrices into page space.
    /// </summary>
    static TextHit BuildTextHit(
        string value, AffineMatrix toPage, TextState state, double advance, int index)
    {
        double top = state.Rise + (state.FontSize * AscenderFraction);
        double bottom = state.Rise - (state.FontSize * DescenderFraction);
        var corners = new[]
        {
            toPage.Apply(0, bottom),
            toPage.Apply(advance, bottom),
            toPage.Apply(0, top),
            toPage.Apply(advance, top),
        };
        double minX = corners.Min(p => p.X), maxX = corners.Max(p => p.X);
        double minY = corners.Min(p => p.Y), maxY = corners.Max(p => p.Y);
        return new TextHit(value, minX, minY, maxX - minX, maxY - minY, index);
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
