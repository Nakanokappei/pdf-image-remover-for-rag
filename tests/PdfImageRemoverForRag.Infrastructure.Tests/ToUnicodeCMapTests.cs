using PdfImageRemoverForRag.Infrastructure.Internal;
using Xunit;

namespace PdfImageRemoverForRag.Infrastructure.Tests;

// Hermetic tests for the /ToUnicode CMap decoder — the fix for garbled CJK
// text. The CMap and code bytes are the exact ones observed from a real
// Identity-H Japanese PDF ("機密情報").
public class ToUnicodeCMapTests
{
    const string JapaneseCMap = @"/CIDInit /ProcSet findresource begin
12 dict begin
begincmap
/CIDSystemInfo << /Registry (Adobe)/Ordering (UCS)/Supplement 0>> def
/CMapName /Adobe-Identity-UCS def /CMapType 2 def
1 begincodespacerange
<2AC0><3CEE>
endcodespacerange
4 beginbfrange
<2AC0><2AC0><5831>
<2E55><2E55><5BC6>
<3354><3354><60C5>
<3CEE><3CEE><6A5F>
endbfrange
endcmap CMapName currentdict /CMap defineresource pop end end";

    [Fact]
    public void Decode_IdentityHBfRange_ReturnsReadableJapanese()
    {
        var cmap = ToUnicodeCMap.Parse(JapaneseCMap);
        // Raw 2-byte codes from the Tj operator: 3CEE 2E55 3354 2AC0.
        var codeBytes = new byte[] { 0x3C, 0xEE, 0x2E, 0x55, 0x33, 0x54, 0x2A, 0xC0 };
        Assert.Equal("機密情報", cmap.Decode(codeBytes));
    }

    [Fact]
    public void Decode_UnknownCode_UsesReplacementCharacter()
    {
        var cmap = ToUnicodeCMap.Parse(JapaneseCMap);
        Assert.Equal("�", cmap.Decode(new byte[] { 0x00, 0x01 }));
    }

    [Fact]
    public void Parse_BfChar_MapsIndividualCodes()
    {
        const string cmap = @"1 begincodespacerange
<0000><FFFF>
endcodespacerange
2 beginbfchar
<0041><0041>
<3042><3042>
endbfchar";
        var parsed = ToUnicodeCMap.Parse(cmap);
        // 0x0041 → 'A', 0x3042 → 'あ'.
        Assert.Equal("Aあ", parsed.Decode(new byte[] { 0x00, 0x41, 0x30, 0x42 }));
    }

    [Fact]
    public void Parse_BfRangeIncremental_IncrementsDestination()
    {
        const string cmap = @"1 begincodespacerange
<0000><FFFF>
endcodespacerange
1 beginbfrange
<0010><0012><0041>
endbfrange";
        var parsed = ToUnicodeCMap.Parse(cmap);
        // 0x10→A, 0x11→B, 0x12→C.
        Assert.Equal("ABC", parsed.Decode(new byte[] { 0x00, 0x10, 0x00, 0x11, 0x00, 0x12 }));
    }
}
