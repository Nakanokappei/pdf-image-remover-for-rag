using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace PdfImageRemoverForRag.App;

/// <summary>
/// Monochrome toolbar icons rendered from a Windows icon font (Segoe Fluent
/// Icons on Windows 11, Segoe MDL2 Assets on Windows 10). Single-color glyphs,
/// matching the Windows 11 command-bar look. The caller owns the returned
/// images and must dispose them on shutdown.
/// </summary>
internal static class ToolbarIcons
{
    /// <summary>
    /// Source bitmap size. Rendered at 48px (16 logical × 300%) so the icon is
    /// crisp at high DPI; the toolbar scales it down to its ImageScalingSize
    /// (LogicalToDeviceUnits(16)) for the current DPI.
    /// </summary>
    public const int IconSize = 48;

    // Icon-font glyphs (private use area, shared by Segoe MDL2 Assets and
    // Segoe Fluent Icons).
    const string GlyphOpen = "\uE8E5";       // OpenFile
    const string GlyphSave = "\uE74E";       // Save
    const string GlyphSelectAll = "\uE8B3";  // SelectAll
    const string GlyphClear = "\uE8E6";      // ClearSelection

    // Near-black, like Windows 11 command-bar glyphs on a light background.
    // Under a high-contrast theme the fixed near-black would vanish against a
    // dark background, so the theme's own text color is used instead. A theme
    // change re-renders the icons (MainForm.RefreshToolbarIcons) because the
    // color is baked into the bitmap at render time.
    static Color IconColor => SystemInformation.HighContrast
        ? SystemColors.ControlText
        : Color.FromArgb(0x3B, 0x3B, 0x3B);

    public static Image CreateOpenIcon() => CreateGlyphIcon(GlyphOpen);
    public static Image CreateSaveIcon() => CreateGlyphIcon(GlyphSave);
    public static Image CreateSelectAllIcon() => CreateGlyphIcon(GlyphSelectAll);
    public static Image CreateClearSelectionIcon() => CreateGlyphIcon(GlyphClear);

    static Image CreateGlyphIcon(string glyph)
    {
        var bitmap = new Bitmap(IconSize, IconSize);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        // Grayscale AA (not ClearType) renders cleanly onto a transparent bitmap.
        g.TextRenderingHint = TextRenderingHint.AntiAlias;
        // Render the glyph at (almost) the full 16px box so it reads as a
        // 16x16 icon; a hair under avoids clipping from font side bearings.
        using var font = ResolveIconFont(IconSize * 0.95f);
        using var brush = new SolidBrush(IconColor);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
        };
        g.DrawString(glyph, font, brush, new RectangleF(0, 0, IconSize, IconSize), format);
        return bitmap;
    }

    /// <summary>The Windows icon font (Segoe Fluent Icons / MDL2 Assets) at the
    /// given pixel size, for callers that draw icon-font glyphs directly (e.g. the
    /// sort chevron). The caller owns and must dispose the returned font.</summary>
    internal static Font ResolveIconFont(float emSizePixels)
    {
        // Prefer the Windows 11 font, then the Windows 10 one; both contain the
        // glyphs above. Constructing a Font with a missing family substitutes
        // another, so confirm the resolved name.
        foreach (var name in new[] { "Segoe Fluent Icons", "Segoe MDL2 Assets" })
        {
            var font = new Font(name, emSizePixels, GraphicsUnit.Pixel);
            if (string.Equals(font.Name, name, StringComparison.OrdinalIgnoreCase)) return font;
            font.Dispose();
        }
        return new Font("Segoe UI Symbol", emSizePixels, GraphicsUnit.Pixel);
    }
}
