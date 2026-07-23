using System.Drawing.Drawing2D;

namespace PdfImageRemoverForRag.App;

/// <summary>
/// A flat, Windows 11-style command-bar renderer for the toolbar: no borders or
/// gradients, and a soft rounded highlight under a button on hover (slightly
/// "raised") or press. Colors sampled to match the Windows 11 light command bar.
/// </summary>
internal sealed class Windows11ToolStripRenderer : ToolStripProfessionalRenderer
{
    static readonly Color HoverFill = Color.FromArgb(0xED, 0xED, 0xED);
    static readonly Color PressedFill = Color.FromArgb(0xDE, 0xDE, 0xDE);
    static readonly Color HoverBorder = Color.FromArgb(0xDD, 0xDD, 0xDD);
    const int CornerRadiusLogical = 6;
    const int InsetLogical = 2;

    public Windows11ToolStripRenderer()
    {
        RoundedEdges = false;
    }

    // Flat strip background (the ToolStrip's own BackColor), no gradient.
    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(e.ToolStrip.BackColor);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    // No outer border.
    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
    }

    protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
    {
        if (e.Item is not ToolStripButton button || !button.Enabled)
        {
            return;
        }
        bool pressed = button.Pressed;
        bool hover = button.Selected;
        if (!pressed && !hover)
        {
            return; // flat when idle
        }

        var g = e.Graphics;
        // Scale inset/radius to the toolbar's DPI (e.g. 300% on the VM).
        int deviceDpi = (e.ToolStrip ?? e.Item.Owner)?.DeviceDpi ?? 96;
        double scale = deviceDpi / 96.0;
        int inset = (int)Math.Round(InsetLogical * scale);
        int radius = (int)Math.Round(CornerRadiusLogical * scale);
        // Inset a little so the rounded highlight floats within the button.
        var rect = new Rectangle(inset, inset,
            e.Item.Width - inset * 2 - 1, e.Item.Height - inset * 2 - 1);
        var savedMode = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var path = RoundedRectangle(rect, radius))
        {
            if (SystemInformation.HighContrast)
            {
                // High contrast: the fixed light grays disappear against the
                // theme, and filling with the theme's Highlight would swallow
                // the glyph (rendered in ControlText). An outline in Highlight
                // marks hover/press without covering the glyph.
                using var highlight = new Pen(SystemColors.Highlight, (float)(pressed ? 2 * scale : scale));
                g.DrawPath(highlight, path);
            }
            else
            {
                using var fill = new SolidBrush(pressed ? PressedFill : HoverFill);
                g.FillPath(fill, path);
                using var border = new Pen(HoverBorder);
                g.DrawPath(border, path);
            }
        }
        g.SmoothingMode = savedMode;
    }

    static GraphicsPath RoundedRectangle(Rectangle rect, int radius)
    {
        int d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
