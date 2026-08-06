using System.Drawing.Drawing2D;

namespace PdfImageRemoverForRag.App;

/// <summary>Whether a layer is drawn — and, for a folder, whether all of it is.</summary>
internal enum LayerVisibility
{
    Visible,
    Hidden,

    /// <summary>A folder holding some of each. Nothing else can be in this state.</summary>
    Mixed,
}

/// <summary>
/// What one line of <see cref="LayerListView"/> shows. The view holds none of
/// this: it asks for a row's visual while painting it, so the thumbnail cache
/// stays free to dispose anything that scrolls out of its window.
/// </summary>
/// <param name="IsGroup">A folder — a flatten unit — rather than one of its objects.</param>
/// <param name="Subtitle">Where the unit is — file and page — under its title.</param>
/// <param name="TextContent">For text objects: the string, drawn rather than rasterized.</param>
/// <param name="IsThumbnailPending">
/// A picture is coming but is not here yet, so the row says so. False both when
/// one is already drawn and when none can ever exist — a format nothing here
/// can decode must not be left promising a thumbnail forever.
/// </param>
/// <param name="Visibility">
/// Whether the layer is drawn. A folder answers for everything inside it, and
/// has a third answer when its objects disagree.
/// </param>
internal readonly record struct LayerVisual(
    bool IsGroup,
    string Title,
    string? Subtitle,
    Image? Thumbnail,
    string? TextContent,
    bool IsThumbnailPending,
    LayerVisibility Visibility,
    bool IsExpanded);

/// <summary>
/// The objects panel's list, laid out like an image editor's layers panel: a
/// flatten unit is a folder, the objects inside it sit under it, and each row
/// has an eye that says whether it is drawn.
///
/// The type is named for the LAYOUT it borrows, which is the only thing here
/// that is a layer. What the user reads says object throughout, because PDF's
/// own layers are optional content groups — a feature this application does not
/// touch — and the list on the other side of the window has always said object.
///
/// One scrolling control that paints its rows, for the reason set out on
/// <see cref="TileView"/>: a control per row is what broke that view on a real
/// document, and this list is fed from the same workspace.
///
/// Rows come in two heights — a folder is a line of text, an object needs room
/// for its thumbnail — so their tops are worked out once per rebuild and
/// hit-testing looks them up rather than dividing.
///
/// **Accessibility.** Because the rows are painted rather than hosted, they do
/// not exist as controls for a screen reader or the keyboard, so both are added
/// back by hand — the same treatment <see cref="TileView"/> needed, and for the
/// same reason. <see cref="CreateAccessibilityInstance"/> publishes one child
/// per row, and the control is focusable so the arrow keys move a cursor and
/// Space hides or shows what is selected. This is MSAA, which NVDA and JAWS
/// read; Narrator wants UIA fragments, whose API surface is internal to WinForms
/// in .NET 8 and cannot be implemented from outside (see docs/known-limitations).
/// </summary>
internal sealed class LayerListView : Panel
{
    // Logical (96-DPI) metrics — every use goes through Dip(). Nothing painted
    // by hand in this app may assume 96 DPI; at 200 % it would come out half
    // size, which is exactly what happened to the tile view once already.
    const int ObjectRowHeight = 44;
    const int GroupRowHeight = 26;
    const int RowInset = 6;
    const int IndentWidth = 18;
    const int DisclosureWidth = 14;
    const int EyeWidth = 18;
    const int FolderSize = 16;
    const int ThumbnailSize = 32;
    const int Gap = 6;

    readonly Func<int, LayerVisual> _visualFor;
    readonly Func<int, bool> _isGroupRow;
    int _rowCount;
    int _hoveredRow = -1;
    int _focusedRow = -1;

    // Where each row starts, with one extra entry for the total height. Two row
    // heights mean the position of a row is a running total rather than a
    // multiplication, and it is wanted on every paint, hit-test and scroll.
    int[] _rowTops = { 0 };

    // What the commands act on. A layers panel selects by clicking the row, the
    // way an image editor does; the eye is a separate control on the same line.
    readonly HashSet<int> _selected = new();

