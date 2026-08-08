using PdfImageRemoverForRag.Core.Models;
using PdfImageRemoverForRag.Infrastructure.Internal;
using PdfSharp.Pdf.Content;
using PdfSharp.Pdf.Content.Objects;
using Xunit;

namespace PdfImageRemoverForRag.Infrastructure.Tests;

// Flattening an overlap replaces one place on one page with pixels, so it must
// delete only the instances inside that place. The identical string on the next
// line, or the same rule drawn elsewhere, has to survive — which is the whole
// difference between this and ordinary removal.
public class RegionRemovalTests
{
    static CSequence Parse(string content) =>
        ContentReader.ReadContent(System.Text.Encoding.ASCII.GetBytes(content));

    static string Render(CSequence sequence)
    {
        var text = new System.Text.StringBuilder();
        foreach (var obj in sequence)
        {
            if (obj is COperator op) text.Append(op.OpCode.Name).Append(' ');
        }
        return text.ToString();
    }

    static int RemoveIn(CSequence sequence, OverlapRegion region, params string[] imageNames) =>
        ContentStreamWalker.RemoveInRegion(
            sequence, region, new HashSet<string>(imageNames),
            new PdfTextDecoder(null), new PdfFontMetrics(null)).Removed;

    static OverlapRegion Region(double x, double y, double w, double h, params OverlapMember[] members) =>
        new(1, x, y, w, h, members.Select(m =>
            new PlacedObject(m.Kind, m.Identity, x, y, w, h)).ToArray());

    static OverlapMember Text(string value) => new(RemovableKind.Text, value);
    static OverlapMember Shape(string signature) => new(RemovableKind.Shape, signature);
    static OverlapMember Image(string hash) => new(RemovableKind.Image, hash);

    [Fact]
    public void OnlyTheShowingInsideTheRegionIsRemoved()
    {
        // "same" is shown twice: once at y=700 (inside the region) and once at
        // y=400 (outside it).
        var sequence = Parse(
            "BT /F1 12 Tf 100 700 Td (same) Tj ET " +
            "BT /F1 12 Tf 100 400 Td (same) Tj ET");

        int removed = RemoveIn(sequence, Region(90, 690, 200, 30, Text("same")));

        Assert.Equal(1, removed);
        // One Tj left, and it is the one that was outside.
        var hits = ContentStreamWalker.FindTexts(
            sequence, new PdfTextDecoder(null), new PdfFontMetrics(null));
        var survivor = Assert.Single(hits);
        Assert.Equal(400 - (12 * 0.25), survivor.Y, 3);
    }

    [Fact]
    public void AStringNotNamedByTheRegionIsLeftAlone()
    {
        var sequence = Parse(
            "BT /F1 12 Tf 100 700 Td (target) Tj ET " +
            "BT /F1 12 Tf 100 700 Td (bystander) Tj ET");

        int removed = RemoveIn(sequence, Region(90, 690, 200, 30, Text("target")));

        Assert.Equal(1, removed);
        var hits = ContentStreamWalker.FindTexts(
            sequence, new PdfTextDecoder(null), new PdfFontMetrics(null));
        Assert.Equal("bystander", Assert.Single(hits).Value);
    }

    [Fact]
    public void OnlyTheRuleInsideTheRegionIsRemoved()
    {
        // The same rule (same signature: same shape, width and color) drawn at
        // two heights. Zero-height paths are exactly the case the minimum extent
        // exists for.
        var sequence = Parse(
            "0 0 0 RG 1 w 50 700 m 545 700 l S " +
            "0 0 0 RG 1 w 50 300 m 545 300 l S");
        var signature = ContentStreamWalker.FindShapes(sequence)[0].Signature;

        int removed = RemoveIn(sequence, Region(40, 690, 520, 20, Shape(signature)));

        Assert.Equal(1, removed);
        var remaining = Assert.Single(ContentStreamWalker.FindShapes(sequence));
        Assert.Equal(300, remaining.Y, 3);
    }

    [Fact]
    public void OnlyTheDrawCallInsideTheRegionIsRemoved()
    {
        // The same image drawn twice: inside the region and outside it.
        var sequence = Parse(
            "q 100 0 0 100 50 600 cm /Im1 Do Q " +
            "q 100 0 0 100 50 100 cm /Im1 Do Q");

        int removed = RemoveIn(
            sequence, Region(40, 590, 120, 120, Image("HASH")), "/Im1");

        Assert.Equal(1, removed);
        var remaining = Assert.Single(ContentStreamWalker.FindDrawCalls(sequence));
        Assert.Equal(100, remaining.Y, 3);
    }

    [Fact]
    public void AllThreeKindsInOneRegionGoTogether()
    {
        var sequence = Parse(
            "q 200 0 0 100 100 600 cm /Im1 Do Q " +
            "0 0 0 RG 1 w 100 620 m 300 620 l S " +
            "BT /F1 12 Tf 120 650 Td (label) Tj ET " +
            "BT /F1 12 Tf 120 200 Td (elsewhere) Tj ET");
        var signature = ContentStreamWalker.FindShapes(sequence)[0].Signature;

        int removed = RemoveIn(
            sequence,
            Region(100, 600, 200, 100, Image("HASH"), Shape(signature), Text("label")),
            "/Im1");

        Assert.Equal(3, removed);
        Assert.Empty(ContentStreamWalker.FindDrawCalls(sequence));
        Assert.Empty(ContentStreamWalker.FindShapes(sequence));
        var survivor = Assert.Single(ContentStreamWalker.FindTexts(
            sequence, new PdfTextDecoder(null), new PdfFontMetrics(null)));
        Assert.Equal("elsewhere", survivor.Value);
    }

    [Fact]
    public void RemovingSeveralInstancesKeepsTheRestOfTheStreamIntact()
    {
        // Deleting by index has to work back-to-front, or the second deletion
        // lands on the wrong operator. The surrounding operators must be
        // untouched.
        var sequence = Parse(
            "q 1 0 0 1 0 0 cm " +
            "BT /F1 12 Tf 100 700 Td (a) Tj ET " +
            "BT /F1 12 Tf 200 700 Td (a) Tj ET " +
            "BT /F1 12 Tf 300 700 Td (a) Tj ET " +
            "Q");

        int removed = RemoveIn(sequence, Region(90, 690, 400, 30, Text("a")));

        Assert.Equal(3, removed);
        Assert.Equal("q cm BT Tf Td ET BT Tf Td ET BT Tf Td ET Q ", Render(sequence));
    }

    [Fact]
    public void NothingMatchingMeansNothingRemoved()
    {
        var sequence = Parse("BT /F1 12 Tf 100 700 Td (text) Tj ET");
        var before = Render(sequence);

        int removed = RemoveIn(sequence, Region(0, 0, 10, 10, Text("text")));

        Assert.Equal(0, removed);
        Assert.Equal(before, Render(sequence));
    }
}
