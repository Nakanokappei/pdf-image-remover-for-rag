using System.Text;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;

namespace PdfImageRemoverForRag.Infrastructure.Internal;

/// <summary>
/// Glyph advance widths for the fonts of one page, in 1/1000 of the font size —
/// the unit PDF stores them in. Only overlap detection needs this: to know
/// whether a label sits on a picture we need the rectangle the label covers,
/// and that is the sum of its glyphs' advances.
///
/// Widths come from the font dictionary: <c>/Widths</c> indexed from
/// <c>/FirstChar</c> for simple fonts, <c>/W</c> ranges with the <c>/DW</c>
/// default for composite (Type0/CID) fonts. Fonts that carry neither — the
/// standard 14, which a viewer is expected to know the metrics of — fall back
/// to a nominal half-em per byte and full em per CID. That is an approximation,
/// and it is a fair one here: a rectangle a few points too wide or narrow can
/// only change whether two objects are judged to touch when they were already
/// within a few points of each other, and the flattened region is the union of
/// the members anyway.
/// </summary>
internal sealed class PdfFontMetrics
{
    /// <summary>Advance assumed for a single-byte code in a font with no widths.</summary>
    const double FallbackSimpleWidth = 500;

    /// <summary>Advance assumed for a CID in a composite font with no /W entry.</summary>
    const double FallbackCompositeWidth = 1000;

    readonly Dictionary<string, FontWidths> _byFontName;

    public PdfFontMetrics(PdfResources? resources)
    {
        _byFontName = new Dictionary<string, FontWidths>(StringComparer.Ordinal);
        var fonts = resources?.Elements.GetDictionary("/Font");
        if (fonts is null) return;

        foreach (var entry in fonts.Elements)
        {
            var fontDict = Resolve(entry.Value);
            if (fontDict is null) continue;
            try
            {
                _byFontName[entry.Key] = Read(fontDict);
            }
            catch
            {
                // A malformed font dictionary must not break analysis; the font
                // simply measures with the fallback advances.
            }
        }
    }

    /// <summary>
    /// Width of <paramref name="rawValue"/> — the operator's string before
    /// ToUnicode decoding, because widths are indexed by character code, not by
    /// the character it maps to — in 1/1000 of the font size.
    /// </summary>
    public double MeasureWidth(string? fontName, string rawValue)
    {
        FontWidths? font = null;
        if (fontName is not null) _byFontName.TryGetValue(fontName, out font);

        // PDFsharp holds string bytes one per char, so Latin1 recovers the
        // original code bytes.
        var bytes = Encoding.Latin1.GetBytes(rawValue);
        if (font is { IsComposite: true })
        {
            double total = 0;
            for (int i = 0; i + 1 < bytes.Length; i += 2)
            {
                int cid = (bytes[i] << 8) | bytes[i + 1];
                total += font.CidWidth(cid);
            }
            return total;
        }

        double sum = 0;
        foreach (var code in bytes) sum += font?.SimpleWidth(code) ?? FallbackSimpleWidth;
        return sum;
    }

    /// <summary>True when the font encodes two bytes per glyph (Type0/CID).</summary>
    public bool IsComposite(string? fontName) =>
        fontName is not null && _byFontName.TryGetValue(fontName, out var font) && font.IsComposite;

    static FontWidths Read(PdfDictionary fontDict)
    {
        var subtype = fontDict.Elements.GetName("/Subtype");
        if (subtype == "/Type0")
        {
            // The widths live on the descendant CIDFont, not on the Type0 shell.
            var descendants = fontDict.Elements.GetArray("/DescendantFonts");
            var descendant = descendants is { Elements.Count: > 0 }
                ? Resolve(descendants.Elements[0]) : null;
            double defaultWidth = descendant?.Elements.ContainsKey("/DW") == true
                ? descendant.Elements.GetReal("/DW") : FallbackCompositeWidth;
            return new FontWidths(
                IsComposite: true,
                FirstChar: 0,
                Widths: Array.Empty<double>(),
                MissingWidth: defaultWidth,
                CidWidths: ReadCidWidths(descendant));
        }

        // Simple font: /Widths runs from /FirstChar, and /MissingWidth (on the
        // descriptor) covers codes outside it.
        var widthsArray = fontDict.Elements.GetArray("/Widths");
        var widths = new double[widthsArray?.Elements.Count ?? 0];
        for (int i = 0; i < widths.Length; i++) widths[i] = ToDouble(widthsArray!.Elements[i]);
        var descriptor = Resolve(fontDict.Elements["/FontDescriptor"]);
        double missing = descriptor?.Elements.ContainsKey("/MissingWidth") == true
            ? descriptor.Elements.GetReal("/MissingWidth") : FallbackSimpleWidth;
        return new FontWidths(
            IsComposite: false,
            FirstChar: fontDict.Elements.GetInteger("/FirstChar"),
            Widths: widths,
            MissingWidth: missing,
            CidWidths: new Dictionary<int, double>());
    }

    /// <summary>
    /// Flatten a CIDFont's <c>/W</c> array into a lookup. It comes in two
    /// shapes: <c>c [w1 w2 ...]</c> assigns consecutive CIDs from c, and
    /// <c>cFirst cLast w</c> assigns one width to the whole range.
    /// </summary>
    static Dictionary<int, double> ReadCidWidths(PdfDictionary? descendant)
    {
        var result = new Dictionary<int, double>();
        var w = descendant?.Elements.GetArray("/W");
        if (w is null) return result;

        for (int i = 0; i < w.Elements.Count;)
        {
            int first = (int)ToDouble(w.Elements[i]);
            if (i + 1 >= w.Elements.Count) break;

            if (Resolve(w.Elements[i + 1]) is null && w.Elements[i + 1] is PdfArray list)
            {
                for (int k = 0; k < list.Elements.Count; k++)
                {
                    result[first + k] = ToDouble(list.Elements[k]);
                }
                i += 2;
            }
            else
            {
                if (i + 2 >= w.Elements.Count) break;
                int last = (int)ToDouble(w.Elements[i + 1]);
                double width = ToDouble(w.Elements[i + 2]);
                // A hostile or broken file could declare a huge range; cap the
                // work rather than allocating millions of entries.
                for (int cid = first; cid <= last && cid - first < 65536; cid++) result[cid] = width;
                i += 3;
            }
        }
        return result;
    }

    static double ToDouble(PdfItem? item) => item switch
    {
        PdfInteger i => i.Value,
        PdfReal r => r.Value,
        PdfReference reference => ToDouble(reference.Value),
        _ => 0,
    };

    static PdfDictionary? Resolve(PdfItem? item) => item switch
    {
        PdfDictionary d => d,
        PdfReference r => r.Value as PdfDictionary,
        _ => null,
    };

    sealed record FontWidths(
        bool IsComposite,
        int FirstChar,
        double[] Widths,
        double MissingWidth,
        Dictionary<int, double> CidWidths)
    {
        public double SimpleWidth(byte code)
        {
            int index = code - FirstChar;
            if (index >= 0 && index < Widths.Length && Widths[index] > 0) return Widths[index];
            return MissingWidth > 0 ? MissingWidth : FallbackSimpleWidth;
        }

        public double CidWidth(int cid) =>
            CidWidths.TryGetValue(cid, out var width) ? width : MissingWidth;
    }
}
