using PdfImageRemoverForRag.Core.Models;
using Xunit;

namespace PdfImageRemoverForRag.Core.Tests;

// RgbColor luminance drives the shape-thumbnail "dark background for light
// shapes" decision; verify the luma for key reference colors.
public class RgbColorTests
{
    [Fact]
    public void Luminance_White_IsBrighterThanLightGray()
    {
        var white = new RgbColor(255, 255, 255);
        var lightGray = new RgbColor(211, 211, 211);
        Assert.True(white.Luminance > lightGray.Luminance);
    }

    [Fact]
    public void Luminance_Black_IsZero()
    {
        Assert.Equal(0, new RgbColor(0, 0, 0).Luminance);
    }

    [Fact]
    public void Luminance_Blue_IsDarkerThanLightGray()
    {
        // Pure blue is dark (luma ≈ 29) → white background, not black.
        var blue = new RgbColor(0, 0, 255);
        var lightGray = new RgbColor(211, 211, 211);
        Assert.True(blue.Luminance < lightGray.Luminance);
    }
}
