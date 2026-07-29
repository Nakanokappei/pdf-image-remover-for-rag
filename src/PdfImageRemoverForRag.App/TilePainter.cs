using System.Drawing.Drawing2D;
using PdfImageRemoverForRag.Core.Models;

namespace PdfImageRemoverForRag.App;

/// <summary>
/// Everything one tile looks like, with nothing about where it lives.
/// </summary>
/// <param name="Kind">Image / Text / Shape — drawn as a small icon by the checkbox.</param>
/// <param name="Thumbnail">The picture, or null when there is none to show.</param>
/// <param name="TextContent">For text objects: the string, drawn rather than rasterized.</param>
/// <param name="IsThumbnailPending">The picture is still being produced; say so.</param>
/// <param name="IsChecked">Marked for removal — drawn pressed.</param>
/// <param name="IsCheckable">False for unsafe groups (§14.3) — drawn muted.</param>
/// <param name="UsageCount">Shown in the top-right badge.</param>
internal readonly record struct TileVisual(
    RemovableKind Kind,
    Image? Thumbnail,
    string? TextContent,
    bool IsThumbnailPending,
    bool IsChecked,
    bool IsCheckable,
    int UsageCount);

/// <summary>
/// Draws one tile of the タイル形式 view into a rectangle.
///
/// This is deliberately not a control. The view used to be a FlowLayoutPanel
/// holding one <c>ImageTile</c> control per object, and on a real document that
/// meant 2,015 child windows: measured, laid out and composited on every paint.
/// The layout was provably correct — logged tile bounds matched the requested
/// size to the pixel — yet scrolling left bands of the previous frame behind,
/// because compositing that many child windows is where the drawing gives up.
/// Neither `DoubleBuffered` nor `WS_EX_COMPOSITED` could fix that; only not
/// creating the windows could.
///
/// So the tiles are painted, not built. <see cref="TileView"/> owns the one
/// control and calls this for the handful of tiles actually on screen.
/// </summary>
internal static class TilePainter
{
    // Logical (96-DPI) metrics. The caller scales them — see ImageTile's note
    // on why nothing in this view may assume 96 DPI.
    const int ContentInset = 10;
    const int CheckBoxSize = 16;
    const int CheckBoxInset = 6;
    const int BadgeInset = 5;
    // The kind icon sits to the LEFT of the checkbox in the top-left corner,
    // so images and shapes (which both show a thumbnail) and blank text tiles
    // can still be told apart at a glance.
    const int KindIconSize = 16;
    const int KindIconGap = 4;

    // Under a high-contrast theme every fixed color painted here would risk
    // vanishing against the theme's background, so each meaningful color
    // defers to SystemColors when the theme is active (accessibility review
    // #4). ControlPaint's 3D border / checkbox already use system colors.
    static bool HighContrast => SystemInformation.HighContrast;