    // Where a Shift range starts. Kept apart from the focused row so that
    // Shift-clicking twice extends from the same place both times.
    int _selectionAnchor = -1;

    /// <summary>Raised when a row's eye is clicked, or Space is pressed on it.</summary>
    public event Action<int>? VisibilityToggled;

    /// <summary>Raised when a folder's chevron is clicked.</summary>
    public event Action<int>? ExpandToggled;

    /// <summary>Raised whenever the set of selected rows changes.</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>
    /// Raised whenever the visible span may have changed. The wheel does not
    /// raise Scroll, so both paths funnel through here and the thumbnail loader
    /// listens once.
    /// </summary>
    public event EventHandler? ViewportChanged;

    /// <summary>Supplies a row's tooltip, or null for none.</summary>
    public Func<int, string?>? ToolTipFor { get; set; }

    readonly ToolTip _toolTip = new();

    public LayerListView(Func<int, LayerVisual> visualFor, Func<int, bool> isGroupRow)
    {
        _visualFor = visualFor;
        // Asked separately from the visual because it decides the row's HEIGHT,
        // which every row needs at rebuild time — and building a visual fetches
        // a thumbnail, which the rows off screen must not pay for.
        _isGroupRow = isGroupRow;
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

    int HeightOf(int row) => Dip(_isGroupRow(row) ? GroupRowHeight : ObjectRowHeight);

    /// <summary>
    /// Replace the contents.
    /// </summary>
    /// <param name="startOver">
    /// True when the rows now describe something else — the cursor, the
    /// selection and the scroll position are meaningless and are dropped,
    /// because an index kept across such a rebuild would point at a different
    /// object. False for a change that keeps the same rows (an expand), where
    /// dropping them would throw away where the user is looking.
    /// </param>
    public void SetRowCount(int count, bool startOver)
    {
        _rowCount = Math.Max(0, count);
        _hoveredRow = -1;

        _rowTops = new int[_rowCount + 1];
        for (int row = 0; row < _rowCount; row++)
        {
            _rowTops[row + 1] = _rowTops[row] + HeightOf(row);
        }
        AutoScrollMinSize = new Size(0, _rowTops[_rowCount]);

        if (startOver)
        {
            _focusedRow = -1;
            _selectionAnchor = -1;
            _selected.Clear();
            AutoScrollPosition = Point.Empty;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            // Anything that no longer exists goes; what survives keeps its row.
            if (_focusedRow >= _rowCount) _focusedRow = -1;
            if (_selected.RemoveWhere(row => row >= _rowCount) > 0)
            {
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        Invalidate();
        ViewportChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Row index range currently on screen, for the thumbnail loader.</summary>
    public (int First, int Count) VisibleRange()
    {
        if (_rowCount == 0) return (0, 0);
        int scrolled = Math.Max(0, -AutoScrollPosition.Y);
        int first = RowAtOffset(scrolled);
        int last = RowAtOffset(scrolled + ClientSize.Height);
        // One extra row at each end so a partially visible row is included.
        first = Math.Max(0, first - 1);
        last = Math.Min(_rowCount - 1, last + 1);
        return (first, last - first + 1);
    }

    /// <summary>The row containing a distance down the whole list.</summary>
    int RowAtOffset(int offset)
    {
        int found = Array.BinarySearch(_rowTops, 0, _rowCount + 1, offset);
        if (found >= 0) return Math.Min(found, _rowCount - 1);
        return Math.Clamp(~found - 1, 0, _rowCount - 1);
    }

    Rectangle BoundsOf(int row) => new(
        0, _rowTops[row] + AutoScrollPosition.Y, ClientSize.Width, HeightOf(row));

    // Where the clickable parts of a row sit. Painting and hit-testing both ask
    // these, so a click can never land beside the thing it is aimed at — which
    // is what a second copy of the arithmetic would eventually cause, and the
    // compiler would never notice.

    /// <summary>The eye leads every row, folder and object alike.</summary>
    Rectangle EyeRect(Rectangle bounds) => new(
        bounds.Left + Dip(RowInset),
        bounds.Top + ((bounds.Height - Dip(EyeWidth)) / 2),
        Dip(EyeWidth), Dip(EyeWidth));

    /// <summary>Then the chevron, on folders only, immediately left of the folder.</summary>
    Rectangle DisclosureRect(Rectangle bounds) => new(
        EyeRect(bounds).Right + Dip(Gap),
        bounds.Top + ((bounds.Height - Dip(DisclosureWidth)) / 2),
        Dip(DisclosureWidth), Dip(DisclosureWidth));

    /// <summary>
    /// The picture column: a folder icon on a unit, the object's thumbnail on
    /// its members. Objects sit one level in from their folder.
    /// </summary>
    Rectangle IconRect(Rectangle bounds, bool isGroup)
    {
        int size = Dip(isGroup ? FolderSize : ThumbnailSize);
        int left = isGroup
            ? DisclosureRect(bounds).Right + Dip(Gap)
            : EyeRect(bounds).Right + Dip(Gap) + Dip(IndentWidth);
        return new Rectangle(left, bounds.Top + ((bounds.Height - size) / 2), size, size);
    }

    int RowAt(Point client)
    {
        int y = client.Y - AutoScrollPosition.Y;
        if (y < 0 || _rowCount == 0 || y >= _rowTops[_rowCount]) return -1;
        return RowAtOffset(y);
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
        bool selected = _selected.Contains(row);

        // Selected rows carry the highlight, as a layers panel does; a folder
        // otherwise gets a band so the grouping reads at a glance.
        var back = selected
            ? SystemColors.Highlight
            : visual.IsGroup ? SystemColors.ControlLight
            : row == _hoveredRow ? SystemColors.ControlLight
            : SystemColors.Window;
        using (var fill = new SolidBrush(back)) g.FillRectangle(fill, bounds);

        var text = selected ? SystemColors.HighlightText : SystemColors.WindowText;
        var muted = selected ? SystemColors.HighlightText : SystemColors.GrayText;

        DrawEye(g, EyeRect(bounds), visual.Visibility, text);

        var icon = IconRect(bounds, visual.IsGroup);
        if (visual.IsGroup)
        {
            DrawDisclosure(g, DisclosureRect(bounds), visual.IsExpanded, text);
            DrawGlyph(g, icon, visual.IsExpanded ? GlyphFolderOpen : GlyphFolder, text);
        }
        else
        {
            DrawThumbnail(g, icon, visual, muted);
        }

        // A hidden layer is greyed, the way an image editor greys one out: the
        // eye alone is a small mark to read a whole list by.
        if (visual.Visibility == LayerVisibility.Hidden && !selected) text = SystemColors.GrayText;

        int x = icon.Right + Dip(Gap);
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
        if (row == _focusedRow && Focused)
        {
            ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(bounds, -Dip(2), -Dip(2)));
        }
    }

    // The Windows icon font's own glyphs, which is what Explorer and every
    // Windows app draw. Hand-drawing them was tried first and did not read as an
    // eye at 16 pixels — the shape is not the hard part, the hinting is.
    const string GlyphShown = "";    // RedEye
    const string GlyphHidden = "";   // Hide — the same eye, struck through
    const string GlyphFolder = "";   // Folder
    const string GlyphFolderOpen = "";

    // One font per size, kept because a row's icons are drawn on every paint.
    Font? _iconFont;
    float _iconFontSize;

    Font IconFont(float pixels)
    {
        if (_iconFont is not null && Math.Abs(_iconFontSize - pixels) < 0.5f) return _iconFont;
        _iconFont?.Dispose();
        _iconFont = ToolbarIcons.ResolveIconFont(pixels);
        _iconFontSize = pixels;
        return _iconFont;
    }

    /// <summary>
    /// Draw an icon-font glyph centred in a box. Grayscale antialiasing rather
    /// than ClearType, the same choice <see cref="ToolbarIcons"/> makes: colour
    /// fringing on a glyph this small reads as a smudge.
    /// </summary>
    void DrawGlyph(Graphics g, Rectangle box, string glyph, Color color)
    {
        var savedText = g.TextRenderingHint;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
        try
        {
            using var brush = new SolidBrush(color);
            using var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
            };
            g.DrawString(glyph, IconFont(box.Height * 0.95f), brush, box, format);
        }
        finally
        {
            g.TextRenderingHint = savedText;
        }
    }

    /// <summary>
    /// The eye: shown, struck through when hidden, and greyed when a folder's
    /// layers disagree — which is a state the objects themselves never have.
    /// </summary>
    void DrawEye(Graphics g, Rectangle box, LayerVisibility visibility, Color color)
    {
        DrawGlyph(g, box,
            visibility == LayerVisibility.Hidden ? GlyphHidden : GlyphShown,
            visibility == LayerVisibility.Mixed ? Color.FromArgb(128, color) : color);
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
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
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
        // afterwards act on a row nobody is looking at.
        Focus();

        int row = RowAt(e.Location);
        if (row < 0) return;

        // The eye and the chevron are the only parts of a row that do something
        // other than select it, and they are hit-tested through the very
        // rectangles that placed them. Only the horizontal span is compared: the
        // glyphs are a third of the row's height, and demanding the pointer land
        // inside them vertically would shrink a target the user is already
        // aiming at by eye.
        var visual = _visualFor(row);
        var bounds = BoundsOf(row);

        if (Spans(e.X, EyeRect(bounds)))
        {
            SetFocusedRow(row);
            ToggleVisibility(row);
            return;
        }
        if (visual.IsGroup && Spans(e.X, DisclosureRect(bounds)))
        {
            SetFocusedRow(row);
            ExpandToggled?.Invoke(row);
            return;
        }

        SelectRow(row, ModifierKeys);
    }

    static bool Spans(int x, Rectangle box) => x >= box.Left && x < box.Right;

    // =======================================================================
    // Selection
    // =======================================================================

    /// <summary>The rows the commands act on, in list order.</summary>
    public IReadOnlyList<int> SelectedRows => _selected.OrderBy(row => row).ToArray();

    /// <summary>
    /// Take a row into the selection the way the modifier keys ask: Ctrl adds
    /// or removes one, Shift takes everything from the anchor, and a plain click
    /// replaces the lot. The rules an image editor's layers panel follows, so
    /// nobody has to learn them here.
    /// </summary>
    void SelectRow(int row, Keys modifiers)
    {
        if ((modifiers & Keys.Control) != 0)
        {
            if (!_selected.Add(row)) _selected.Remove(row);
            _selectionAnchor = row;
        }
        else if ((modifiers & Keys.Shift) != 0 && _selectionAnchor >= 0)
        {
            _selected.Clear();
            int from = Math.Min(_selectionAnchor, row), to = Math.Max(_selectionAnchor, row);
            for (int i = from; i <= to; i++) _selected.Add(i);
        }
        else
        {
            _selected.Clear();
            _selected.Add(row);
            _selectionAnchor = row;
        }

        SetFocusedRow(row);
        Invalidate();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Drop the selection without touching anything else.</summary>
    public void ClearSelection()
    {
        if (_selected.Count == 0) return;
        _selected.Clear();
        Invalidate();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

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
        if (_focusedRow < 0 && _rowCount > 0) SetFocusedRow(VisibleRange().First);
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

        int row = _focusedRow < 0 ? VisibleRange().First : _focusedRow;
        int page = Math.Max(1, ClientSize.Height / Dip(ObjectRowHeight));

        switch (e.KeyCode)
        {
            case Keys.Up: MoveTo(row - 1, e.Modifiers); break;
            case Keys.Down: MoveTo(row + 1, e.Modifiers); break;
            case Keys.Home: MoveTo(0, e.Modifiers); break;
            case Keys.End: MoveTo(_rowCount - 1, e.Modifiers); break;
            case Keys.PageUp: MoveTo(row - page, e.Modifiers); break;
            case Keys.PageDown: MoveTo(row + page, e.Modifiers); break;
            // Space is the eye, on everything selected: hiding six layers is a
            // thing a user does, and doing it one row at a time is not.
            case Keys.Space: ToggleVisibilityOfSelection(row); break;
            // Left and Right fold a folder, mirroring every tree control. On an
            // object row Left goes up to the folder it belongs to, which is the
            // only "out" move this two-level list has.
            case Keys.Left: CollapseOrGoToGroup(row); break;
            case Keys.Right: ExpandGroup(row); break;
            default: return;
        }
        e.Handled = true;
    }

    /// <summary>
    /// Move the cursor, taking the selection with it — or extending it, when
    /// Shift is down.
    /// </summary>
    void MoveTo(int row, Keys modifiers)
    {
        row = Math.Clamp(row, 0, _rowCount - 1);
        SelectRow(row, modifiers & Keys.Shift);
    }

    void ToggleVisibilityOfSelection(int row)
    {
        if (_selected.Count == 0)
        {
            ToggleVisibility(row);
            return;
        }
        foreach (int selected in SelectedRows) ToggleVisibility(selected);
    }

    void CollapseOrGoToGroup(int row)
    {
        var visual = _visualFor(row);
        if (visual.IsGroup)
        {
            if (visual.IsExpanded) ExpandToggled?.Invoke(row);
            return;
        }
        // Walk back to this object's folder.
        for (int i = row - 1; i >= 0; i--)
        {
            if (!_isGroupRow(i)) continue;
            SelectRow(i, Keys.None);
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

        int old = _focusedRow;
        _focusedRow = row;
        EnsureVisible(row);
        // Only the two rows whose appearance changed; EnsureVisible already
        // invalidates the lot when it had to scroll.
        if (old >= 0 && old != row) Invalidate(BoundsOf(old));
        Invalidate(BoundsOf(row));

        // childID is 1-based here: 0 identifies the control itself, and the
        // framework maps an OS childID back to GetChild(childID - 1).
        AccessibilityNotifyClients(AccessibleEvents.Focus, row + 1);
    }

    /// <summary>Put the cursor on a row and make it the whole selection.</summary>
    internal void SelectOnly(int row) => SelectRow(Math.Clamp(row, 0, _rowCount - 1), Keys.None);

    void EnsureVisible(int row)
    {
        int top = _rowTops[row];
        int height = HeightOf(row);
        int viewTop = Math.Max(0, -AutoScrollPosition.Y);
        int viewBottom = viewTop + ClientSize.Height;

        if (top < viewTop) AutoScrollPosition = new Point(0, top);
        else if (top + height > viewBottom) AutoScrollPosition = new Point(0, top + height - ClientSize.Height);
        else return;

        Invalidate();
        ViewportChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Hide or show the row's layer — the same path the mouse takes, so the two
    /// input routes can never drift apart.
    /// </summary>
    internal void ToggleVisibility(int row)
    {
        if (row < 0 || row >= _rowCount) return;

        VisibilityToggled?.Invoke(row);
        AccessibilityNotifyClients(AccessibleEvents.StateChange, row + 1);
    }

    // =======================================================================
    // Accessibility surface (read by LayerListAccessibleObject)
    // =======================================================================

    internal int RowCount => _rowCount;
    internal int FocusedRow => _focusedRow;
    internal bool IsRowSelected(int row) => _selected.Contains(row);
    internal Rectangle RowScreenBounds(int row) => RectangleToScreen(BoundsOf(row));
    internal int RowIndexAt(Point clientPoint) => RowAt(clientPoint);
    internal LayerVisual RowVisual(int row) => _visualFor(row);

    /// <summary>
    /// What a screen reader reads for a row: its name, then the file and page
    /// underneath it when there is one. No extra vocabulary — the role carries
    /// "check box" and the state carries shown / part-shown / expanded, each in
    /// the reader's own words.
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
        if (disposing)
        {
            _toolTip.Dispose();
            _iconFont?.Dispose();
        }
        base.Dispose(disposing);
    }
}
