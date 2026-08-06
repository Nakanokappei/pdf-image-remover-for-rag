using PdfImageRemoverForRag.Core.Grouping;
using PdfImageRemoverForRag.Core.Models;

namespace PdfImageRemoverForRag.App;

/// <summary>
/// What the host can say about one object in a unit: the object list row it
/// belongs to, its small bitmap when one is available, and whether a bitmap can
/// ever exist for it.
/// </summary>
/// <param name="CanEverRender">
/// False for a format nothing here can decode (JPX / CCITT / JBIG2). The panel
/// needs this to tell "still rendering" from "never will" — with only a
/// nullable bitmap it would have to guess, and guessing is how a view comes to
/// promise a thumbnail that is never coming.
/// </param>
internal readonly record struct LayerThumbnail(
    CrossFileImageGroup? Group,
    Image? Bitmap,
    bool CanEverRender);

/// <summary>
/// The objects panel, docked to the right of the object list: the flatten units
/// the object selected on the left takes part in, laid out the way an image
/// editor lays out layers, with a preview of the page underneath.
///
/// It answers one question — "where does this object overlap something, and
/// what would be baked with it" — about whatever the list has selected. That is
/// why it is not a tree of the whole document: the object list already is that
/// overview, and duplicating it beside itself gave the user two lists to read
/// and no reason to prefer either.
///
/// A unit is a folder and its objects sit under it. Each row has an eye that
/// says whether it is drawn, and closing one means what it means in an image
/// editor: that object does not appear in what gets written — here, at that one
/// place on that one page. Selection is separate from all that: a row is
/// selected by clicking it, and the commands act on what is selected.
///
/// **Nothing is held per row.** The list is a filtered view — moving the
/// selection on the left changes which units are shown — so state kept against
/// a row index would be lost the moment the user looked at another object. What
/// is hidden lives in the workspace, and the panel asks.
/// </summary>
internal sealed class FlattenPanel : UserControl
{
    /// <summary>One unit on one page of one file, as the panel lists it.</summary>
    sealed record UnitEntry(
        string FilePath, int DocumentNumber, OverlapRegion Region, int NumberOnPage)
    {
        public bool Expanded { get; set; } = true;
    }

    /// <summary>
    /// A line in the list: a unit header, or one of its objects. Kept as
    /// indices into <see cref="_units"/> so the rows stay cheap to rebuild on
    /// every expand.
    /// </summary>
    readonly record struct Row(int UnitIndex, PlacedObject? Object);

    // Every unit in the open workspace, and the subset currently listed.
    readonly List<UnitEntry> _units = new();
    readonly List<Row> _rows = new();

    CrossFileImageGroup? _selectedGroup;
    bool _anyDocuments;

    readonly LayerListView _list;
    // Title and a menu share a bar. The commands are in the menu rather than on
    // a row of their own: five buttons cannot share a line with a heading at any
    // width, and this panel is the one the user drags narrow.
    readonly Panel _titleBar = new() { Dock = DockStyle.Top };
    readonly Label _title = new()
    {
        Dock = DockStyle.Fill,
        AutoSize = false,
        Text = L10n.FlattenPanelHeader,
        TextAlign = ContentAlignment.MiddleLeft,
        UseMnemonic = false,
    };
    readonly Button _menuButton = new()
    {
        Dock = DockStyle.Right,
        Text = L10n.MenuGlyph,
        FlatStyle = FlatStyle.System,
        AutoSize = false,
        UseMnemonic = false,
    };
    readonly ContextMenuStrip _menu = new();

    // The commands, every one of them acting on the SELECTED rows and every one
    // taking effect at once: flatten replaces them with a picture of themselves,
    // undo takes such a picture back, merge gathers them into one unit and split
    // takes them out of the one they are in.
    readonly ToolStripMenuItem _flattenSelection = new(L10n.FlattenApply);
    readonly ToolStripMenuItem _undoFlatten = new(L10n.FlattenUndo);
    readonly ToolStripMenuItem _mergeSelection = new(L10n.FlattenMerge);
    readonly ToolStripMenuItem _splitSelection = new(L10n.FlattenSplit);
    readonly ToolStripMenuItem _clearSelection = new(L10n.ToolClearSelection);

    readonly Label _description = new()
    {
        Dock = DockStyle.Top,
        AutoSize = false,
        Text = L10n.FlattenDescription,
        UseMnemonic = false,
    };
    // Only ever visible when it has something to say: an always-present strip
    // of empty red would train the eye to skip it.
    readonly Label _wholePageWarning = new()
    {
        Dock = DockStyle.Top,
        AutoSize = false,
        Text = L10n.FlattenWholePageWarning,
        Visible = false,
        UseMnemonic = false,
    };
    readonly Label _emptyMessage = new()
    {
        Dock = DockStyle.Fill,
        Text = L10n.StatusOpenPrompt,
        TextAlign = ContentAlignment.MiddleCenter,
        ForeColor = SystemColors.GrayText,
        UseMnemonic = false,
    };
    // List above, page below. FixedPanel is the preview for the same reason the
    // workspace split fixes this whole panel: making the window taller should
    // feed the list, which is what the user is working down. It is also what
    // makes a remembered preview height mean anything — with the panels scaling
    // proportionally, every resize would quietly change the height that was
    // restored.
    readonly SplitContainer _split = new()
    {
        Dock = DockStyle.Fill,
        Orientation = Orientation.Horizontal,
        FixedPanel = FixedPanel.Panel2,
    };
    readonly PreviewPane _preview = new() { Dock = DockStyle.Fill };