    public static void Draw(
        Graphics g, Rectangle bounds, TileVisual tile, Font baseFont, Func<int, int> dip)
    {
        // Background + pressed/raised border. Checked tiles get a highlight
        // wash plus a sunken 3D border so the "pressed button" metaphor reads
        // instantly; unchecked tiles look like raised buttons. High contrast
        // swaps the fixed pale-blue wash for the theme's Highlight.
        var background = tile.IsChecked
            ? (HighContrast ? SystemColors.Highlight : Color.FromArgb(208, 228, 252))
            : SystemColors.Window;
        using (var backgroundBrush = new SolidBrush(background))
        {
            g.FillRectangle(backgroundBrush, bounds);
        }
        ControlPaint.DrawBorder3D(g, bounds,
            tile.IsChecked ? Border3DStyle.Sunken : Border3DStyle.Raised);

        // Content centred in the padded area. A pressed tile shifts it down-right,
        // like a real button.
        var contentArea = Rectangle.Inflate(bounds, -dip(ContentInset), -dip(ContentInset));
        if (tile.IsChecked) contentArea.Offset(dip(1), dip(1));

        if (tile.TextContent is not null)
        {
            var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                      | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis;
            using var font = new Font(baseFont.FontFamily, 10f);
            // High contrast: theme text colors, matching the tile's Window /
            // Highlight background above.
            var textColor = HighContrast
                ? (tile.IsChecked ? SystemColors.HighlightText : SystemColors.WindowText)
                : Color.DimGray;
            TextRenderer.DrawText(g, tile.TextContent, font, contentArea, textColor, flags);
        }
        else if (tile.Thumbnail is not null)
        {
            var imageRect = FitInside(tile.Thumbnail.Size, contentArea);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(tile.Thumbnail, imageRect);
        }
        else if (tile.IsThumbnailPending)
        {
            // Say what is happening. A tile with no content at all looks like
            // the app failed to load the image.
            var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                      | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis;
            using var font = new Font(baseFont.FontFamily, 8.5f);
            // DimGray (#696969), not Gray (#808080): grey-on-white at #808080 is
            // 3.95:1, just under the 4.5:1 WCAG AA minimum for normal text;
            // #696969 is 5.49:1. High contrast uses the theme's disabled color.
            TextRenderer.DrawText(g, L10n.ThumbnailPending, font, contentArea,
                HighContrast ? SystemColors.GrayText : Color.DimGray, flags);
        }

        // Mute unsafe tiles with a translucent wash so they read as disabled
        // without hiding the thumbnail entirely.
        if (!tile.IsCheckable)
        {
            using var muteBrush = new SolidBrush(Color.FromArgb(170, SystemColors.Control));
            g.FillRectangle(muteBrush, bounds);
        }

        DrawKindIcon(g, bounds, tile, dip);
        DrawSelectionCheckBox(g, bounds, tile, dip);
        DrawUsageBadge(g, bounds, tile, baseFont, dip);
    }

    static void DrawKindIcon(
        Graphics g, Rectangle bounds, TileVisual tile, Func<int, int> dip)
    {
        // Kind marker in the top-left corner, to the left of the checkbox.
        int size = dip(KindIconSize);
        var box = new Rectangle(
            bounds.Left + dip(CheckBoxInset), bounds.Top + dip(CheckBoxInset), size, size);

        // Same translucent backing as the checkbox so the glyph stays legible
        // over a thumbnail.
        using (var backing = new SolidBrush(Color.FromArgb(210, SystemColors.Window)))
        {
            g.FillRectangle(backing, box.X - 1, box.Y - 1, size + 2, size + 2);
        }

        var color = HighContrast ? SystemColors.WindowText : Color.FromArgb(0x44, 0x44, 0x44);
        var saved = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        // Small inner box so nothing touches the backing's edge.
        var inner = Rectangle.Inflate(box, -dip(2), -dip(2));
        using var pen = new Pen(color, Math.Max(1f, dip(2) * 0.85f));
        using var brush = new SolidBrush(color);
        switch (tile.Kind)
        {
            case RemovableKind.Text:
                // Paragraph rules: four lines, the last one short. A capital "T"
                // was tried first and read as a table or a heading marker, while
                // the rules are the mark editors use for a block of text.
                float rulePitch = inner.Height / 3.4f;
                for (int rule = 0; rule < 4; rule++)
                {
                    float ruleY = inner.Top + (rule * rulePitch);
                    float ruleWidth = rule == 3 ? inner.Width * 0.55f : inner.Width;
                    g.DrawLine(pen, inner.Left, ruleY, inner.Left + ruleWidth, ruleY);
                }
                break;
            case RemovableKind.Shape:
                // A square overlapped by a circle. A lone triangle read as a
                // warning sign; three overlapping primitives turn into a blob at
                // 16 px, so two are drawn.
                g.DrawRectangle(pen, inner.Left, inner.Top,
                    inner.Width * 0.62f, inner.Height * 0.62f);
                g.DrawEllipse(pen,
                    inner.Left + (inner.Width * 0.34f), inner.Top + (inner.Height * 0.34f),
                    inner.Width * 0.64f, inner.Height * 0.64f);
                break;
            default:
                // A framed picture with a sun and a mountain ridge.
                g.DrawRectangle(pen, inner);
                int r = Math.Max(2, inner.Width / 5);
                g.FillEllipse(brush, inner.Left + inner.Width / 6, inner.Top + inner.Height / 6, r, r);
                g.DrawLines(pen, new[]
                {
                    new Point(inner.Left, inner.Bottom - inner.Height / 5),
                    new Point(inner.Left + inner.Width / 2, inner.Top + inner.Height / 2),
                    new Point(inner.Right, inner.Bottom - inner.Height / 5),
                });
                break;
        }
        g.SmoothingMode = saved;
    }

