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
internal readonly record struct ObjectThumbnail(
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
internal sealed class GraphicsObjectsPanel : UserControl
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

    readonly UnitListView _list;
    // Title and one button share a bar. The button is a button and not a menu
    // because there is one command at this level — everything else belongs to a
    // unit and lives in that unit's row. A menu holding a single item asks the
    // user to open it to find that out.
    readonly Panel _titleBar = new() { Dock = DockStyle.Top };
    readonly Label _title = new()
    {
        Dock = DockStyle.Fill,
        AutoSize = false,
        Text = L10n.GraphicsObjectsTitle,
        TextAlign = ContentAlignment.MiddleLeft,
        UseMnemonic = false,
    };
    readonly Button _mergeButton = new()
    {
        Dock = DockStyle.Right,
        Text = L10n.FlattenMerge,
        FlatStyle = FlatStyle.System,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        UseMnemonic = false,
    };

    // A unit's menu lives in its own row and acts on THAT unit: turning what it
    // holds into a picture, doing the same to only what is selected inside it,
    // splitting a selection off. Nothing in it needs the words "in the selected
    // unit", because the row it was opened from is the answer — which is the
    // whole reason it is not one menu at the top of the panel.
    readonly ContextMenuStrip _unitMenu = new() { AccessibleName = L10n.FlattenUnitMenu };
    readonly ToolStripMenuItem _flattenVisible = new(L10n.FlattenVisible);
    readonly ToolStripMenuItem _flattenSelected = new(L10n.FlattenSelected);
    readonly ToolStripMenuItem _splitSelection = new(L10n.FlattenSplit);

    // The unit whose row menu is open. Set as it opens and read by the
    // commands, so what they act on cannot drift from what was pressed.
    UnitEntry? _menuUnit;

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
    /// Raised when the user asks for objects to be turned into a picture now,
    /// carrying the places to draw. The panel does not do it: this writes a
    /// file, and the workspace is what owns files.
    /// </summary>
    public event EventHandler<FlattenRequestedEventArgs>? FlattenRequested;

    internal sealed class FlattenRequestedEventArgs : EventArgs
    {
        public FlattenRequestedEventArgs(
            IReadOnlyDictionary<string, IReadOnlyList<OverlapRegion>> places)
        {
            Places = places;
        }

        /// <summary>The places to draw, per source file. Never empty.</summary>
        public IReadOnlyDictionary<string, IReadOnlyList<OverlapRegion>> Places { get; }
    }

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
    public Func<PlacedObject, ObjectThumbnail>? ThumbnailFor { get; set; }

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
    /// Height of the preview under the list, in LOGICAL pixels: set by the host
    /// from the saved layout before the handle exists, updated as the user drags
    /// the splitter, and read back at shutdown. Zero means the user has never
    /// chosen one, and the three-fifths default applies.
    ///
    /// It is kept here rather than read off <see cref="_split"/> on demand
    /// because a DPI change re-applies it: converting device pixels back at the
    /// NEW scale would move the splitter every time the window changed monitor.
    /// </summary>
    public int PreviewHeight
    {
        get => _previewHeight;
        set
        {
            _previewHeight = value;
            // A height arriving from outside is one the user chose in an
            // earlier session, and setting it has to show.
            _previewHeightIsTheUsersChoice = value > 0;
            if (IsHandleCreated) ApplyPreviewHeight();
        }
    }

    int _previewHeight;

    /// <summary>
    /// Whether that height is the USER's answer rather than this panel's. Until
    /// it is, the preview takes the golden section of the panel and grows with
    /// it — the same rule the workspace split follows, and for the same reason:
    /// a height fixed in pixels turns every resize into a drag the user has to
    /// do themselves.
    /// </summary>
    bool _previewHeightIsTheUsersChoice;

    // Set by SplitterMoving, which ONLY fires while the user is dragging, and
    // consumed by the SplitterMoved that ends the drag. SplitterMoved alone is
    // not a signal that anything was chosen: the layout engine raises it too,
    // repeatedly, while the panel is still being built.
    bool _splitterDragged;

    public GraphicsObjectsPanel()
    {
        _list = new UnitListView(VisualForRow, IsGroupRow)
        {
            Dock = DockStyle.Fill,
            Visible = false,
        };
        _list.AccessibleName = L10n.GraphicsObjectsTitle;
        _list.AccessibleDescription = L10n.FlattenDescription;
        _list.VisibilityToggled += OnVisibilityToggled;
        _list.ExpandToggled += OnExpandToggled;
        _list.SelectionChanged += OnRowSelectionChanged;
        _list.ToolTipFor = ToolTipForRow;
        _list.ViewportChanged += (_, e) => ViewportChanged?.Invoke(this, e);

        _preview.AccessibleName = L10n.AccessibleFlattenPreview;
        _wholePageWarning.ForeColor = MainForm.WarningTextColour;
        _title.Font = new Font(Font, FontStyle.Bold);

        _mergeButton.AccessibleName = $"{L10n.FlattenMerge} ({L10n.GraphicsObjectsTitle})";
        _mergeButton.Click += (_, _) => EditUnits(merge: true);
        _unitMenu.Items.AddRange(new ToolStripItem[]
        {
            _flattenVisible, _flattenSelected,
            new ToolStripSeparator(), _splitSelection,
        });
        // Decided as a menu opens rather than kept in step with every click:
        // there is one moment when it matters, and this way it cannot be stale.
        _unitMenu.Opening += (_, _) => RefreshCommandState();
        _flattenVisible.Click += (_, _) => RequestFlatten(VisibleIn(_menuUnit));
        _flattenSelected.Click += (_, _) => RequestFlatten(SelectedIn(_menuUnit));
        _splitSelection.Click += (_, _) => EditUnits(merge: false);
        _list.UnitMenuRequested += OnUnitMenuRequested;

        _titleBar.Controls.Add(_title);
        _titleBar.Controls.Add(_mergeButton);

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
        _previewHeight = Undip(PreviewHeightInDevicePixels);
        _previewHeightIsTheUsersChoice = true;
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
        _titleBar.Height = Dip(30);
        _titleBar.Padding = new Padding(0, Dip(3), Dip(6), Dip(3));
        FitWarningHeight();
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

        int wantedListHeight = _previewHeightIsTheUsersChoice
            ? available - Dip(_previewHeight) - _split.SplitterWidth
            : (int)(available * (1 - GoldenSection.MinorShare));

        _split.SplitterDistance = Math.Clamp(
            wantedListHeight,
            _split.Panel1MinSize,
            available - _split.Panel2MinSize - _split.SplitterWidth);
    }

    /// <summary>
    /// The warning wraps, and this panel is narrow and user-resizable, so its
    /// height has to be re-measured whenever the width changes.
    /// </summary>
    void FitWarningHeight()
    {
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
        foreach (var (unit, _) in SelectedByUnit())
        {
            var shown = VisibleIn(unit);
            if (shown.Count > 0 && OverlapDetector.CoversWholePage(
                    OverlapDetector.RegionCovering(unit.Region.Page, shown.ToArray())))
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
        if (!IsHandleCreated) return;
        FitWarningHeight();
        // Re-share the panel: growing it should grow the page underneath, not
        // leave it at the height a smaller window decided.
        if (!_previewHeightIsTheUsersChoice) ApplyPreviewHeight();
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

        // Selecting an object on the left selects the unit it sits in here, so
        // the panel arrives pointing at something: its page is in the preview
        // and the commands act on it, without a second click. Which object the
        // left pane named does not narrow that selection — the commands say
        // "the selected unit", and this is the same rule they follow.
        if (_rows.Count > 0)
        {
            _list.SelectOnly(0);
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
    /// What the panel says when it has no rows. Four different silences, and
    /// two of them are worth explaining: an object that overlaps nothing, and
    /// files with nothing to combine anywhere in them, are the cases where the
    /// user is entitled to wonder what went wrong.
    /// </summary>
    string EmptyMessage()
    {
        if (!_anyDocuments) return L10n.StatusOpenPrompt;
        if (_units.Count == 0) return L10n.FlattenNoOverlaps;
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

    RowVisual VisualForRow(int index)
    {
        if (index < 0 || index >= _rows.Count) return default;
        var row = _rows[index];
        var unit = _units[row.UnitIndex];

        if (row.Object is null)
        {
            // The folder's eye answers for everything inside it, and has a third
            // answer when its objects disagree.
            int hidden = unit.Region.Members.Count(m => Hidden(unit, m));
            return new RowVisual(
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
                Visibility: hidden == 0 ? RowVisibility.Visible
                    : hidden == unit.Region.Members.Count ? RowVisibility.Hidden
                    : RowVisibility.Mixed,
                IsExpanded: unit.Expanded);
        }

        var member = row.Object;
        // Text draws its string rather than a bitmap, the same rule the table
        // and the tiles follow — so one object looks the same in all three, and
        // so a text row never pays for a lookup whose answer it discards.
        string? text = ImageListRow.ThumbnailText(member.Kind, member.Identity);
        var thumbnail = text is null ? ThumbnailFor?.Invoke(member) ?? default : default;

        return new RowVisual(
            IsGroup: false,
            Title: ObjectLabel(member),
            Subtitle: null,
            Thumbnail: thumbnail.Bitmap,
            TextContent: text,
            IsThumbnailPending: text is null && thumbnail.Bitmap is null && thumbnail.CanEverRender,
            Visibility: Hidden(unit, member) ? RowVisibility.Hidden : RowVisibility.Visible,
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

    /// <summary>
    /// Select the first row that is an object rather than a unit. Showing the
    /// panel normally selects the unit — the commands act on units — and this
    /// is the one caller that wants the other thing: a photograph of the panel
    /// should show what an outlined object looks like.
    /// </summary>
    public void SelectFirstObject()
    {
        for (int i = 0; i < _rows.Count; i++)
        {
            if (_rows[i].Object is null) continue;
            _list.SelectOnly(i);
            return;
        }
    }

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
    /// What is drawn in one unit. Hidden objects stay out: one of those is
    /// going to be taken out by the save, and drawing it into the picture would
    /// put it back as pixels.
    /// </summary>
    IReadOnlyList<PlacedObject> VisibleIn(UnitEntry? unit) => unit is null
        ? Array.Empty<PlacedObject>()
        : unit.Region.Members.Where(m => !Hidden(unit, m)).ToArray();

    /// <summary>
    /// What is drawn AND selected in one unit — the narrower of the two
    /// commands. In the region's own member order, because the covering
    /// rectangle is built from that sequence.
    /// </summary>
    IReadOnlyList<PlacedObject> SelectedIn(UnitEntry? unit)
    {
        if (unit is null) return Array.Empty<PlacedObject>();

        var picked = _list.SelectedRows
            .Where(index => index < _rows.Count && ReferenceEquals(_units[_rows[index].UnitIndex], unit))
            .Select(index => _rows[index].Object)
            .Where(o => o is not null)
            .ToArray();
        return unit.Region.Members
            .Where(m => !Hidden(unit, m) && picked.Contains(m))
            .ToArray();
    }

    /// <summary>
    /// Ask for these objects to become a picture. One place, in one file: both
    /// commands act on one unit, which is the row their menu was opened from.
    /// </summary>
    void RequestFlatten(IReadOnlyList<PlacedObject> members)
    {
        if (_menuUnit is null || members.Count == 0) return;

        var place = OverlapDetector.RegionCovering(_menuUnit.Region.Page, members.ToArray());
        FlattenRequested?.Invoke(this, new FlattenRequestedEventArgs(
            new Dictionary<string, IReadOnlyList<OverlapRegion>>(StringComparer.OrdinalIgnoreCase)
            {
                [_menuUnit.FilePath] = new[] { place },
            }));
    }

    /// <summary>Open a unit's own menu, on the unit whose row it came from.</summary>
    void OnUnitMenuRequested(int row, Point at)
    {
        if (row < 0 || row >= _rows.Count) return;
        _menuUnit = _units[_rows[row].UnitIndex];
        _unitMenu.Show(_list, at);
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
        // The unit's own commands answer about the row the menu belongs to.
        _flattenVisible.Enabled = VisibleIn(_menuUnit).Count > 0;
        _flattenSelected.Enabled = SelectedIn(_menuUnit).Count > 0;

        var (units, selection) = EditingScope();
        _mergeButton.Enabled = FlattenUnitEditing.CanMerge(units, selection);
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
    /// Show the page the selection is on, and outline the objects picked out BY
    /// NAME on it — a selected row that IS an object.
    ///
    /// A selected unit outlines nothing. The unit is what the preview is already
    /// showing, so boxing every object on the page marks nothing out; the box is
    /// worth drawing only when it says "this one of them".
    /// </summary>
    void ShowPreviewForSelection()
    {
        var selected = SelectedByUnit();
        if (selected.Count == 0)
        {
            _preview.Clear();
            return;
        }

        var unit = selected[0].Unit;
        ShowPreview(unit, OutlinedIn(unit).Select(RectOf).ToArray());
    }

    /// <summary>
    /// The objects of one unit the user selected as objects. A selected folder
    /// contributes none of its members — that is the whole difference between
    /// this and <see cref="SelectedByUnit"/>, which the commands use.
    /// </summary>
    IEnumerable<PlacedObject> OutlinedIn(UnitEntry unit) => _list.SelectedRows
        .Where(index => index < _rows.Count
                        && _rows[index].Object is not null
                        && ReferenceEquals(_units[_rows[index].UnitIndex], unit))
        .Select(index => _rows[index].Object!);

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

            // Enlarging is allowed: this page was rendered for this pane, so it
            // is already the right size — until the pane grows a little, where
            // filling it is better than a gap that shrinks again on the next
            // render.
            var display = Fit.Inside(_page.Bitmap.Size, area, mayEnlarge: true);
            var boxes = PageHighlightPainter.MapToDisplay(
                display, _page.PageWidthPoints, _page.PageHeightPoints, _page.RotationDegrees,
                _boxesInPoints, Dip(4));
            PageHighlightPainter.DrawMarkedPage(
                e.Graphics, _page.Bitmap, display, boxes, Dip(3), dimOutsideBoxes: false);
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