    /// <summary>Raised whenever the set of selected rows changes.</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>
    /// Raised when the user asks for the selected objects to be flattened now.
    /// The panel does not do it: flattening writes a file, and the workspace is
    /// what owns files.
    /// </summary>
    public event EventHandler? FlattenRequested;

    /// <summary>Raised when the user asks to take back the flatten they are looking at.</summary>
    public event EventHandler? UndoFlattenRequested;

    /// <summary>
    /// Raised when an eye is clicked. Whether a layer is drawn is the same fact
    /// as whether a save keeps it, and that fact lives in the workspace beside
    /// the object list's own ticks — so the panel asks for the change rather
    /// than making it.
    /// </summary>
    public event EventHandler<VisibilityChangeEventArgs>? VisibilityChangeRequested;

    internal sealed class VisibilityChangeEventArgs : EventArgs
    {
        public VisibilityChangeEventArgs(
            string filePath, PageDimensions page, IReadOnlyList<PlacedObject> objects, bool hide)
        {
            FilePath = filePath;
            Page = page;
            Objects = objects;
            Hide = hide;
        }

        public string FilePath { get; }

        /// <summary>The page they are drawn on, size included — a region needs both.</summary>
        public PageDimensions Page { get; }

        public IReadOnlyList<PlacedObject> Objects { get; }

        /// <summary>True to stop drawing them, false to draw them again.</summary>
        public bool Hide { get; }
    }

    /// <summary>
    /// Raised when the rows on screen change, so the host can fetch the
    /// thumbnails they need. The panel never holds a bitmap itself.
    /// </summary>
    public event EventHandler? ViewportChanged;

    /// <summary>
    /// What the host knows about an object: its list row and its thumbnail.
    /// One call rather than two, so the panel cannot pair a group with a
    /// bitmap decided under different rules.
    /// </summary>
    public Func<PlacedObject, LayerThumbnail>? ThumbnailFor { get; set; }

    /// <summary>
    /// The file to render a page from: the document with the hidden layers
    /// actually taken out. Greying them on top of the page was tried first and
    /// was hard to read.
    /// </summary>
    public Func<string, Task<string>>? PreviewSourceFor { get; set; }

    /// <summary>
    /// Whether this drawing of this object, on this page of this file, is
    /// hidden. Answered by the host, because that is where the mark lives — and
    /// asked per PLACEMENT, because hiding a layer here hides the one layer, not
    /// every other showing of the same object.
    /// </summary>
    public Func<string, int, PlacedObject, bool>? IsHidden { get; set; }

    /// <summary>
    /// Whether the object selected in the list is a picture some flatten drew,
    /// which is the only thing the undo command can act on. Decided by the host
    /// — it is the workspace that knows what has been flattened.
    /// </summary>
    public bool CanUndoFlatten { get; set; }

    /// <summary>
    /// Height of the preview under the list, in LOGICAL pixels: set by the host
    /// from the saved layout before the handle exists, updated as the user drags
    /// the splitter, and read back at shutdown. Zero means the user has never
    /// chosen one, and the three-fifths default applies.
    ///
    /// It is kept here rather than read off <see cref="_split"/> on demand
    /// because a DPI change re-applies it: converting device pixels back at the
    /// NEW scale would move the splitter every time the window changed monitor.
    /// </summary>
    public int PreviewHeight { get; set; }

    // Set by SplitterMoving, which ONLY fires while the user is dragging, and
    // consumed by the SplitterMoved that ends the drag. SplitterMoved alone is
    // not a signal that anything was chosen: the layout engine raises it too,
    // repeatedly, while the panel is still being built.
    bool _splitterDragged;