    static void DrawSelectionCheckBox(
        Graphics g, Rectangle bounds, TileVisual tile, Func<int, int> dip)
    {
        // Checkbox just to the right of the kind icon, mirroring the
        // pressed/checked state so the tile's selection reads at a glance (the
        // whole tile still toggles on click). Unsafe tiles show a grayed,
        // unchecked box to signal "can't remove".
        int glyphSize = dip(CheckBoxSize);
        int x = bounds.Left + dip(CheckBoxInset) + dip(KindIconSize) + dip(KindIconGap);
        var origin = new Point(x, bounds.Top + dip(CheckBoxInset));

        // A translucent backing keeps the box legible over both the window
        // background and the pressed highlight wash (theme Window color, so it
        // stays correct under high contrast).
        using (var backing = new SolidBrush(Color.FromArgb(210, SystemColors.Window)))
        {
            g.FillRectangle(backing, origin.X - 1, origin.Y - 1, glyphSize + 2, glyphSize + 2);
        }

        var state = !tile.IsCheckable
            ? ButtonState.Inactive
            : tile.IsChecked ? ButtonState.Checked : ButtonState.Normal;
        ControlPaint.DrawCheckBox(g, origin.X, origin.Y, glyphSize, glyphSize, state);
    }

    static void DrawUsageBadge(
        Graphics g, Rectangle bounds, TileVisual tile, Font baseFont, Func<int, int> dip)
    {
        // Top-right badge with the usage count — the one attribute this view
        // shows. Highlight color when pressed, gray otherwise. High contrast
        // inverts the theme's text/background pair so the badge stays a badge.
        var text = tile.UsageCount.ToString();
        using var font = new Font(baseFont.FontFamily, 8f, FontStyle.Bold);
        var textSize = g.MeasureString(text, font);
        var badge = new RectangleF(
            bounds.Right - textSize.Width - dip(14),
            bounds.Top + dip(BadgeInset),
            textSize.Width + dip(8),
            textSize.Height + dip(2));

        var badgeFill = tile.IsChecked
            ? SystemColors.Highlight
            : HighContrast ? SystemColors.WindowText : Color.FromArgb(96, 96, 96);
        var badgeText = HighContrast
            ? (tile.IsChecked ? SystemColors.HighlightText : SystemColors.Window)
            : Color.White;
        using var badgeBrush = new SolidBrush(badgeFill);
        using var badgeTextBrush = new SolidBrush(badgeText);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        FillRoundedRectangle(g, badgeBrush, badge, dip(7));
        g.DrawString(text, font, badgeTextBrush, badge.X + dip(4), badge.Y + dip(1));
    }

    static Rectangle FitInside(Size imageSize, Rectangle area)
    {
        // Scale-to-fit with aspect ratio; never upscale beyond 1:1.
        double scale = Math.Min(1.0, Math.Min(
            (double)area.Width / imageSize.Width,
            (double)area.Height / imageSize.Height));
        int w = Math.Max(1, (int)(imageSize.Width * scale));
        int h = Math.Max(1, (int)(imageSize.Height * scale));
        return new Rectangle(
            area.X + ((area.Width - w) / 2),
            area.Y + ((area.Height - h) / 2),
            w, h);
    }

    static void FillRoundedRectangle(Graphics g, Brush brush, RectangleF rect, int radius)
    {
        using var path = new GraphicsPath();
        path.AddArc(rect.X, rect.Y, radius, radius, 180, 90);
        path.AddArc(rect.Right - radius, rect.Y, radius, radius, 270, 90);
        path.AddArc(rect.Right - radius, rect.Bottom - radius, radius, radius, 0, 90);
        path.AddArc(rect.X, rect.Bottom - radius, radius, radius, 90, 90);
        path.CloseFigure();
        g.FillPath(brush, path);
    }
}
