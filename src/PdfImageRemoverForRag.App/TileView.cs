using PdfImageRemoverForRag.Core.Models;

namespace PdfImageRemoverForRag.App;

/// <summary>
/// The タイル形式 view: one scrolling control that paints the tiles itself.
///
/// It replaces a FlowLayoutPanel that held one child control per object. That
/// worked on the sample files and fell apart on a real one — 2,015 child
/// windows scrolled with bands of the previous frame left behind. The geometry
/// was never wrong (logged tile bounds matched the requested size exactly); the
/// window count was. A DataGridView does not make one control per row either,
/// and neither does this any more.
///
/// Bitmaps are asked for at paint time rather than held, so the cache is free
/// to dispose anything that scrolls out of its window without this view ever
/// referring to a disposed image.
///
/// **Accessibility.** Because the tiles are painted rather than hosted, they do
/// not exist as controls for a screen reader or the keyboard. Both are added
/// back by hand: <see cref="CreateAccessibilityInstance"/> exposes one UIA child
/// per tile (see <c>TileViewAccessibleObject</c>), and the control is made
/// focusable so the arrow keys move a focused-tile cursor and Space toggles it.
/// </summary>
internal sealed class TileView : Panel
{
    IReadOnlyList<CrossFileImageGroup> _items = Array.Empty<CrossFileImageGroup>();
    readonly Func<CrossFileImageGroup, TileVisual> _visualFor;
    int _hoveredIndex = -1;

    // The keyboard/accessibility cursor: which tile has focus. -1 when none.
    int _focusedTileIndex = -1;

    /// <summary>Raised when a tile is clicked, with the group it represents.</summary>
    public event EventHandler<CrossFileImageGroup>? TileToggled;

    /// <summary>
    /// Raised whenever the visible range may have changed. The wheel does not
    /// raise Scroll, so both paths funnel through here and the thumbnail loader
    /// only has to listen once.
    /// </summary>
    public event EventHandler? ViewportChanged;

    /// <summary>Supplies the tooltip for a group, or null for none.</summary>
    public Func<CrossFileImageGroup, string?>? ToolTipFor { get; set; }

    /// <summary>
    /// Supplies the screen-reader name for a group (type + usage, plus the
    /// string for text tiles). The checked / not-removable state is reported
    /// separately as UIA state flags, so it is not part of this text.
    /// </summary>
    public Func<CrossFileImageGroup, string>? AccessibleNameFor { get; set; }

    readonly ToolTip _toolTip = new();

