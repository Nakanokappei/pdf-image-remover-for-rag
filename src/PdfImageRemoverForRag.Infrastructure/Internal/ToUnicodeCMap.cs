using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PdfImageRemoverForRag.Infrastructure.Internal;

/// <summary>
/// A parsed <c>/ToUnicode</c> CMap: maps a font's character codes to Unicode
/// text. Composite (Type0 / Identity-H) fonts encode text as 2-byte codes
/// that are meaningless without this map — decoding through it is what turns
/// a garbled Japanese/CJK string back into readable text.
/// </summary>
internal sealed class ToUnicodeCMap
{
    readonly IReadOnlyDictionary<int, string> _codeToText;
    readonly int _codeByteLength;

    ToUnicodeCMap(IReadOnlyDictionary<int, string> codeToText, int codeByteLength)
    {
        _codeToText = codeToText;
        _codeByteLength = codeByteLength;
    }

    /// <summary>
    /// Parse the textual CMap program. Reads the codespace byte length and
    /// the <c>bfchar</c> / <c>bfrange</c> sections (both the incremental and
    /// the array forms). Robust to unknown constructs — anything it cannot
    /// parse is simply absent from the map (decoded as the replacement char).
    /// </summary>
    public static ToUnicodeCMap Parse(string cmapText)
    {
        // Code byte length comes from the first codespacerange entry's hex
        // width (e.g. <2AC0> → 2 bytes). Default to 2 (Identity-H is the
        // common case that needs decoding at all).
        int codeByteLength = 2;
        var csMatch = Regex.Match(cmapText, @"begincodespacerange\s*<([0-9A-Fa-f]+)>");
        if (csMatch.Success) codeByteLength = Math.Max(1, csMatch.Groups[1].Value.Length / 2);

        var map = new Dictionary<int, string>();
        ParseBfChar(cmapText, map);
        ParseBfRange(cmapText, map);
        return new ToUnicodeCMap(map, codeByteLength);
    }

    /// <summary>
    /// Decode a raw code-byte string (character codes, big-endian, fixed
    /// width) into Unicode text. Unknown codes become U+FFFD so the caller
    /// still gets a stable, comparable string.
    /// </summary>
    public string Decode(ReadOnlySpan<byte> codeBytes)
    {
        var builder = new StringBuilder();
        for (int i = 0; i + _codeByteLength <= codeBytes.Length; i += _codeByteLength)
        {
            int code = 0;
            for (int k = 0; k < _codeByteLength; k++) code = (code << 8) | codeBytes[i + k];
            builder.Append(_codeToText.TryGetValue(code, out var s) ? s : "�");
        }
        return builder.ToString();
    }

    static void ParseBfChar(string cmapText, Dictionary<int, string> map)
    {
        // bfchar entries are "<src> <dst>" pairs.
        foreach (Match block in Regex.Matches(cmapText, @"beginbfchar(.*?)endbfchar", RegexOptions.Singleline))
        {
            foreach (Match m in Regex.Matches(block.Groups[1].Value,
                @"<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>"))
            {
                int code = ParseHexInt(m.Groups[1].Value);
                map[code] = HexToUtf16(m.Groups[2].Value);
            }
        }
    }

    static void ParseBfRange(string cmapText, Dictionary<int, string> map)
    {
        // bfrange entries come in two forms:
        //   <lo> <hi> <dst>      — dst increments once per code in [lo, hi]
        //   <lo> <hi> [<d0> <d1> …] — one dst per code, listed explicitly
        foreach (Match block in Regex.Matches(cmapText, @"beginbfrange(.*?)endbfrange", RegexOptions.Singleline))
        {
            foreach (var rawLine in block.Groups[1].Value.Split('\n'))
            {
                var line = rawLine.Trim();
                if (line.Length == 0) continue;

                var array = Regex.Match(line, @"<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>\s*\[(.*)\]");
                if (array.Success)
                {
                    int lo = ParseHexInt(array.Groups[1].Value);
                    var dsts = Regex.Matches(array.Groups[3].Value, @"<([0-9A-Fa-f]+)>");
                    for (int i = 0; i < dsts.Count; i++)
                    {
                        map[lo + i] = HexToUtf16(dsts[i].Groups[1].Value);
                    }
                    continue;
                }

                var incremental = Regex.Match(line, @"<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>");
                if (incremental.Success)
                {
                    int lo = ParseHexInt(incremental.Groups[1].Value);
                    int hi = ParseHexInt(incremental.Groups[2].Value);
                    string dstHex = incremental.Groups[3].Value;
                    int dstBase = ParseHexInt(dstHex);
                    // Cap the span defensively so a malformed huge range can't hang.
                    for (int code = lo; code <= hi && code - lo < 65536; code++)
                    {
                        map[code] = HexToUtf16((dstBase + (code - lo)).ToString(
                            "X" + dstHex.Length, CultureInfo.InvariantCulture));
                    }
                }
            }
        }
    }

    static int ParseHexInt(string hex) => int.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    /// <summary>Convert a hex string of UTF-16BE code units into a .NET string.</summary>
    static string HexToUtf16(string hex)
    {
        if (hex.Length % 2 != 0) hex = "0" + hex;
        var bytes = new byte[hex.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
        {
            bytes[i] = byte.Parse(hex.AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }
        return Encoding.BigEndianUnicode.GetString(bytes);
    }
}