    public FlattenPanel()
    {
        _list = new LayerListView(VisualForRow, IsGroupRow)
        {
            Dock = DockStyle.Fill,
            Visible = false,
        };
        _list.AccessibleName = L10n.FlattenPanelTitle;
        _list.AccessibleDescription = L10n.FlattenDescription;
        _list.VisibilityToggled += OnVisibilityToggled;
        _list.ExpandToggled += OnExpandToggled;
        _list.SelectionChanged += OnRowSelectionChanged;
        _list.ToolTipFor = ToolTipForRow;
        _list.ViewportChanged += (_, e) => ViewportChanged?.Invoke(this, e);

        _preview.AccessibleName = L10n.AccessibleFlattenPreview;
        _wholePageWarning.ForeColor = MainForm.WarningTextColour;
        _title.Font = new Font(Font, FontStyle.Bold);

        _menuButton.AccessibleName = $"{L10n.FlattenMenu} ({L10n.FlattenPanelTitle})";
        _menuButton.Click += (_, _) => _menu.Show(_menuButton, new Point(0, _menuButton.Height));
        _menu.Items.AddRange(new ToolStripItem[]
        {
            _flattenSelection, _undoFlatten, new ToolStripSeparator(),
            _mergeSelection, _splitSelection, new ToolStripSeparator(),
            _clearSelection,
        });
        // Decided as the menu opens rather than kept in step with every click:
        // there is one moment when it matters, and this way it cannot be stale.
        _menu.Opening += (_, _) => RefreshCommandState();
        _flattenSelection.Click += (_, _) => FlattenRequested?.Invoke(this, EventArgs.Empty);
        _undoFlatten.Click += (_, _) => UndoFlattenRequested?.Invoke(this, EventArgs.Empty);
        _mergeSelection.Click += (_, _) => EditUnits(merge: true);
        _splitSelection.Click += (_, _) => EditUnits(merge: false);
        _clearSelection.Click += (_, _) => ClearSelection();

        _titleBar.Controls.Add(_title);
        _titleBar.Controls.Add(_menuButton);

        var listSide = new Panel { Dock = DockStyle.Fill };
        listSide.Controls.Add(_list);
        listSide.Controls.Add(_emptyMessage);
        _split.Panel1.Controls.Add(listSide);
        _split.Panel2.Controls.Add(_preview);
        _split.SplitterMoving += (_, _) => _splitterDragged = true;
        _split.SplitterMoved += OnSplitterMoved;

        // Docked children claim their edge in reverse order of addition, so the
        // fill goes in first and the title bar ends up outermost.
        Controls.Add(_split);
        Controls.Add(_wholePageWarning);
        Controls.Add(_description);
        Controls.Add(_titleBar);
    }

    int Dip(int logical) => LogicalToDeviceUnits(logical);

    /// <summary>Device pixels back to logical ones — the inverse WinForms omits.</summary>
    int Undip(int device) => (int)Math.Round(device * 96.0 / DeviceDpi);

    int PreviewHeightInDevicePixels =>
        _split.Height - _split.SplitterDistance - _split.SplitterWidth;

    /// <summary>
    /// Remember where the user put the splitter, so it survives a DPI change now
    /// and a restart later. Only a move that began with
    /// <see cref="SplitContainer.SplitterMoving"/> counts — see the field.
    /// </summary>
    void OnSplitterMoved(object? sender, SplitterEventArgs e)
    {
        if (!_splitterDragged) return;
        _splitterDragged = false;
        PreviewHeight = Undip(PreviewHeightInDevicePixels);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyDpiDependentLayout();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        ApplyDpiDependentLayout();
    }

    void ApplyDpiDependentLayout()
    {
        _title.Padding = new Padding(Dip(8), 0, Dip(8), 0);
        // Square, so the glyph sits in the middle of it rather than in a slab.
        _menuButton.Width = Dip(30);
        _titleBar.Height = Dip(30);
        _titleBar.Padding = new Padding(0, Dip(3), Dip(6), Dip(3));
        _description.Padding = new Padding(Dip(8), 0, Dip(8), Dip(6));
        FitDescriptionHeight();
        _split.SplitterWidth = Math.Max(4, Dip(4));
        _split.Panel1MinSize = Dip(120);
        _split.Panel2MinSize = Dip(120);
        ApplyPreviewHeight();
    }

    /// <summary>
    /// Put the splitter where <see cref="PreviewHeight"/> asks for, or three
    /// fifths to the list when the user has never said — the list is the thing
    /// being operated, and the preview only has to be big enough to say where on
    /// the page you are. Re-applied whenever the DPI changes, so a remembered
    /// height means the same amount of picture at any scale.
    /// </summary>
    void ApplyPreviewHeight()
    {
        int available = _split.Height;
        if (available <= _split.Panel1MinSize + _split.Panel2MinSize + _split.SplitterWidth) return;

        int wantedListHeight = PreviewHeight > 0
            ? available - Dip(PreviewHeight) - _split.SplitterWidth
            : (int)(available * 0.6);

        _split.SplitterDistance = Math.Clamp(
            wantedListHeight,
            _split.Panel1MinSize,
            available - _split.Panel2MinSize - _split.SplitterWidth);
    }

    /// <summary>
    /// The description and the warning both wrap, and this panel is narrow and
    /// user-resizable, so their heights have to be re-measured whenever the
    /// width changes.
    /// </summary>
    void FitDescriptionHeight()
    {
        _description.Height = WrappedHeight(_description);
        if (_wholePageWarning.Visible) _wholePageWarning.Height = WrappedHeight(_wholePageWarning);
    }

    int WrappedHeight(Label label) =>
        TextRenderer.MeasureText(
            label.Text, label.Font,
            new Size(Math.Max(Dip(100), Width - Dip(16)), int.MaxValue),
            TextFormatFlags.WordBreak).Height + Dip(8);

    /// <summary>
    /// Warn when what is selected would turn a whole page into one picture. The
    /// region as DETECTED is not the test: what becomes a picture is the
    /// bounding box of what the user chose, and a region can be whole-page as
    /// found yet a corner of it once narrowed down.
    /// </summary>
    void RefreshWholePageWarning()
    {
        bool covers = false;
        foreach (var (unit, members) in SelectedByUnit())
        {
            if (OverlapDetector.CoversWholePage(
                    OverlapDetector.RegionCovering(unit.Region.Page, members)))
            {
                covers = true;
                break;
            }
        }
        if (covers == _wholePageWarning.Visible) return;
        _wholePageWarning.Visible = covers;
        if (covers) _wholePageWarning.Height = WrappedHeight(_wholePageWarning);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (IsHandleCreated) FitDescriptionHeight();
    }

