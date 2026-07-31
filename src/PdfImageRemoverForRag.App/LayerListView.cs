using System.Drawing.Drawing2D;

namespace PdfImageRemoverForRag.App;

/// <summary>
/// What one line of <see cref="LayerListView"/> shows. The view holds none of
/// this: it asks for a row's visual while painting it, so the thumbnail cache
/// stays free to dispose anything that scrolls out of its window.
/// </summary>
/// <param name="IsGroup">A unit header rather than one of its objects.</param>
/// <param name="Subtitle">Where the unit is — file and page — under its title.</param>
/// <param name="TextContent">For text objects: the string, drawn rather than rasterized.</param>
/// <param name="IsThumbnailPending">
/// A picture is coming but is not here yet, so the row says so. False both when
/// one is already drawn and when none can ever exist — a format nothing here
/// can decode must not be left promising a thumbnail forever.
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
    Image? Thumbnail,
    string? TextContent,
    bool IsThumbnailPending,
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
///
/// **Accessibility.** Because the rows are painted rather than hosted, they do
/// not exist as controls for a screen reader or the keyboard, so both are added
/// back by hand — the same treatment <see cref="TileView"/> needed, and for the
/// same reason. <see cref="CreateAccessibilityInstance"/> publishes one child
/// per row, and the control is focusable so the arrow keys move a cursor and
/// Space ticks the row under it. This is MSAA, which NVDA and JAWS read;
/// Narrator wants UIA fragments, whose API surface is internal to WinForms in
/// .NET 8 and cannot be implemented from outside (see docs/known-limitations).
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
        // Selectable + TabStop make the list reachable by keyboard; without
        // them a Panel cannot take focus and this could only be used with a
        // mouse.
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
               | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
               | ControlStyles.Selectable, true);
        TabStop = true;
        BackColor = SystemColors.Window;
    }

    int Dip(int logical) => LogicalToDeviceUnits(logical);

    int Pitch => Dip(RowHeight);

    /// <summary>
    /// Replace the contents.
    /// </summary>
    /// <param name="startOver">
    /// True when the rows now describe something else — the cursor and the
    /// scroll position are meaningless and are dropped, because an index kept
    /// across such a rebuild would point at a different object. False for a
    /// change that keeps the same rows (an expand), where dropping them would
    /// throw away where the user is looking.
    /// </param>
    public void SetRowCount(int count, bool startOver)
    {
        _rowCount = Math.Max(0, count);
        _hoveredRow = -1;
        AutoScrollMinSize = new Size(0, _rowCount * Pitch);

        if (startOver)
        {
            _selectedRow = -1;
            AutoScrollPosition = Point.Empty;
        }
        else if (_selectedRow >= _rowCount)
        {
            _selectedRow = -1;
        }

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

    // Where the two clickable parts of a row sit. Painting and hit-testing both
    // ask these, so a click can never land beside the box it is aimed at —
    // which is what a second copy of the arithmetic would eventually cause, and
    // the compiler would never notice.

    /// <summary>Objects sit one level in from their unit.</summary>
    int RowIndent(bool isGroup) => Dip(RowInset) + (isGroup ? 0 : Dip(IndentWidth));

    Rectangle DisclosureRect(Rectangle bounds, bool isGroup) => new(
        bounds.Left + RowIndent(isGroup),
        bounds.Top + ((bounds.Height - Dip(DisclosureWidth)) / 2),
        Dip(DisclosureWidth), Dip(DisclosureWidth));

    Rectangle CheckBoxRect(Rectangle bounds, bool isGroup) => new(
        DisclosureRect(bounds, isGroup).Right + Dip(Gap),
        bounds.Top + ((bounds.Height - Dip(CheckBoxSize)) / 2),
        Dip(CheckBoxSize), Dip(CheckBoxSize));

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

        // Disclosure triangle, groups only.
        var disclosure = DisclosureRect(bounds, visual.IsGroup);
        if (visual.IsGroup) DrawDisclosure(g, disclosure, visual.IsExpanded, text);

        DrawCheckBox(g, CheckBoxRect(bounds, visual.IsGroup), visual.Check);

        // Thumbnail, objects only — a group is a folder, and in an image editor
        // a layer group shows no picture of its own either.
        int x = CheckBoxRect(bounds, visual.IsGroup).Right + Dip(Gap);
        if (!visual.IsGroup)
        {
            var box = new Rectangle(x, bounds.Top + ((bounds.Height - Dip(ThumbnailSize)) / 2),
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
        using (var line = new Pen(SystemColors.ControlLight))
        {
            g.DrawLine(line, bounds.Left, bounds.Bottom - 1, bounds.Right, bounds.Bottom - 1);
        }

        // Where the keyboard is. Drawn only while the list has focus, so a
        // mouse user is not shown a cursor they are not driving.
        if (selected && Focused)
        {
            ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(bounds, -Dip(2), -Dip(2)));
        }
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
        // A stroked chevron, not a filled triangle. Windows 11's own tree and
        // navigation controls draw one, and at this size a solid wedge is a blot
        // where a line reads as a direction. Down when open, right when closed —
        // the convention every file tree and layers panel keeps.
        float centreX = box.Left + (box.Width / 2f);
        float centreY = box.Top + (box.Height / 2f);
        // Long arm across the chevron's back, short arm to its point: the ratio
        // is what makes it a chevron rather than a corner.
        float across = box.Width * 0.30f;
        float depth = box.Height * 0.17f;
        var points = expanded
            ? new[]
            {
                new PointF(centreX - across, centreY - depth),
                new PointF(centreX, centreY + depth),
                new PointF(centreX + across, centreY - depth),
            }
            : new[]
            {
                new PointF(centreX - depth, centreY - across),
                new PointF(centreX + depth, centreY),
                new PointF(centreX - depth, centreY + across),
            };

        // Rounded cap and join, and a width tied to the box, so the mark keeps
        // its proportions at every DPI instead of thinning out at 200 %.
        using var pen = new Pen(color, Math.Max(1f, box.Width / 10f))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };
        var saved = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.DrawLines(pen, points);
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
        // Clicking has to bring the keyboard cursor with it, or Space would
        // afterwards tick a row nobody is looking at.
        Focus();

        int row = RowAt(e.Location);
        if (row < 0) return;
        SetFocusedRow(row);

        // The triangle and the checkbox are the only parts of a row that do
        // something other than select it, and they are hit-tested through the
        // very rectangles that placed them. Only the horizontal span is
        // compared: the glyphs are a third of the row's height, and demanding
        // the pointer land inside them vertically would shrink a target the
        // user is already aiming at by eye.
        var visual = _visualFor(row);
        var bounds = BoundsOf(row);
        var disclosure = DisclosureRect(bounds, visual.IsGroup);
        var checkBox = CheckBoxRect(bounds, visual.IsGroup);

        if (visual.IsGroup && Spans(e.X, disclosure))
        {
            ExpandToggled?.Invoke(row);
        }
        else if (Spans(e.X, checkBox))
        {
            ToggleRow(row);
        }
    }

    static bool Spans(int x, Rectangle box) => x >= box.Left && x < box.Right;

    // =======================================================================
    // Keyboard
    // =======================================================================

    /// <summary>Claim the arrow / paging / Space keys so they drive the rows.</summary>
    protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) switch
    {
        Keys.Left or Keys.Right or Keys.Up or Keys.Down
            or Keys.Home or Keys.End or Keys.PageUp or Keys.PageDown
            or Keys.Space => true,
        _ => base.IsInputKey(keyData),
    };

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        // Land the cursor on the first visible row the first time focus arrives.
        if (_selectedRow < 0 && _rowCount > 0) SetFocusedRow(VisibleRange().First);
        else Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        Invalidate();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_rowCount == 0) return;

        int row = _selectedRow < 0 ? VisibleRange().First : _selectedRow;
        int page = Math.Max(1, ClientSize.Height / Pitch);

        switch (e.KeyCode)
        {
            case Keys.Up: SetFocusedRow(row - 1); break;
            case Keys.Down: SetFocusedRow(row + 1); break;
            case Keys.Home: SetFocusedRow(0); break;
            case Keys.End: SetFocusedRow(_rowCount - 1); break;
            case Keys.PageUp: SetFocusedRow(row - page); break;
            case Keys.PageDown: SetFocusedRow(row + page); break;
            case Keys.Space: ToggleRow(row); break;
            // Left and Right fold a group, mirroring every tree control. On an
            // object row Left goes up to the group it belongs to, which is the
            // only "out" move this two-level list has.
            case Keys.Left: CollapseOrGoToGroup(row); break;
            case Keys.Right: ExpandGroup(row); break;
            default: return;
        }
        e.Handled = true;
    }

    void CollapseOrGoToGroup(int row)
    {
        var visual = _visualFor(row);
        if (visual.IsGroup)
        {
            if (visual.IsExpanded) ExpandToggled?.Invoke(row);
            return;
        }
        // Walk back to this object's group header.
        for (int i = row - 1; i >= 0; i--)
        {
            if (!_visualFor(i).IsGroup) continue;
            SetFocusedRow(i);
            return;
        }
    }

    void ExpandGroup(int row)
    {
        var visual = _visualFor(row);
        if (visual.IsGroup && !visual.IsExpanded) ExpandToggled?.Invoke(row);
    }

    /// <summary>
    /// Move the cursor: scroll it into view, repaint the rows that changed, and
    /// tell UI Automation the focus moved so a screen reader follows.
    /// </summary>
    internal void SetFocusedRow(int row)
    {
        if (_rowCount == 0) return;
        row = Math.Clamp(row, 0, _rowCount - 1);

        int old = _selectedRow;
        _selectedRow = row;
        EnsureVisible(row);
        // Only the two rows whose appearance changed; EnsureVisible already
        // invalidates the lot when it had to scroll.
        if (old >= 0 && old != row) Invalidate(BoundsOf(old));
        Invalidate(BoundsOf(row));
        if (old != row) RowSelected?.Invoke(row);

        // childID is 1-based here: 0 identifies the control itself, and the
        // framework maps an OS childID back to GetChild(childID - 1).
        AccessibilityNotifyClients(AccessibleEvents.Focus, row + 1);
    }

    void EnsureVisible(int row)
    {
        int top = row * Pitch;
        int viewTop = Math.Max(0, -AutoScrollPosition.Y);
        int viewBottom = viewTop + ClientSize.Height;

        if (top < viewTop) AutoScrollPosition = new Point(0, top);
        else if (top + Pitch > viewBottom) AutoScrollPosition = new Point(0, top + Pitch - ClientSize.Height);
        else return;

        Invalidate();
        ViewportChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Tick or clear the row's checkbox — the same path the mouse takes, so the
    /// two input routes can never drift apart.
    /// </summary>
    internal void ToggleRow(int row)
    {
        if (row < 0 || row >= _rowCount) return;

        CheckToggled?.Invoke(row);
        AccessibilityNotifyClients(AccessibleEvents.StateChange, row + 1);
    }

    // =======================================================================
    // Accessibility surface (read by LayerListAccessibleObject)
    // =======================================================================

    internal int RowCount => _rowCount;
    internal int FocusedRow => _selectedRow;
    internal Rectangle RowScreenBounds(int row) => RectangleToScreen(BoundsOf(row));
    internal int RowIndexAt(Point clientPoint) => RowAt(clientPoint);
    internal LayerVisual RowVisual(int row) => _visualFor(row);

    /// <summary>
    /// What a screen reader reads for a row: its name, then the file and page
    /// underneath it when there is one. No extra vocabulary — the role carries
    /// "check box" and the state carries ticked / part-ticked / expanded, each
    /// in the reader's own words.
    /// </summary>
    internal string RowAccessibleName(int row)
    {
        var visual = _visualFor(row);
        return visual.Subtitle is null ? visual.Title : $"{visual.Title}, {visual.Subtitle}";
    }

    protected override AccessibleObject CreateAccessibilityInstance()
        => new LayerListAccessibleObject(this);

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int row = RowAt(e.Location);
        if (row == _hoveredRow) return;

        int old = _hoveredRow;
        _hoveredRow = row;
        _toolTip.SetToolTip(this, row >= 0 ? ToolTipFor?.Invoke(row) ?? string.Empty : string.Empty);
        // Only the row losing the hover and the one gaining it. Repainting the
        // whole list on every pointer move re-derives every visible row's
        // visual, which is where the real cost of a repaint is.
        if (old >= 0) Invalidate(BoundsOf(old));
        if (row >= 0) Invalidate(BoundsOf(row));
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hoveredRow < 0) return;
        int old = _hoveredRow;
        _hoveredRow = -1;
        Invalidate(BoundsOf(old));
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
