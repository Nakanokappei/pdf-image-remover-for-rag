using PdfImageRemoverForRag.Core.Models;

namespace PdfImageRemoverForRag.App;

/// <summary>
/// What one line of <see cref="LayerListView"/> shows. The view holds none of
/// this: it asks for a row's visual while painting it, so the thumbnail cache
/// stays free to dispose anything that scrolls out of its window.
/// </summary>
/// <param name="IsGroup">A unit header rather than one of its objects.</param>
/// <param name="Subtitle">Where the unit is — file and page — under its title.</param>
/// <param name="TextContent">For text objects: the string, drawn rather than rasterized.</param>
/// <param name="HasCheckBox">
/// False for anything that cannot be flattened, which is drawn without a box
/// rather than with a disabled one — there is no state to report, the operation
/// simply does not apply.
/// </param>
/// <param name="Check">
/// Three states, because a unit's box answers "is all of this being flattened"
/// and that has a third answer. <see cref="CheckState.Indeterminate"/> is drawn
/// as a dash: with only ticked and cleared to draw, a unit holding one ticked
/// object out of four looked exactly like a unit holding none.
/// </param>
internal readonly record struct LayerVisual(
    bool IsGroup,
    string Title,
    string? Subtitle,
    RemovableKind Kind,
    Image? Thumbnail,
    string? TextContent,
    bool IsThumbnailPending,
    bool HasCheckBox,
    CheckState Check,
    bool IsExpanded);

/// <summary>
/// The flatten panel's list, laid out like an image editor's layers panel: a
/// unit is a layer group, and the objects inside it are its layers, each with a
/// thumbnail, a name and a checkbox.
///
/// One scrolling control that paints its rows, for the reason set out on
/// <see cref="TileView"/>: a control per row is what broke that view on a real
/// document, and this list is fed from the same workspace.
///
/// Rows are a uniform height so hit-testing is arithmetic rather than a walk,
/// and only the visible span is ever painted.
/// </summary>
internal sealed class LayerListView : Panel
{
    // Logical (96-DPI) metrics — every use goes through Dip(). Nothing painted
    // by hand in this app may assume 96 DPI; at 200 % it would come out half
    // size, which is exactly what happened to the tile view once already.
    const int RowHeight = 44;
    const int RowInset = 6;
    const int IndentWidth = 18;
    const int DisclosureWidth = 14;
    const int CheckBoxSize = 16;
    const int ThumbnailSize = 32;
    const int Gap = 6;

    readonly Func<int, LayerVisual> _visualFor;
    int _rowCount;
    int _hoveredRow = -1;
    int _selectedRow = -1;

    /// <summary>Raised when a row's checkbox is clicked.</summary>
    public event Action<int>? CheckToggled;

    /// <summary>Raised when a group header's disclosure triangle is clicked.</summary>
    public event Action<int>? ExpandToggled;

    /// <summary>Raised when a row becomes the selected one.</summary>
    public event Action<int>? RowSelected;

    /// <summary>
    /// Raised whenever the visible span may have changed. The wheel does not
    /// raise Scroll, so both paths funnel through here and the thumbnail loader
    /// listens once.
    /// </summary>
    public event EventHandler? ViewportChanged;

    /// <summary>Supplies a row's tooltip, or null for none.</summary>
    public Func<int, string?>? ToolTipFor { get; set; }

    readonly ToolTip _toolTip = new();

    public LayerListView(Func<int, LayerVisual> visualFor)
    {
        _visualFor = visualFor;
        AutoScroll = true;
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
               | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = SystemColors.Window;
    }

    int Dip(int logical) => LogicalToDeviceUnits(logical);

    int Pitch => Dip(RowHeight);

    /// <summary>The row the user last landed on, or -1.</summary>
    public int SelectedRow => _selectedRow;

    /// <summary>
    /// Replace the contents. The selection does not survive: the rows it
    /// referred to are gone, and an index kept across a rebuild would point at
    /// a different object.
    /// </summary>
    public void SetRowCount(int count)
    {
        _rowCount = Math.Max(0, count);
        _hoveredRow = -1;
        _selectedRow = -1;
        AutoScrollMinSize = new Size(0, _rowCount * Pitch);
        AutoScrollPosition = Point.Empty;
        Invalidate();
        ViewportChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Rebuild after a change that keeps the same rows — a tick, an expand —
    /// without disturbing where the user is looking.
    /// </summary>
    public void RefreshRows(int count)
    {
        int keep = _selectedRow;
        _rowCount = Math.Max(0, count);
        AutoScrollMinSize = new Size(0, _rowCount * Pitch);
        _selectedRow = keep < _rowCount ? keep : -1;
        Invalidate();
        ViewportChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Row index range currently on screen, for the thumbnail loader.</summary>
    public (int First, int Count) VisibleRange()
    {
        if (_rowCount == 0) return (0, 0);
        int scrolled = Math.Max(0, -AutoScrollPosition.Y);
        int first = Math.Min(_rowCount - 1, scrolled / Pitch);
        // One extra row at each end so a partially visible row is included.
        int count = (ClientSize.Height / Pitch) + 2;
        return (first, Math.Min(count, _rowCount - first));
    }

    Rectangle BoundsOf(int row) =>
        new(0, (row * Pitch) + AutoScrollPosition.Y, ClientSize.Width, Pitch);

    int RowAt(Point client)
    {
        int y = client.Y - AutoScrollPosition.Y;
        if (y < 0) return -1;
        int row = y / Pitch;
        return row >= 0 && row < _rowCount ? row : -1;
    }

    // =======================================================================
    // Painting
    // =======================================================================

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_rowCount == 0) return;

        var (first, count) = VisibleRange();
        for (int row = first; row < first + count && row < _rowCount; row++)
        {
            PaintRow(e.Graphics, row, BoundsOf(row));
        }
    }