    // =======================================================================
    // The workspace
    // =======================================================================

    /// <summary>
    /// Take the units out of a freshly analyzed workspace. The selection does
    /// not survive: the regions it referred to are gone, and carrying it over to
    /// a different set would flatten something nobody chose.
    /// </summary>
    public void SetDocuments(IReadOnlyList<PdfDocumentInfo> documents)
    {
        _units.Clear();
        _selectedGroup = null;
        _anyDocuments = documents.Count > 0;

        // Numbered in the order the files were opened, because the number has
        // to mean something the user can see: the first file they opened is 1.
        int documentNumber = 0;
        foreach (var document in documents)
        {
            documentNumber++;
            // Numbered within their page, so a unit's label matches what the
            // user is looking at rather than a running total across the file.
            foreach (var page in document.OverlapRegions.GroupBy(r => r.PageNumber).OrderBy(g => g.Key))
            {
                int number = 1;
                foreach (var region in page)
                {
                    _units.Add(new UnitEntry(
                        document.FilePath, documentNumber, region, number++));
                }
            }
        }

        // Files can be open with nothing in them to flatten. Say why, or the
        // panel staying empty on every selection reads as it being broken.
        _description.Text = _anyDocuments && _units.Count == 0
            ? L10n.FlattenNoOverlaps
            : L10n.FlattenDescription;
        if (IsHandleCreated) FitDescriptionHeight();

        // Emptied directly rather than through ShowFor: the workspace changed
        // under a selection that may compare equal to the new one, and that is
        // exactly the case ShowFor's guard is there to skip.
        _selectedGroup = null;
        ResetExpansion();
        RebuildRows(resetScroll: true);
        _preview.Clear();
        FocusPinnedUnit();
        RaiseSelectionChanged();
    }

    /// <summary>
    /// List the units the given object takes part in. Null — nothing selected
    /// on the left — empties the panel: there is no object to say anything
    /// about, and showing every unit here would just be the object list again.
    /// </summary>
    public void ShowFor(CrossFileImageGroup? group)
    {
        // The grid raises CurrentCellChanged for a sideways move too, and the
        // panel describes the ROW. Rebuilding on those would re-filter every
        // unit in the workspace and throw away the user's scroll position for
        // a list that is about to look identical.
        if (ReferenceEquals(_selectedGroup, group)) return;

        _selectedGroup = group;
        // Looking at another object ends the edit that was being shown.
        _pinnedUnits = Array.Empty<OverlapRegion>();
        ResetExpansion();
        RebuildRows(resetScroll: true);

        // Point the preview at the first unit so selecting on the left already
        // shows where on the page this is, without a second click.
        if (_rows.Count > 0)
        {
            ShowPreviewFor(_rows[0]);
        }
        else
        {
            _preview.Clear();
        }
    }

    /// <summary>
    /// Open the first folder and close the rest, every time the panel comes to
    /// describe something else. A layers panel that opens everything buries the
    /// second unit under the first one's objects; one that opens nothing makes
    /// the user click before there is anything to see.
    /// </summary>
    void ResetExpansion()
    {
        bool first = true;
        foreach (int index in ListedUnitIndices())
        {
            _units[index].Expanded = first;
            first = false;
        }
    }

    /// <summary>The units the panel lists, in workspace order.</summary>
    IEnumerable<int> ListedUnitIndices()
    {
        for (int i = 0; i < _units.Count; i++)
        {
            bool pinned = _pinnedUnits.Any(r => ReferenceEquals(r, _units[i].Region));
            if (!pinned && (_selectedGroup is null || !UnitContains(_units[i], _selectedGroup))) continue;
            yield return i;
        }
    }

    void RebuildRows(bool resetScroll)
    {
        _rows.Clear();
        foreach (int i in ListedUnitIndices())
        {
            _rows.Add(new Row(i, null));
            if (!_units[i].Expanded) continue;
            foreach (var member in _units[i].Region.Members) _rows.Add(new Row(i, member));
        }

        bool anyRows = _rows.Count > 0;
        _list.Visible = anyRows;
        _emptyMessage.Visible = !anyRows;
        _emptyMessage.Text = EmptyMessage();

        _list.SetRowCount(_rows.Count, startOver: resetScroll);
    }

    /// <summary>
    /// What the panel says when it has no rows. Three different silences, and
    /// only one of them is worth explaining: an object that overlaps nothing is
    /// the case where the user is entitled to wonder what went wrong.
    /// </summary>
    string EmptyMessage()
    {
        if (!_anyDocuments) return L10n.StatusOpenPrompt;
        if (_selectedGroup is null) return string.Empty;
        return L10n.FlattenObjectNotOverlapping;
    }

    /// <summary>
    /// Whether a unit has a member that IS the selected list row. The rule for
    /// what "is" means lives on the group itself, so this side and the host's
    /// object-to-row lookup can never disagree about identity.
    /// </summary>
    static bool UnitContains(UnitEntry unit, CrossFileImageGroup group) =>
        unit.Region.Members.Any(group.Matches);