    public TileView(Func<CrossFileImageGroup, TileVisual> visualFor)
    {
        _visualFor = visualFor;
        AutoScroll = true;
        // One control means DoubleBuffered actually applies to everything drawn
        // here — which was never true of the panel-of-controls it replaces.
        DoubleBuffered = true;
        // Selectable + TabStop make the view reachable by keyboard; without them
        // a Panel cannot take focus and the tile view could only be used with a
        // mouse.
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
               | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
               | ControlStyles.Selectable, true);
        TabStop = true;
        BackColor = SystemColors.Control;
    }

    int Dip(int logical) => LogicalToDeviceUnits(logical);

    int TilePitchX => Dip(TileMetrics.TileWidth) + (Dip(TileMetrics.TileMargin) * 2);
    int TilePitchY => Dip(TileMetrics.TileHeight) + (Dip(TileMetrics.TileMargin) * 2);

    /// <summary>How many tiles fit across the current width, at least one.</summary>
    int Columns => Math.Max(1,
        (ClientSize.Width - (Dip(TileMetrics.PanelPadding) * 2)) / Math.Max(1, TilePitchX));

    /// <summary>Replace the contents and scroll back to the top.</summary>
    public void SetItems(IReadOnlyList<CrossFileImageGroup> items)
    {
        _items = items;
        _hoveredIndex = -1;
        _focusedTileIndex = -1;
        UpdateScrollRange();
        // A rebuild replaces everything, so the old offset means nothing — and
        // when the new set is shorter it points past the end, which showed as
        // an empty panel.
        AutoScrollPosition = Point.Empty;
        Invalidate();
    }

    void UpdateScrollRange()
    {
        int rows = (_items.Count + Columns - 1) / Math.Max(1, Columns);
        AutoScrollMinSize = new Size(0, (rows * TilePitchY) + (Dip(TileMetrics.PanelPadding) * 2));
    }

    protected override void OnClientSizeChanged(EventArgs e)
    {
        base.OnClientSizeChanged(e);
        // The column count follows the width, so the virtual height does too.
        UpdateScrollRange();
        Invalidate();
    }

    /// <summary>Index range currently on screen, for the thumbnail loader.</summary>
    public (int First, int Count) VisibleRange()
    {
        if (_items.Count == 0) return (0, 0);

        int columns = Columns;
        int scrolled = Math.Max(0, -AutoScrollPosition.Y);
        int firstRow = scrolled / TilePitchY;
        // One extra row at each end so a partially visible tile is included.
        int rows = (ClientSize.Height / TilePitchY) + 2;
        return (firstRow * columns, rows * columns);
    }

    /// <summary>Where one tile sits, in client coordinates.</summary>
    Rectangle BoundsOf(int index)
    {
        int columns = Columns;
        int padding = Dip(TileMetrics.PanelPadding);
        int margin = Dip(TileMetrics.TileMargin);
        int column = index % columns;
        int row = index / columns;
        return new Rectangle(
            padding + margin + (column * TilePitchX) + AutoScrollPosition.X,
            padding + margin + (row * TilePitchY) + AutoScrollPosition.Y,
            Dip(TileMetrics.TileWidth), Dip(TileMetrics.TileHeight));
    }

    /// <summary>The tile under a client point, or -1.</summary>
    int IndexAt(Point point)
    {
        var (first, count) = VisibleRange();
        for (int i = first; i < Math.Min(_items.Count, first + count); i++)
        {
            if (BoundsOf(i).Contains(point)) return i;
        }
        return -1;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_items.Count == 0) return;

        // Only the tiles the clip region touches are drawn — the whole point of
        // this class. On a 2,000-object document that is a few dozen, not 2,000.
        var (first, count) = VisibleRange();
        for (int i = first; i < Math.Min(_items.Count, first + count); i++)
        {
            var bounds = BoundsOf(i);
            if (!bounds.IntersectsWith(e.ClipRectangle)) continue;
            TilePainter.Draw(e.Graphics, bounds, _visualFor(_items[i]), Font, Dip);

            // A keyboard user needs to see which tile is focused, or the arrow
            // keys move something invisible.
            if (i == _focusedTileIndex && Focused)
            {
                ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(bounds, -Dip(3), -Dip(3)));
            }
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left) return;

        int index = IndexAt(e.Location);
        if (index < 0) return;

        // A click also moves keyboard focus here, so the arrows continue from
        // the tile the user just clicked.
        Focus();
        SetFocusedTile(index);
        ToggleTile(index);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        // One tooltip for the whole view, re-pointed as the mouse moves; the
        // old design registered one per control, 2,015 of them.
        int index = IndexAt(e.Location);
        if (index == _hoveredIndex) return;

        _hoveredIndex = index;
        var text = index >= 0 ? ToolTipFor?.Invoke(_items[index]) : null;
        _toolTip.SetToolTip(this, text ?? string.Empty);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        // Scrolling moves the virtual origin, so everything has to repaint;
        // the base class only invalidates what it thinks was exposed.
        Invalidate();
        ViewportChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnScroll(ScrollEventArgs se)
    {
        base.OnScroll(se);
        Invalidate();
        ViewportChanged?.Invoke(this, EventArgs.Empty);
    }

    // --- keyboard ----------------------------------------------------------

    /// <summary>Claim the arrow / paging / Space keys so they drive the tiles.</summary>
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
        // Land the cursor on the first visible tile the first time focus arrives.
        if (_focusedTileIndex < 0 && _items.Count > 0) SetFocusedTile(FirstVisibleIndex());
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
        if (_items.Count == 0) return;

        int columns = Columns;
        int i = _focusedTileIndex < 0 ? FirstVisibleIndex() : _focusedTileIndex;
        int pageRows = Math.Max(1, ClientSize.Height / TilePitchY);

        switch (e.KeyCode)
        {
            case Keys.Left: SetFocusedTile(i - 1); break;
            case Keys.Right: SetFocusedTile(i + 1); break;
            case Keys.Up: SetFocusedTile(i - columns); break;
            case Keys.Down: SetFocusedTile(i + columns); break;
            case Keys.Home: SetFocusedTile(0); break;
            case Keys.End: SetFocusedTile(_items.Count - 1); break;
            case Keys.PageUp: SetFocusedTile(i - (columns * pageRows)); break;
            case Keys.PageDown: SetFocusedTile(i + (columns * pageRows)); break;
            case Keys.Space: ToggleTile(i); break;
            default: return;
        }
        e.Handled = true;
    }

    int FirstVisibleIndex()
    {
        var (first, _) = VisibleRange();
        return Math.Clamp(first, 0, Math.Max(0, _items.Count - 1));
    }

    /// <summary>
    /// Move the keyboard/accessibility cursor to a tile, scroll it into view,
    /// repaint the old and new tiles, and tell UIA the focus moved so a screen
    /// reader follows.
    /// </summary>
    internal void SetFocusedTile(int index)
    {
        if (_items.Count == 0) return;
        index = Math.Clamp(index, 0, _items.Count - 1);

        int old = _focusedTileIndex;
        _focusedTileIndex = index;
        EnsureVisible(index);
        if (old >= 0 && old != index) Invalidate(BoundsOf(old));
        Invalidate(BoundsOf(index));

        // childID is 1-based here (0 identifies the control itself), which is the
        // offset the framework's ControlAccessibleObject uses when it maps an OS
        // childID back to GetChild(childID - 1). VERIFY on Windows that Narrator
        // follows the cursor; if it lands on the wrong tile, this offset is why.
        AccessibilityNotifyClients(AccessibleEvents.Focus, index + 1);
    }

    void EnsureVisible(int index)
    {
        int row = index / Columns;
        int top = Dip(TileMetrics.PanelPadding) + (row * TilePitchY);
        int bottom = top + TilePitchY;
        int viewTop = -AutoScrollPosition.Y;
        int viewBottom = viewTop + ClientSize.Height;

        if (top < viewTop) AutoScrollPosition = new Point(0, top);
        else if (bottom > viewBottom) AutoScrollPosition = new Point(0, bottom - ClientSize.Height);

        Invalidate();
        ViewportChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Toggle a tile's removal state, unless it is not safely removable (in
    /// which case it is inert, exactly as the mouse path was).
    /// </summary>
    internal void ToggleTile(int index)
    {
        if (index < 0 || index >= _items.Count) return;
        var group = _items[index];
        if (!group.IsSafelyRemovable) return;

        TileToggled?.Invoke(this, group);
        Invalidate(BoundsOf(index));
        AccessibilityNotifyClients(AccessibleEvents.StateChange, index + 1);
    }

    // --- accessibility surface (read by TileViewAccessibleObject) ----------

    internal int TileCount => _items.Count;
    internal int FocusedTileIndex => _focusedTileIndex;
    internal Rectangle TileScreenBounds(int index) => RectangleToScreen(BoundsOf(index));
    internal int TileIndexAt(Point clientPoint) => IndexAt(clientPoint);
    internal bool TileIsRemovable(int index) => _items[index].IsSafelyRemovable;
    internal bool TileIsChecked(int index) => _visualFor(_items[index]).IsChecked;

    internal string TileAccessibleName(int index) =>
        AccessibleNameFor?.Invoke(_items[index]) ?? string.Empty;

    protected override AccessibleObject CreateAccessibilityInstance()
        => new TileViewAccessibleObject(this);

    protected override void Dispose(bool disposing)
    {
        if (disposing) _toolTip.Dispose();
        base.Dispose(disposing);
    }
}
