using System.Text;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;

namespace PdfImageRemoverForRag.Infrastructure.Internal;

/// <summary>
/// Decodes text-operator strings into readable Unicode for one page. Built
/// from the page's <c>/Font</c> resources: each font that carries a
/// <c>/ToUnicode</c> CMap gets a decoder, so composite (Identity-H) fonts —
/// which store text as raw 2-byte codes — read back correctly instead of as
/// mojibake. Fonts without a ToUnicode map (WinAnsi TrueType etc.) already
/// hold readable bytes, so their strings pass through unchanged.
/// </summary>
internal sealed class PdfTextDecoder
{
    readonly Dictionary<string, ToUnicodeCMap> _cmapByFontName;

    public PdfTextDecoder(PdfResources? resources)
    {
        _cmapByFontName = new Dictionary<string, ToUnicodeCMap>(StringComparer.Ordinal);
        var fonts = resources?.Elements.GetDictionary("/Font");
        if (fonts is null) return;

        foreach (var kv in fonts.Elements)
        {
            var fontDict = Resolve(kv.Value);
            var toUnicode = Resolve(fontDict?.Elements["/ToUnicode"]);
            if (toUnicode?.Stream is null) continue;
            try
            {
                var cmapText = Encoding.ASCII.GetString(toUnicode.Stream.UnfilteredValue);
                _cmapByFontName[kv.Key] = ToUnicodeCMap.Parse(cmapText);
            }
            catch
            {
                // A malformed CMap must not break analysis — the font just
                // falls back to raw-string passthrough below.
            }
        }
    }

    /// <summary>
    /// Decode one string shown under <paramref name="fontName"/> (the name
    /// from the current <c>Tf</c> operator). Uses the font's ToUnicode map
    /// when present; otherwise returns the raw value, which is already
    /// readable for single-byte encodings.
    /// </summary>
    public string Decode(string? fontName, string rawValue)
    {
        if (fontName is not null && _cmapByFontName.TryGetValue(fontName, out var cmap))
        {
            // PDFsharp stores string bytes one-per-char, so Latin1 round-trips
            // them back to the original code bytes for the CMap to decode.
            return cmap.Decode(Encoding.Latin1.GetBytes(rawValue));
        }
        return rawValue;
    }

    static PdfDictionary? Resolve(PdfItem? item) => item switch
    {
        PdfDictionary d => d,
        PdfReference r => r.Value as PdfDictionary,
        _ => null,
    };
}