    // =======================================================================
    // Rows
    // =======================================================================

    bool IsGroupRow(int index) =>
        index >= 0 && index < _rows.Count && _rows[index].Object is null;

    LayerVisual VisualForRow(int index)
    {
        if (index < 0 || index >= _rows.Count) return default;
        var row = _rows[index];
        var unit = _units[row.UnitIndex];

        if (row.Object is null)
        {
            // The folder's eye answers for everything inside it, and has a third
            // answer when its objects disagree.
            int hidden = unit.Region.Members.Count(m => Hidden(unit, m));
            return new LayerVisual(
                IsGroup: true,
                Title: L10n.FlattenUnitLabel(
                        unit.DocumentNumber, unit.Region.PageNumber, unit.NumberOnPage)
                    + $"  ({KindSummary(unit.Region)})",
                // No subtitle: a folder is one line tall, and a second line
                // inside it would be two half-height ones. The file it is in is
                // in the tooltip, where a panel showing one file at a time can
                // afford to keep it.
                Subtitle: null,
                Thumbnail: null,
                TextContent: null,
                IsThumbnailPending: false,
                Visibility: hidden == 0 ? LayerVisibility.Visible
                    : hidden == unit.Region.Members.Count ? LayerVisibility.Hidden
                    : LayerVisibility.Mixed,
                IsExpanded: unit.Expanded);
        }

        var member = row.Object;
        // Text draws its string rather than a bitmap, the same rule the table
        // and the tiles follow — so one object looks the same in all three, and
        // so a text row never pays for a lookup whose answer it discards.
        string? text = ImageListRow.ThumbnailText(member.Kind, member.Identity);
        var thumbnail = text is null ? ThumbnailFor?.Invoke(member) ?? default : default;

        return new LayerVisual(
            IsGroup: false,
            Title: ObjectLabel(member),
            Subtitle: null,
            Thumbnail: thumbnail.Bitmap,
            TextContent: text,
            IsThumbnailPending: text is null && thumbnail.Bitmap is null && thumbnail.CanEverRender,
            Visibility: Hidden(unit, member) ? LayerVisibility.Hidden : LayerVisibility.Visible,
            IsExpanded: false);
    }

    bool Hidden(UnitEntry unit, PlacedObject member) =>
        IsHidden?.Invoke(unit.FilePath, unit.Region.PageNumber, member) == true;

    string? ToolTipForRow(int index)
    {
        if (index < 0 || index >= _rows.Count) return null;
        var row = _rows[index];
        if (row.Object is null) return _units[row.UnitIndex].FilePath;
        return row.Object.Kind == RemovableKind.Text ? row.Object.Identity : ObjectLabel(row.Object);
    }

    /// <summary>The kinds in the unit, in list order, e.g. "image + text".</summary>
    static string KindSummary(OverlapRegion region) => string.Join(" + ", region.Members
        .Select(m => m.Kind)
        .Distinct()
        .OrderBy(k => k)
        .Select(KindLabel));

    static string KindLabel(RemovableKind kind) => ImageListRow.TypeLabel(kind);

    /// <summary>
    /// An object's name: its kind, then what identifies it to a person. For text
    /// that is the string itself — quoted, so a run of spaces reads as content
    /// rather than as a missing label — and for the other kinds the size, since
    /// one image looks like another in a list of words.
    /// </summary>
    static string ObjectLabel(PlacedObject member)
    {
        if (member.Kind == RemovableKind.Text)
        {
            const int maxShown = 40;
            var value = member.Identity;
            if (value.Length > maxShown) value = value[..maxShown] + "…";
            return $"{KindLabel(member.Kind)}  \"{value}\"";
        }
        return $"{KindLabel(member.Kind)}  "
             + L10n.ShapeSize((int)Math.Round(member.Width), (int)Math.Round(member.Height));
    }

    // =======================================================================
    // The eye
    // =======================================================================

    /// <summary>
    /// Hide or show what the row stands for. A folder takes everything inside
    /// it: its eye reads as one switch, so it has to act as one.
    /// </summary>
    void OnVisibilityToggled(int index)
    {
        if (index < 0 || index >= _rows.Count) return;
        var row = _rows[index];
        var unit = _units[row.UnitIndex];

        var objects = row.Object is null
            ? unit.Region.Members
            : new[] { row.Object };
        // A folder that is partly hidden goes fully hidden first: one more press
        // brings it all back. The alternative — inverting each object — makes a
        // single click do two opposite things at once.
        bool hide = row.Object is null
            ? unit.Region.Members.Any(m => !Hidden(unit, m))
            : !Hidden(unit, row.Object);

        VisibilityChangeRequested?.Invoke(this, new VisibilityChangeEventArgs(
            unit.FilePath, unit.Region.Page, objects, hide));
    }

    void OnExpandToggled(int index)
    {
        if (index < 0 || index >= _rows.Count) return;
        var unit = _units[_rows[index].UnitIndex];
        unit.Expanded = !unit.Expanded;
        RebuildRows(resetScroll: false);
    }

