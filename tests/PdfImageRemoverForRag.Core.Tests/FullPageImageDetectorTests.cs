using PdfImageRemoverForRag.Core.Grouping;
using PdfImageRemoverForRag.Core.Models;
using Xunit;

namespace PdfImageRemoverForRag.Core.Tests;

// Spec §24 "ページ全体画像候補判定" — verifies the 90 % threshold behaviour
// including the boundary and the "coverage in only one dimension" case.
public class FullPageImageDetectorTests
{
    static PdfImageOccurrence Occ(int page, double w, double h) =>
        new(page, "1 0 R", "/Im1", 0, 0, w, h);

    [Fact]
    public void CoveringMostOfBothDimensions_FlagsAsFullPage()
    {
        var d = new FullPageImageDetector(new[] { new PageDimensions(1, 595, 842) });
        Assert.True(d.IsPossibleFullPage(Occ(1, w: 590, h: 830)));
    }

    [Fact]
    public void ExactlyNinetyPercent_IsFullPage()
    {
        // The threshold is inclusive: 0.9 counts as full-page.
        var d = new FullPageImageDetector(new[] { new PageDimensions(1, 100, 100) });
        Assert.True(d.IsPossibleFullPage(Occ(1, w: 90, h: 90)));
    }

    [Fact]
    public void EightyNinePercent_IsNotFullPage()
    {
        var d = new FullPageImageDetector(new[] { new PageDimensions(1, 100, 100) });
        Assert.False(d.IsPossibleFullPage(Occ(1, w: 89, h: 89)));
    }

    [Fact]
    public void OnlyOneDimensionCovered_IsNotFullPage()
    {
        // A wide banner spanning the full width but only 20 % height is NOT
        // a full-page image — deleting it should not raise the scan warning.
        var d = new FullPageImageDetector(new[] { new PageDimensions(1, 100, 100) });
        Assert.False(d.IsPossibleFullPage(Occ(1, w: 100, h: 20)));
    }

    [Fact]
    public void UnknownPageNumber_IsNotFullPage()
    {
        // Safer to under-warn than over-warn when we don't know the page size.
        var d = new FullPageImageDetector(new[] { new PageDimensions(1, 100, 100) });
        Assert.False(d.IsPossibleFullPage(Occ(page: 99, w: 100, h: 100)));
    }

    [Fact]
    public void DuplicatePageDimensions_ThrowsAtConstruction()
    {
        // Fail loudly rather than silently pick one — indicates upstream bug.
        Assert.Throws<ArgumentException>(() =>
            new FullPageImageDetector(new[]
            {
                new PageDimensions(1, 100, 100),
                new PageDimensions(1, 200, 200),
            }));
    }
}