    void PaintRow(Graphics g, int row, Rectangle bounds)
    {
        var visual = _visualFor(row);
        bool selected = row == _selectedRow;

        // A group header gets a band so the grouping reads at a glance; an
        // object row sits on the panel background, indented under it.
        var back = selected
            ? SystemColors.Highlight
            : visual.IsGroup ? SystemColors.ControlLight
            : row == _hoveredRow ? SystemColors.ControlLight
            : SystemColors.Window;
        using (var fill = new SolidBrush(back)) g.FillRectangle(fill, bounds);

        var text = selected ? SystemColors.HighlightText : SystemColors.WindowText;
        var muted = selected ? SystemColors.HighlightText : SystemColors.GrayText;

        int x = bounds.Left + Dip(RowInset);
        int middle = bounds.Top + (bounds.Height / 2);

        // Objects sit one level in from their unit.
        if (!visual.IsGroup) x += Dip(IndentWidth);

        // Disclosure triangle, groups only.
        if (visual.IsGroup)
        {
            DrawDisclosure(g, new Rectangle(x, middle - (Dip(DisclosureWidth) / 2),
                Dip(DisclosureWidth), Dip(DisclosureWidth)), visual.IsExpanded, text);
        }
        x += Dip(DisclosureWidth) + Dip(Gap);

        // Checkbox, where the operation applies at all.
        if (visual.HasCheckBox)
        {
            DrawCheckBox(g, new Rectangle(x, middle - (Dip(CheckBoxSize) / 2),
                Dip(CheckBoxSize), Dip(CheckBoxSize)), visual.Check);
        }
        x += Dip(CheckBoxSize) + Dip(Gap);

        // Thumbnail, objects only — a group is a folder, and in an image editor
        // a layer group shows no picture of its own either.
        if (!visual.IsGroup)
        {
            var box = new Rectangle(x, middle - (Dip(ThumbnailSize) / 2),
                Dip(ThumbnailSize), Dip(ThumbnailSize));
            DrawThumbnail(g, box, visual, muted);
            x += Dip(ThumbnailSize) + Dip(Gap);
        }

        // Title, with the subtitle beneath it when there is one.
        var titleFont = visual.IsGroup ? new Font(Font, FontStyle.Bold) : Font;
        try
        {
            int width = Math.Max(Dip(20), bounds.Right - Dip(RowInset) - x);
            if (visual.Subtitle is null)
            {
                TextRenderer.DrawText(g, visual.Title, titleFont,
                    new Rectangle(x, bounds.Top, width, bounds.Height), text,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis
                    | TextFormatFlags.NoPrefix);
            }
            else
            {
                int half = bounds.Height / 2;
                TextRenderer.DrawText(g, visual.Title, titleFont,
                    new Rectangle(x, bounds.Top + Dip(2), width, half), text,
                    TextFormatFlags.Bottom | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(g, visual.Subtitle, Font,
                    new Rectangle(x, bounds.Top + half, width, half), muted,
                    TextFormatFlags.Top | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
        }
        finally
        {
            if (visual.IsGroup) titleFont.Dispose();
        }

        // Hairline between rows, so a run of objects reads as separate layers.
        using var line = new Pen(SystemColors.ControlLight);
        g.DrawLine(line, bounds.Left, bounds.Bottom - 1, bounds.Right, bounds.Bottom - 1);
    }

    /// <summary>
    /// Draw a checkbox in one of three states. <see cref="ControlPaint"/> knows
    /// ticked and cleared but has no mixed state, so the dash is drawn by hand
    /// over a cleared box — which also keeps all three the same size, since the
    /// themed renderer picks its own.
    /// </summary>
    void DrawCheckBox(Graphics g, Rectangle box, CheckState state)
    {
        ControlPaint.DrawCheckBox(g, box,
            ButtonState.Flat | (state == CheckState.Checked
                ? ButtonState.Checked
                : ButtonState.Normal));
        if (state != CheckState.Indeterminate) return;

        // A bar across the middle, inset from the border so it reads as a mark
        // inside the box rather than as a struck-through box.
        int inset = Math.Max(Dip(3), box.Width / 4);
        int thickness = Math.Max(Dip(2), box.Height / 6);
        var bar = new Rectangle(
            box.Left + inset,
            box.Top + ((box.Height - thickness) / 2),
            Math.Max(1, box.Width - (inset * 2)),
            thickness);
        // Theme colour, so the mark survives a high-contrast theme the same way
        // the tick drawn by ControlPaint does.
        using var brush = new SolidBrush(SystemColors.ControlText);
        g.FillRectangle(brush, bar);
    }

    static void DrawDisclosure(Graphics g, Rectangle box, bool expanded, Color color)
    {
        // Right-pointing when closed, down-pointing when open — the convention
        // every file tree and layers panel uses.
        var points = expanded
            ? new[]
            {
                new Point(box.Left, box.Top + (box.Height / 4)),
                new Point(box.Right, box.Top + (box.Height / 4)),
                new Point(box.Left + (box.Width / 2), box.Bottom - (box.Height / 4)),
            }
            : new[]
            {
                new Point(box.Left + (box.Width / 4), box.Top),
                new Point(box.Right - (box.Width / 4), box.Top + (box.Height / 2)),
                new Point(box.Left + (box.Width / 4), box.Bottom),
            };
        using var brush = new SolidBrush(color);
        var saved = g.SmoothingMode;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.FillPolygon(brush, points);
        g.SmoothingMode = saved;
    }

    void DrawThumbnail(Graphics g, Rectangle box, LayerVisual visual, Color muted)
    {
        using (var frame = new Pen(SystemColors.ControlDark))
        {
            g.DrawRectangle(frame, box.X, box.Y, box.Width - 1, box.Height - 1);
        }
        var inner = Rectangle.Inflate(box, -Dip(2), -Dip(2));

        // Text is drawn as text, never rasterized — the same rule the table and
        // the tiles follow, so the same object looks the same in all three.
        if (visual.TextContent is not null)
        {
            TextRenderer.DrawText(g, visual.TextContent, Font, inner, muted,
                TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis
                | TextFormatFlags.NoPrefix);
            return;
        }

        if (visual.Thumbnail is not null)
        {
            var saved = g.InterpolationMode;
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(visual.Thumbnail, FitInside(visual.Thumbnail.Size, inner));
            g.InterpolationMode = saved;
            return;
        }

        if (visual.IsThumbnailPending)
        {
            TextRenderer.DrawText(g, "…", Font, inner, muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    static Rectangle FitInside(Size image, Rectangle area)
    {
        double scale = Math.Min((double)area.Width / image.Width, (double)area.Height / image.Height);
        int w = Math.Max(1, (int)(image.Width * scale));
        int h = Math.Max(1, (int)(image.Height * scale));
        return new Rectangle(area.X + ((area.Width - w) / 2), area.Y + ((area.Height - h) / 2), w, h);
    }

    // =======================================================================
    // Mouse
    // =======================================================================

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        int row = RowAt(e.Location);
        if (row < 0) return;

        _selectedRow = row;
        Invalidate();

        var visual = _visualFor(row);
        var bounds = BoundsOf(row);
        int x = bounds.Left + Dip(RowInset) + (visual.IsGroup ? 0 : Dip(IndentWidth));

        // The triangle and the checkbox are the only parts of a row that do
        // something other than select it, so they are hit-tested by the same
        // arithmetic that placed them.
        if (visual.IsGroup && Between(e.X, x, x + Dip(DisclosureWidth)))
        {
            ExpandToggled?.Invoke(row);
            return;
        }
        x += Dip(DisclosureWidth) + Dip(Gap);
        if (visual.HasCheckBox && Between(e.X, x, x + Dip(CheckBoxSize)))
        {
            CheckToggled?.Invoke(row);
            return;
        }

        RowSelected?.Invoke(row);
    }

    static bool Between(int value, int low, int high) => value >= low && value < high;

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int row = RowAt(e.Location);
        if (row == _hoveredRow) return;

        _hoveredRow = row;
        _toolTip.SetToolTip(this, row >= 0 ? ToolTipFor?.Invoke(row) ?? string.Empty : string.Empty);
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hoveredRow < 0) return;
        _hoveredRow = -1;
        Invalidate();
    }

    // Both scrolling paths tell the thumbnail loader; the wheel does not raise
    // Scroll, and without this the rows that come into view stay blank.
    protected override void OnScroll(ScrollEventArgs se)
    {
        base.OnScroll(se);
        ViewportChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        ViewportChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnClientSizeChanged(EventArgs e)
    {
        base.OnClientSizeChanged(e);
        ViewportChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _toolTip.Dispose();
        base.Dispose(disposing);
    }
}