    /// <summary>
    /// Repaint: what is hidden has changed under the rows, and under the page
    /// preview, where it is what the greying answers.
    /// </summary>
    public void RefreshVisibility()
    {
        _list.Invalidate();
        ShowPreviewForSelection();
    }

    // =======================================================================
    // Selection
    // =======================================================================

    void OnRowSelectionChanged(object? sender, EventArgs e)
    {
        ShowPreviewForSelection();
        RaiseSelectionChanged();
    }

    /// <summary>Drop the selection. The panel's own command, not the toolbar's.</summary>
    public void ClearSelection() => _list.ClearSelection();

    /// <summary>How many objects the selection covers, for the status line.</summary>
    public int SelectedObjectCount => SelectedByUnit().Sum(x => x.Members.Count);

    /// <summary>
    /// The selected objects, gathered by the unit they sit in. A selected folder
    /// stands for everything inside it, which is what makes "select the folder,
    /// press Flatten" mean what it looks like.
    /// </summary>
    IReadOnlyList<(UnitEntry Unit, IReadOnlyList<PlacedObject> Members)> SelectedByUnit()
    {
        var byUnit = new List<(UnitEntry Unit, List<PlacedObject> Members)>();
        foreach (int index in _list.SelectedRows)
        {
            if (index >= _rows.Count) continue;
            var row = _rows[index];
            var unit = _units[row.UnitIndex];

            var slot = byUnit.FirstOrDefault(x => ReferenceEquals(x.Unit, unit));
            if (slot.Unit is null)
            {
                slot = (unit, new List<PlacedObject>());
                byUnit.Add(slot);
            }

            // In the region's own member order, so a covering rectangle is built
            // from the same sequence the analyzer produced — and so a folder and
            // one of its objects both being selected cannot list it twice.
            var wanted = row.Object is null ? unit.Region.Members : new[] { row.Object };
            foreach (var member in unit.Region.Members)
            {
                if (wanted.Contains(member) && !slot.Members.Contains(member))
                {
                    slot.Members.Add(member);
                }
            }
        }
        return byUnit
            .Where(x => x.Members.Count > 0)
            .Select(x => (x.Unit, (IReadOnlyList<PlacedObject>)x.Members))
            .ToArray();
    }

