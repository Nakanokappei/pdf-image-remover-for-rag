using PdfImageRemoverForRag.Infrastructure.Internal;
using PdfSharp.Pdf.Content;
using Xunit;

namespace PdfImageRemoverForRag.Infrastructure.Tests;

// Overlap detection needs to know where a string sits on the page, which means
// running the text state machine (PDF §9.4). These tests drive it from raw
// content streams, so the expected rectangle is arithmetic rather than
// something a PDF writer decided.
//
// No /Font resources are supplied, so glyphs measure at the fallback half-em:
// a 12 pt string of N characters advances 6N points. The box runs from 0.25 of
// the font size below the baseline to 0.85 above it — 13.2 pt tall at 12 pt.
public class TextPositionTests
{
    const double FontSize = 12;
    const double HalfEm = FontSize / 2;                     // fallback advance per char
    const double Ascender = FontSize * 0.85;
    const double Descender = FontSize * 0.25;

    static List<ContentStreamWalker.TextHit> Scan(string content)
    {
        var sequence = ContentReader.ReadContent(System.Text.Encoding.ASCII.GetBytes(content));
        return ContentStreamWalker.FindTexts(sequence, new PdfTextDecoder(null), new PdfFontMetrics(null));
    }

    [Fact]
    public void TdPlacesTheRunAtTheGivenBaseline()
    {
        var hit = Assert.Single(Scan("BT /F1 12 Tf 100 700 Td (Hello) Tj ET"));

        Assert.Equal("Hello", hit.Value);
        Assert.Equal(100, hit.X, 3);
        Assert.Equal(700 - Descender, hit.Y, 3);
        Assert.Equal(5 * HalfEm, hit.Width, 3);
        Assert.Equal(Ascender + Descender, hit.Height, 3);
    }

    [Fact]
    public void TmPlacesTheRunAndSurvivesUntilChanged()
    {
        var hits = Scan("BT /F1 12 Tf 1 0 0 1 50 400 Tm (ab) Tj (cd) Tj ET");

        Assert.Equal(2, hits.Count);
        Assert.Equal(50, hits[0].X, 3);
        // The second run starts where the first one ended: the show operator
        // advances the text matrix.
        Assert.Equal(50 + (2 * HalfEm), hits[1].X, 3);
        Assert.Equal(400 - Descender, hits[1].Y, 3);
    }

    [Fact]
    public void TStarMovesDownByTheLeading()
    {
        var hits = Scan("BT /F1 12 Tf 14 TL 100 700 Td (one) Tj T* (two) Tj ET");

        Assert.Equal(2, hits.Count);
        Assert.Equal(700 - Descender, hits[0].Y, 3);
        Assert.Equal(686 - Descender, hits[1].Y, 3);
        // The new line starts back at the line matrix's X, not after "one".
        Assert.Equal(100, hits[1].X, 3);
    }

    [Fact]
    public void TDSetsTheLeadingItMovesBy()
    {
        var hits = Scan("BT /F1 12 Tf 100 700 Td 0 -20 TD (one) Tj T* (two) Tj ET");

        Assert.Equal(2, hits.Count);
        Assert.Equal(680 - Descender, hits[0].Y, 3);
        Assert.Equal(660 - Descender, hits[1].Y, 3);
    }

    [Fact]
    public void CmTransformsTheTextIntoPageSpace()
    {
        // The text is placed at (10, 10) inside a space translated by (200, 300).
        var hit = Assert.Single(Scan("q 1 0 0 1 200 300 cm BT /F1 12 Tf 10 10 Td (x) Tj ET Q"));

        Assert.Equal(210, hit.X, 3);
        Assert.Equal(310 - Descender, hit.Y, 3);
    }

    [Fact]
    public void ScaledTextIsMeasuredInPageUnits()
    {
        // A CTM scale of 2 doubles both the advance and the height.
        var hit = Assert.Single(Scan("q 2 0 0 2 0 0 cm BT /F1 12 Tf 0 100 Td (ab) Tj ET Q"));

        Assert.Equal(2 * 2 * HalfEm, hit.Width, 3);
        Assert.Equal(2 * (Ascender + Descender), hit.Height, 3);
        Assert.Equal(2 * (100 - Descender), hit.Y, 3);
    }

    [Fact]
    public void CharacterSpacingWidensTheRun()
    {
        var plain = Assert.Single(Scan("BT /F1 12 Tf 0 0 Td (abcd) Tj ET"));
        var spaced = Assert.Single(Scan("BT /F1 12 Tf 3 Tc 0 0 Td (abcd) Tj ET"));

        Assert.Equal(plain.Width + (4 * 3), spaced.Width, 3);
    }

    [Fact]
    public void HorizontalScalingWidensTheRunWithoutChangingItsHeight()
    {
        var hit = Assert.Single(Scan("BT /F1 12 Tf 200 Tz 0 100 Td (ab) Tj ET"));

        Assert.Equal(2 * 2 * HalfEm, hit.Width, 3);
        Assert.Equal(Ascender + Descender, hit.Height, 3);
    }

    [Fact]
    public void TJArrayCountsItsKerningNumbersInTheWidth()
    {
        // -1000 units of a 12 pt font is a full 12 pt gap between the pieces.
        var hit = Assert.Single(Scan("BT /F1 12 Tf 0 100 Td [(ab) -1000 (cd)] TJ ET"));

        Assert.Equal("abcd", hit.Value);
        Assert.Equal((4 * HalfEm) + FontSize, hit.Width, 3);
    }

    [Fact]
    public void TextRiseLiftsTheBox()
    {
        var hit = Assert.Single(Scan("BT /F1 12 Tf 5 Ts 0 100 Td (x) Tj ET"));

        Assert.Equal(105 - Descender, hit.Y, 3);
    }

    [Fact]
    public void EveryRunKeepsItsOperatorIndex()
    {
        // The index is what lets one instance be flattened while identical
        // strings elsewhere on the page stay.
        var hits = Scan("BT /F1 12 Tf 0 700 Td (same) Tj 0 -20 Td (same) Tj ET");

        Assert.Equal(2, hits.Count);
        Assert.NotEqual(hits[0].Index, hits[1].Index);
        Assert.All(hits, h => Assert.Equal("same", h.Value));
    }

    [Fact]
    public void QuoteOperatorMovesToTheNextLineBeforeShowing()
    {
        var hits = Scan("BT /F1 12 Tf 16 TL 100 700 Td (one) Tj (two) ' ET");

        Assert.Equal(2, hits.Count);
        Assert.Equal(684 - Descender, hits[1].Y, 3);
        Assert.Equal(100, hits[1].X, 3);
    }
}
