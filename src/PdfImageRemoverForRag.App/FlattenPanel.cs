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
/// The 統合 (Flatten) panel, docked to the right of the object list: the units
/// the object selected on the left takes part in, laid out like an image
/// editor's layers panel, with a preview of the page underneath.
///
/// It answers one question — "where does this object overlap something, and
/// what would be baked with it" — about whatever the list has selected. That is
/// why it is not a tree of the whole document: the object list already is that
/// overview, and duplicating it beside itself gave the user two lists to read
/// and no reason to prefer either.
///
/// A unit is a layer group and its objects are the layers inside it, each with
/// a thumbnail, a name and a checkbox. Ticking the group takes everything in
/// it; ticking objects takes only those.
///
/// **Ticks live here, not in the rows.** The list is a filtered view — moving
/// the selection on the left changes which units are shown — so anything held
/// per row would be lost the moment the user looked at another object.
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
    /// every expand and every tick.
    /// </summary>
    readonly record struct Row(int UnitIndex, PlacedObject? Object);

    // Every unit in the open workspace, and the subset currently listed.
    readonly List<UnitEntry> _units = new();
    readonly List<Row> _rows = new();

    // What is ticked, by unit. Reference-keyed: the regions come from the
    // analysis of the open files and live as long as the workspace does, while
    // two different places on a page can hold equal-valued members.
    readonly Dictionary<OverlapRegion, HashSet<PlacedObject>> _checked =
        new((IEqualityComparer<OverlapRegion>)ReferenceEqualityComparer.Instance);

    CrossFileImageGroup? _selectedGroup;
    bool _anyDocuments;

    readonly LayerListView _list;
    // Title and a clear button share a bar, so the command sits with the thing
    // it acts on. The toolbar's Clear Selection deliberately does not reach in
    // here — one button that emptied both sides would make an irreversible
    // operation's target unpredictable — which left ticks made across a dozen
    // different objects with no way back but saving.
    readonly Panel _titleBar = new() { Dock = DockStyle.Top };
    readonly Label _title = new()
    {
        Dock = DockStyle.Fill,
        AutoSize = false,
        // The glyph pairs it with the object list's ☑ column across the
        // window: one side's ticks remove, this side's flatten.
        Text = L10n.FlattenPanelHeader,
        TextAlign = ContentAlignment.MiddleLeft,
        UseMnemonic = false,
    };
    readonly Button _clearChecks = new()
    {
        Dock = DockStyle.Right,
        Text = L10n.ToolClearSelection,
        FlatStyle = FlatStyle.System,
        AutoSize = false,
        Enabled = false,
        UseMnemonic = false,
    };

    // Detection is right almost every time; these are for the rest. Both act on
    // what is TICKED, which is what makes them each other's opposite: merge
    // gathers the ticks into one unit, split takes them out of the one they are
    // in. Docked right in reverse order, so they read merge, split, clear.
    readonly Button _splitSelection = NewUnitEditButton(L10n.FlattenSplit);
    readonly Button _mergeSelection = NewUnitEditButton(L10n.FlattenMerge);

    static Button NewUnitEditButton(string caption) => new()
    {
        Dock = DockStyle.Right,
        Text = caption,
        FlatStyle = FlatStyle.System,
        AutoSize = false,
        Enabled = false,
        UseMnemonic = false,
    };
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

    /// <summary>Raised whenever the set of ticked objects changes.</summary>
    public event EventHandler? SelectionChanged;

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
        _list = new LayerListView(VisualForRow)
        {
            Dock = DockStyle.Fill,
            Visible = false,
        };
        _list.AccessibleName = L10n.FlattenPanelTitle;
        _list.AccessibleDescription = L10n.FlattenDescription;
        _list.CheckToggled += OnCheckToggled;
        _list.ExpandToggled += OnExpandToggled;
        _list.RowSelected += OnRowSelected;
        _list.ToolTipFor = ToolTipForRow;
        _list.ViewportChanged += (_, e) => ViewportChanged?.Invoke(this, e);

        _preview.AccessibleName = L10n.AccessibleFlattenPreview;
        _wholePageWarning.ForeColor = MainForm.WarningTextColour;
        _title.Font = new Font(Font, FontStyle.Bold);
        _clearChecks.AccessibleName = $"{L10n.ToolClearSelection} ({L10n.FlattenPanelTitle})";
        _clearChecks.Click += (_, _) => ClearChecks();
        _mergeSelection.AccessibleName = $"{L10n.FlattenMerge} ({L10n.FlattenPanelTitle})";
        _splitSelection.AccessibleName = $"{L10n.FlattenSplit} ({L10n.FlattenPanelTitle})";
        _mergeSelection.Click += (_, _) => EditUnits(merge: true);
        _splitSelection.Click += (_, _) => EditUnits(merge: false);

        // Added in this order because Dock.Right stacks each new control
        // inboard of the last: clear ends up rightmost, then split, then merge.
        _titleBar.Controls.Add(_title);
        _titleBar.Controls.Add(_clearChecks);
        _titleBar.Controls.Add(_splitSelection);
        _titleBar.Controls.Add(_mergeSelection);

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
        foreach (var button in new[] { _clearChecks, _splitSelection, _mergeSelection })
        {
            button.Width = TextRenderer.MeasureText(button.Text, button.Font).Width + Dip(24);
        }
        // One standard button tall, and the bar is sized to it: the button
        // fills whatever height it is docked into, so the bar is what decides
        // whether it looks like a button or a slab.
        _titleBar.Height = Dip(30);
        _titleBar.Padding = new Padding(0, Dip(3), Dip(8), Dip(3));
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
    /// Show the warning when what is ticked would cover essentially the whole
    /// page — on any page, since one such unit is enough to lose a page of text.
    ///
    /// It reads the TICKED objects, not the units as detected: the area that
    /// becomes a picture is the bounding box of what the user chose, and a
    /// region can be whole-page as found yet a corner of it once narrowed down.
    /// </summary>
    void RefreshWholePageWarning()
    {
        bool covers = false;
        foreach (var unit in _units)
        {
            if (!_checked.TryGetValue(unit.Region, out var ticked) || ticked.Count == 0) continue;
            var members = unit.Region.Members.Where(ticked.Contains).ToArray();
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
    /// Take the units out of a freshly analyzed workspace. Ticks do not survive:
    /// the regions they referred to are gone, and carrying them over to a
    /// different set would flatten something nobody chose.
    /// </summary>
    public void SetDocuments(IReadOnlyList<PdfDocumentInfo> documents)
    {
        _units.Clear();
        _checked.Clear();
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

        // A merge or a split has just rearranged the units; the objects that
        // were ticked are the same objects, so the ticks go back on.
        if (_pendingChecks is { Count: > 0 } restore)
        {
            foreach (var unit in _units)
            {
                var here = unit.Region.Members.Where(restore.Contains).ToArray();
                if (here.Length > 0) _checked[unit.Region] = new HashSet<PlacedObject>(here);
            }
        }
        _pendingChecks = null;
        FocusPinnedUnit();

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
        RebuildRows(resetScroll: true);
        _preview.Clear();
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

    void RebuildRows(bool resetScroll)
    {
        _rows.Clear();
        for (int i = 0; i < _units.Count; i++)
        {
            bool pinned = _pinnedUnits.Any(r => ReferenceEquals(r, _units[i].Region));
            if (!pinned && (_selectedGroup is null || !UnitContains(_units[i], _selectedGroup))) continue;

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

    LayerVisual VisualForRow(int index)
    {
        if (index < 0 || index >= _rows.Count) return default;
        var row = _rows[index];
        var unit = _units[row.UnitIndex];

        if (row.Object is null)
        {
            // The unit's box answers "is all of this being flattened", so a
            // part-ticked unit gets the mixed state rather than being rounded
            // to one of the other two.
            int ticked = _checked.GetValueOrDefault(unit.Region)?.Count ?? 0;
            return new LayerVisual(
                IsGroup: true,
                Title: $"{L10n.FlattenUnitLabel(unit.DocumentNumber, unit.Region.PageNumber, unit.NumberOnPage)} "
                    + $"({KindSummary(unit.Region)})",
                Subtitle: $"{Path.GetFileName(unit.FilePath)}  {L10n.UsagePageLabel(unit.Region.PageNumber)}",
                Thumbnail: null,
                TextContent: null,
                IsThumbnailPending: false,
                Check: ticked == 0 ? CheckState.Unchecked
                     : ticked == unit.Region.Members.Count ? CheckState.Checked
                     : CheckState.Indeterminate,
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
            Check: _checked.GetValueOrDefault(unit.Region)?.Contains(member) == true
                ? CheckState.Checked
                : CheckState.Unchecked,
            IsExpanded: false);
    }

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

    // One kind-to-label function for the whole App: the panel used to keep its
    // own copy, which listed three kinds and called anything else an image.
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
    // Ticking
    // =======================================================================

    void OnCheckToggled(int index)
    {
        if (index < 0 || index >= _rows.Count) return;
        var row = _rows[index];
        var unit = _units[row.UnitIndex];
        var ticked = _checked.TryGetValue(unit.Region, out var found)
            ? found
            : _checked[unit.Region] = new HashSet<PlacedObject>();

        if (row.Object is null)
        {
            // A unit is a bulk switch for its objects.
            bool takeAll = ticked.Count != unit.Region.Members.Count;
            ticked.Clear();
            if (takeAll) foreach (var m in unit.Region.Members) ticked.Add(m);
        }
        else if (!ticked.Remove(row.Object))
        {
            ticked.Add(row.Object);
        }

        if (ticked.Count == 0) _checked.Remove(unit.Region);
        // A tick changes how rows look, never which rows exist — the check
        // state is read while painting. Rebuilding would re-filter every unit
        // in the workspace and re-trigger a thumbnail load for no reason.
        _list.Invalidate();
        ShowPreviewFor(row);
        RaiseSelectionChanged();
    }

    void OnExpandToggled(int index)
    {
        if (index < 0 || index >= _rows.Count) return;
        var unit = _units[_rows[index].UnitIndex];
        unit.Expanded = !unit.Expanded;
        RebuildRows(resetScroll: false);
    }

    void OnRowSelected(int index)
    {
        if (index < 0 || index >= _rows.Count) return;
        ShowPreviewFor(_rows[index]);
    }

    void ShowPreviewFor(Row row)
    {
        var unit = _units[row.UnitIndex];

        // The outline answers "what would become an image", so it follows the
        // TICKS, not the cursor. It used to follow whichever row had focus, and
        // since clicking a checkbox also moves focus, ticking a unit, unticking
        // one of its objects and ticking it back left the outline showing that
        // one object — the ticks were back to all, the picture was not.
        //
        // The focused row still decides it while the unit has nothing ticked:
        // there the question really is "where is this one?".
        var ticked = _checked.GetValueOrDefault(unit.Region);
        var shown = ticked is { Count: > 0 }
            ? unit.Region.Members.Where(ticked.Contains)     // members' order, not click order
            : row.Object is null
                ? unit.Region.Members
                : new[] { row.Object };
        _preview.Show(
            unit.FilePath, unit.Region.PageNumber, shown.Select(RectOf).ToArray());
    }

    static RectangleF RectOf(PlacedObject o) =>
        new((float)o.X, (float)o.Y, (float)o.Width, (float)o.Height);

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

            _list.SetFocusedRow(i);
            ShowPreviewFor(_rows[i]);
            return;
        }
    }

    /// <summary>
    /// Repaint the rows: their thumbnails come from the same cache the object
    /// list uses, and the host fills that in the background.
    /// </summary>
    public void RefreshThumbnails() => _list.Invalidate();

    /// <summary>Clear every tick. Called after a save.</summary>
    public void ClearChecks()
    {
        if (_checked.Count == 0) return;
        _checked.Clear();
        _list.Invalidate();
        // The outline was showing what was ticked, and nothing is now.
        int focused = _list.FocusedRow;
        if (focused >= 0 && focused < _rows.Count) ShowPreviewFor(_rows[focused]);
        RaiseSelectionChanged();
    }

    /// <summary>How many individual objects are ticked, across every unit.</summary>
    public int CheckedObjectCount => _checked.Values.Sum(set => set.Count);

    /// <summary>
    /// Raised when the user has merged or split units by hand, with the file
    /// whose units changed and the list that replaces them. The panel does not
    /// own the workspace — it describes one — so the change is handed back to
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
    /// What merging and splitting act on: the units of the one file the ticks
    /// are in, and the ticked objects themselves. Ticks in more than one file
    /// answer an empty scope, which disables both buttons — a unit is a
    /// rectangle on one page and there is no such rectangle across two files.
    /// </summary>
    (IReadOnlyList<OverlapRegion> Units, IReadOnlyList<PlacedObject> Selection) EditingScope()
    {
        var ticked = _units
            .Where(u => _checked.TryGetValue(u.Region, out var set) && set.Count > 0)
            .ToList();
        if (ticked.Count == 0) return (Array.Empty<OverlapRegion>(), Array.Empty<PlacedObject>());

        var filePath = ticked[0].FilePath;
        var pageNumber = ticked[0].Region.PageNumber;
        if (ticked.Any(u => !string.Equals(u.FilePath, filePath, StringComparison.OrdinalIgnoreCase)
                            || u.Region.PageNumber != pageNumber))
        {
            return (Array.Empty<OverlapRegion>(), Array.Empty<PlacedObject>());
        }

        var selection = ticked
            .SelectMany(u => u.Region.Members.Where(_checked[u.Region].Contains))
            .ToArray();
        // Every unit of that page, not only the ticked ones: a merge has to see
        // the units it is taking objects out of.
        //
        // ONE PAGE, and that is not only about what a merge may join. A member
        // carries no page number and compares by value, so a footer printed at
        // the same spot on twenty pages is twenty EQUAL objects: handed every
        // unit in the file, "which units hold this selection" answered twenty,
        // and both buttons stayed dead on every document with a running header.
        var units = _units
            .Where(u => string.Equals(u.FilePath, filePath, StringComparison.OrdinalIgnoreCase)
                        && u.Region.PageNumber == pageNumber)
            .Select(u => u.Region)
            .ToArray();
        return (units, selection);
    }

    /// <summary>
    /// Merge or split, then hand the new unit list back. The ticks are put back
    /// on afterwards: they are the same objects, so the user's selection
    /// survives an operation that rearranges the units around it.
    /// </summary>
    void EditUnits(bool merge)
    {
        var (units, selection) = EditingScope();
        if (units.Count == 0) return;

        var edited = merge
            ? FlattenUnitEditing.Merge(units, selection)
            : FlattenUnitEditing.Split(units, selection);
        if (ReferenceEquals(edited, units)) return;

        // The unit an edit just made holds the objects that were ticked, which
        // are not necessarily the object selected in the list on the left — and
        // the panel lists only the units of THAT object. Without pinning, a
        // merge answered by making its own result disappear.
        _pinnedUnits = edited
            .Where(r => !units.Any(u => ReferenceEquals(u, r)))
            .ToArray();

        var filePath = _units
            .First(u => units.Contains(u.Region))
            .FilePath;
        _pendingChecks = selection;
        UnitsEdited?.Invoke(this, new UnitsEditedEventArgs(filePath, edited));
    }

    /// <summary>
    /// Ticks to re-apply after the workspace hands the panel its documents
    /// back. Cleared as soon as they are used, so an unrelated rebuild — a save,
    /// a file being opened — still starts with nothing ticked.
    /// </summary>
    IReadOnlyList<PlacedObject>? _pendingChecks;

    /// <summary>
    /// Units made by the last merge or split. They are listed whatever the
    /// object list has selected, until the user moves that selection — the
    /// result of an action has to be visible, and the action is over as soon as
    /// they look somewhere else.
    /// </summary>
    IReadOnlyList<OverlapRegion> _pinnedUnits = Array.Empty<OverlapRegion>();

    /// <summary>
    /// Announce a change of ticks, and keep the panel's own clear button in
    /// step with them. Every path that touches <c>_checked</c> comes through
    /// here, so the button can never claim there is something to clear when
    /// there is not.
    /// </summary>
    void RaiseSelectionChanged()
    {
        _clearChecks.Enabled = _checked.Count > 0;
        var (units, selection) = EditingScope();
        _mergeSelection.Enabled = FlattenUnitEditing.CanMerge(units, selection);
        _splitSelection.Enabled = FlattenUnitEditing.CanSplit(units, selection);
        RefreshWholePageWarning();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
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
    /// The places to flatten, per source file — each covering only the objects
    /// that are actually ticked, which is also all that will be removed. A unit
    /// with nothing ticked is not in the result at all.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<OverlapRegion>> SelectedRegionsByFile()
    {
        var byFile = new Dictionary<string, List<OverlapRegion>>(StringComparer.OrdinalIgnoreCase);
        foreach (var unit in _units)
        {
            if (!_checked.TryGetValue(unit.Region, out var ticked) || ticked.Count == 0) continue;

            // In the region's own member order, so the covering rectangle is
            // built from the same sequence the analyzer produced.
            var members = unit.Region.Members.Where(ticked.Contains).ToArray();
            if (!byFile.TryGetValue(unit.FilePath, out var regions))
            {
                regions = new List<OverlapRegion>();
                byFile[unit.FilePath] = regions;
            }
            regions.Add(OverlapDetector.RegionCovering(unit.Region.Page, members));
        }
        return byFile.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<OverlapRegion>)kv.Value,
            StringComparer.OrdinalIgnoreCase);
    }

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

        public void Show(string filePath, int pageNumber, IReadOnlyList<RectangleF> boxesInPoints)
        {
            _boxesInPoints = boxesInPoints;
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
            PageHighlightPainter.DrawPage(e.Graphics, _page.Bitmap, display, boxes);
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