    /// <summary>
    /// The places to combine, per source file — each covering the selected
    /// layers of one unit that are actually SHOWN. A hidden layer is one the
    /// save is going to take out, so baking it into the picture would put it
    /// back as pixels; it is left out, and the save removes it as asked.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<OverlapRegion>> SelectedRegionsByFile()
    {
        var byFile = new Dictionary<string, List<OverlapRegion>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (unit, members) in SelectedByUnit())
        {
            var shown = members.Where(m => !Hidden(unit, m)).ToArray();
            if (shown.Length == 0) continue;

            if (!byFile.TryGetValue(unit.FilePath, out var regions))
            {
                regions = new List<OverlapRegion>();
                byFile[unit.FilePath] = regions;
            }
            regions.Add(OverlapDetector.RegionCovering(unit.Region.Page, shown));
        }
        return byFile.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<OverlapRegion>)kv.Value,
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// What merging and splitting act on: the units of the one page the
    /// selection is in, and the selected objects. A selection spread over two
    /// pages or two files answers an empty scope, which disables both commands.
    ///
    /// ONE PAGE, and that is not only about what a merge may join. A member
    /// carries no page number and compares by value, so a footer printed at the
    /// same spot on twenty pages is twenty EQUAL objects: handed every unit in
    /// the file, "which units hold this selection" answered twenty, and both
    /// commands stayed dead on every document with a running header.
    /// </summary>
    (IReadOnlyList<OverlapRegion> Units, IReadOnlyList<PlacedObject> Selection) EditingScope()
    {
        var selected = SelectedByUnit();
        if (selected.Count == 0) return (Array.Empty<OverlapRegion>(), Array.Empty<PlacedObject>());

        var filePath = selected[0].Unit.FilePath;
        int pageNumber = selected[0].Unit.Region.PageNumber;
        if (selected.Any(x => !string.Equals(x.Unit.FilePath, filePath, StringComparison.OrdinalIgnoreCase)
                              || x.Unit.Region.PageNumber != pageNumber))
        {
            return (Array.Empty<OverlapRegion>(), Array.Empty<PlacedObject>());
        }

        // Every unit of that page, not only the selected ones: a merge has to
        // see the units it is taking objects out of.
        var units = _units
            .Where(u => string.Equals(u.FilePath, filePath, StringComparison.OrdinalIgnoreCase)
                        && u.Region.PageNumber == pageNumber)
            .Select(u => u.Region)
            .ToArray();
        return (units, selected.SelectMany(x => x.Members).ToArray());
    }

    /// <summary>
    /// Merge or split, then hand the new unit list back.
    /// </summary>
    void EditUnits(bool merge)
    {
        var (units, selection) = EditingScope();
        if (units.Count == 0) return;

        var edited = merge
            ? FlattenUnitEditing.Merge(units, selection)
            : FlattenUnitEditing.Split(units, selection);
        if (ReferenceEquals(edited, units)) return;

        // The unit an edit just made holds the objects that were selected, which
        // are not necessarily the object selected in the list on the left — and
        // the panel lists only the units of THAT object. Without pinning, a
        // merge answered by making its own result disappear.
        _pinnedUnits = edited
            .Where(r => !units.Any(u => ReferenceEquals(u, r)))
            .ToArray();

        var filePath = _units
            .First(u => units.Contains(u.Region))
            .FilePath;
        UnitsEdited?.Invoke(this, new UnitsEditedEventArgs(filePath, edited));
    }

    /// <summary>
    /// Raised when the user merges or splits units by hand. The panel does not
    /// own the workspace, so it cannot store the correction — it hands it to
    /// whoever does.
    /// </summary>
    public event EventHandler<UnitsEditedEventArgs>? UnitsEdited;

    internal sealed class UnitsEditedEventArgs : EventArgs
    {
        public UnitsEditedEventArgs(string filePath, IReadOnlyList<OverlapRegion> units)
        {
            FilePath = filePath;
            Units = units;
        }

        public string FilePath { get; }
        public IReadOnlyList<OverlapRegion> Units { get; }
    }

    /// <summary>
    /// Units made by the last merge or split. They are listed whatever the
    /// object list has selected, until the user moves that selection — the
    /// result of an action has to be visible, and the action is over as soon as
    /// they look somewhere else.
    /// </summary>
    IReadOnlyList<OverlapRegion> _pinnedUnits = Array.Empty<OverlapRegion>();

    /// <summary>
    /// Put the cursor on the unit a merge or a split has just made, so the
    /// result is what the user is looking at rather than something they have to
    /// find. Silent when there is none.
    /// </summary>
    void FocusPinnedUnit()
    {
        if (_pinnedUnits.Count == 0) return;

        RebuildRows(resetScroll: false);
        for (int i = 0; i < _rows.Count; i++)
        {
            if (_rows[i].Object is not null) continue;
            if (!_pinnedUnits.Any(r => ReferenceEquals(r, _units[_rows[i].UnitIndex].Region))) continue;

            _list.SelectOnly(i);
            return;
        }
    }

    /// <summary>
    /// Announce a change of selection, and keep the menu's own commands in step
    /// with it. Every path that changes what is selected comes through here.
    /// </summary>
    void RaiseSelectionChanged()
    {
        RefreshCommandState();
        RefreshWholePageWarning();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    void RefreshCommandState()
    {
        var selected = SelectedByUnit();
        // Anything selected and SHOWN can be combined, whatever units it is
        // spread over — each unit's shown layers become one picture. With every
        // selected layer hidden there is nothing to draw.
        _flattenSelection.Enabled = selected.Any(x => x.Members.Any(m => !Hidden(x.Unit, m)));
        _clearSelection.Enabled = selected.Count > 0;
        _undoFlatten.Enabled = CanUndoFlatten;

        var (units, selection) = EditingScope();
        _mergeSelection.Enabled = FlattenUnitEditing.CanMerge(units, selection);
        _splitSelection.Enabled = FlattenUnitEditing.CanSplit(units, selection);
    }

    /// <summary>
    /// The groups whose thumbnails the visible rows need. The panel holds no
    /// bitmaps; the host renders these into the same viewport-bounded cache the
    /// object list uses.
    /// </summary>
    public IReadOnlyList<CrossFileImageGroup> VisibleThumbnailGroups()
    {
        if (ThumbnailFor is null || _rows.Count == 0) return Array.Empty<CrossFileImageGroup>();

        var (first, count) = _list.VisibleRange();
        var groups = new List<CrossFileImageGroup>(count);
        for (int i = first; i < first + count && i < _rows.Count; i++)
        {
            var member = _rows[i].Object;
            // Text draws its string, so it needs nothing rendered.
            if (member is null || member.Kind == RemovableKind.Text) continue;
            var group = ThumbnailFor(member).Group;
            if (group is not null) groups.Add(group);
        }
        return groups;
    }

    /// <summary>
    /// Repaint the rows: their thumbnails come from the same cache the object
    /// list uses, and the host fills that in the background.
    /// </summary>
    public void RefreshThumbnails() => _list.Invalidate();

    // =======================================================================
    // The preview
    // =======================================================================

    /// <summary>
    /// The outline answers "what would become a picture", so it follows the
    /// SELECTION. With nothing selected it follows the first row, which is what
    /// makes clicking an object on the left already show where it is.
    /// </summary>
    void ShowPreviewForSelection()
    {
        var selected = SelectedByUnit();
        if (selected.Count == 0)
        {
            if (_rows.Count > 0) ShowPreviewFor(_rows[0]);
            return;
        }

        var (unit, members) = selected[0];
        ShowPreview(unit, members.Select(RectOf).ToArray());
    }

    void ShowPreviewFor(Row row)
    {
        var unit = _units[row.UnitIndex];
        var shown = row.Object is null ? unit.Region.Members : new[] { row.Object };
        ShowPreview(unit, shown.Select(RectOf).ToArray());
    }

    /// <summary>
    /// Show a page with the selected layers outlined. The page comes from
    /// wherever the host says — with hidden layers taken out, that is a copy —
    /// and asking for it is work, so it is awaited and only the newest answer is
    /// used. Until it arrives the pane keeps showing what it had.
    /// </summary>
    async void ShowPreview(UnitEntry unit, IReadOnlyList<RectangleF> outlined)
    {
        int request = ++_previewRequest;
        var source = unit.FilePath;
        if (PreviewSourceFor is not null)
        {
            try
            {
                source = await PreviewSourceFor(unit.FilePath);
            }
            catch (Exception)
            {
                // The page as it stands is a worse answer than the page without
                // its hidden layers, and a better one than no page at all.
                source = unit.FilePath;
            }
        }
        if (request != _previewRequest || IsDisposed) return;

        _preview.Show(source, unit.Region.PageNumber, outlined);
    }

    // Only the newest preview may install itself: the user can click down a
    // list, and each click can be waiting on a copy being written.
    int _previewRequest;

    static RectangleF RectOf(PlacedObject o) =>
        new((float)o.X, (float)o.Y, (float)o.Width, (float)o.Height);

    // =======================================================================
    // The preview pane
    // =======================================================================

    /// <summary>
    /// One page of one file, drawn with the selected row's rectangles picked
    /// out. Exactly one rendered page is held at a time — the same
    /// viewport-bounded memory rule the rest of the app follows.
    /// </summary>
    sealed class PreviewPane : Panel
    {
        // Control.Margin is a different thing (spacing outside the control), so
        // this one says what it measures: the gap between the pane's edge and
        // the page drawn inside it.
        const int PageInset = 12;

        readonly PdfPageRenderer _renderer = new();
        RenderedPage? _page;
        // What is selected, outlined. What is HIDDEN needs no mark here: the
        // page being rendered is one those layers are already gone from.
        IReadOnlyList<RectangleF> _boxesInPoints = Array.Empty<RectangleF>();
        string? _filePath;
        int _pageNumber;
        int _renderedWidth;
        // Only the newest request may install its result: the user can click
        // through the list faster than a page renders.
        int _requestId;
        bool _closed;

        public PreviewPane()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                   | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = SystemColors.Window;
        }

        int Dip(int logical) => LogicalToDeviceUnits(logical);

        public void Show(
            string filePath, int pageNumber, IReadOnlyList<RectangleF> selectedInPoints)
        {
            _boxesInPoints = selectedInPoints;
            // Same page, different boxes (a different row on the same page):
            // repaint, do not render again.
            if (_filePath == filePath && _pageNumber == pageNumber && _page is not null)
            {
                Invalidate();
                return;
            }
            _filePath = filePath;
            _pageNumber = pageNumber;
            Invalidate();
            _ = RenderAsync();
        }

        public void Clear()
        {
            _filePath = null;
            _page?.Bitmap.Dispose();
            _page = null;
            _boxesInPoints = Array.Empty<RectangleF>();
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            // Re-render only when the pane has grown past what was rendered:
            // shrinking scales down cleanly, growing would show a blurry page.
            if (_filePath is not null && ClientSize.Width > _renderedWidth + Dip(32))
            {
                _ = RenderAsync();
            }
        }

        async Task RenderAsync()
        {
            if (_filePath is null || _closed) return;
            int width = Math.Max(1, ClientSize.Width - (Dip(PageInset) * 2));
            int id = ++_requestId;

            var rendered = await _renderer.RenderAsync(_filePath, _pageNumber, width);
            if (_closed || id != _requestId)
            {
                rendered?.Bitmap.Dispose();
                return;
            }

            _page?.Bitmap.Dispose();
            _page = rendered;
            _renderedWidth = width;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var area = new Rectangle(
                Dip(PageInset), Dip(PageInset),
                Math.Max(1, ClientSize.Width - (Dip(PageInset) * 2)),
                Math.Max(1, ClientSize.Height - (Dip(PageInset) * 2)));

            if (_page is null)
            {
                TextRenderer.DrawText(e.Graphics,
                    _filePath is null ? string.Empty : L10n.ThumbnailPending,
                    Font, area, SystemColors.GrayText,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }

            var display = FitInside(_page.Bitmap.Size, area);
            var boxes = PageHighlightPainter.MapToDisplay(
                display, _page.PageWidthPoints, _page.PageHeightPoints, _page.RotationDegrees,
                _boxesInPoints, Dip(4));
            e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            PageHighlightPainter.DrawPage(
                e.Graphics, _page.Bitmap, display, Array.Empty<RectangleF>());
            using (var frame = new Pen(SystemColors.ControlDark))
            {
                e.Graphics.DrawRectangle(frame, display.X, display.Y, display.Width - 1, display.Height - 1);
            }
            PageHighlightPainter.DrawOutlines(e.Graphics, boxes, Dip(3));
        }

        static Rectangle FitInside(Size imageSize, Rectangle area)
        {
            double scale = Math.Min(
                (double)area.Width / imageSize.Width,
                (double)area.Height / imageSize.Height);
            int w = Math.Max(1, (int)(imageSize.Width * scale));
            int h = Math.Max(1, (int)(imageSize.Height * scale));
            return new Rectangle(
                area.X + ((area.Width - w) / 2), area.Y + ((area.Height - h) / 2), w, h);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _closed = true;
                _page?.Bitmap.Dispose();
                _page = null;
            }
            base.Dispose(disposing);
        }
    }
}
