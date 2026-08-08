using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using PdfImageRemoverForRag.Core.Errors;
using PdfImageRemoverForRag.Core.Formatting;
using PdfImageRemoverForRag.Core.Models;

namespace PdfImageRemoverForRag.App;

/// <summary>
/// The single-window UI: menu bar (File / View / Help), an icon toolbar,
/// a status bar, and between them a workspace split into the object list —
/// every removable image, text string and shape, switchable between table and
/// tile views — and the graphics objects panel beside it. Multiple PDFs can be
/// open at once; an object that occurs in several of them shows as ONE
/// row/tile, and the per-file breakdown lives in the usage-locations window a
/// row's right-click menu opens. This class does layout and event
/// wiring only — analysis, cleaning, and verification live in
/// <see cref="PdfCleaningWorkflow"/>, display formatting in
/// <see cref="ImageListRow"/>, and all user-visible strings in
/// <see cref="L10n"/>.
/// </summary>
internal sealed partial class MainForm : Form
{
    readonly PdfCleaningWorkflow _workflow;

    // --- menu --------------------------------------------------------------
    readonly MenuStrip _menuStrip = new();
    readonly ToolStripMenuItem _openMenuItem = new(L10n.MenuOpen) { ShortcutKeys = Keys.Control | Keys.O };
    readonly ToolStripMenuItem _saveMenuItem = new(L10n.MenuSave) { ShortcutKeys = Keys.Control | Keys.S, Enabled = false };
    readonly ToolStripMenuItem _closeAllMenuItem = new(L10n.MenuCloseAll) { Enabled = false };
    readonly ToolStripMenuItem _exitMenuItem = new(L10n.MenuExit);
    readonly ToolStripMenuItem _tableViewMenuItem = new(L10n.MenuTableView) { Checked = true, CheckOnClick = false };
    readonly ToolStripMenuItem _tileViewMenuItem = new(L10n.MenuTileView) { Checked = false, CheckOnClick = false };
    // 表示する種類 submenu: per-kind visibility filters. Every kind starts
    // checked; CheckOnClick is off so MainForm can veto turning off the last one.
    // The entries themselves are in _kindToggles.
    readonly ToolStripMenuItem _shownTypesMenuItem = new(L10n.MenuShownTypes);
    readonly ToolStripMenuItem _manualMenuItem = new(L10n.MenuManual);
    readonly ToolStripMenuItem _aboutMenuItem = new(L10n.MenuAbout);

    // --- toolbar (icon buttons) --------------------------------------------
    readonly ToolStrip _toolStrip = new() { GripStyle = ToolStripGripStyle.Hidden };
    readonly ToolStripButton _openToolButton = new() { Enabled = true };
    readonly ToolStripButton _saveToolButton = new() { Enabled = false };
    readonly ToolStripButton _selectAllToolButton = new() { Enabled = false };
    readonly ToolStripButton _clearSelectionToolButton = new() { Enabled = false };

    // The same per-kind filters as the menu, repeated on the toolbar so that
    // narrowing the list to one kind is a click rather than a trip through a
    // submenu per kind.
    //
    // Real check boxes, not ToolStripButtons with CheckOnClick.
    // Windows11ToolStripRenderer draws hover and press and returns before the
    // base renderer, so a checked ToolStripButton looks exactly like an
    // unchecked one — a toggle whose state cannot be seen is worse than no
    // toggle. A hosted CheckBox brings its own glyph and its own UIA toggle
    // pattern.
    // Held as a field, unlike the other separator, because its right-hand
    // margin is what keeps the filter boxes off it and that margin is scaled
    // per DPI like every other hand-picked distance.
    readonly ToolStripSeparator _filterSeparator = new();

    /// <summary>
    /// One kind's pair of switches: the submenu entry and the toolbar box. They
    /// show the same filter and are never allowed to disagree, so they travel
    /// together — through the wiring, the layout, the DPI pass and the
    /// synchronisation. Adding the fifth kind meant editing six places that each
    /// listed the other four; this is that list, written once.
    /// </summary>
    sealed record KindToggle(RemovableKind Kind, ToolStripMenuItem MenuItem, CheckBox Box);

    /// <summary>
    /// In display order, which is the enum's order — the same order the object
    /// list sorts by, so the filters read down the toolbar the way the rows read
    /// down the list.
    /// </summary>
    readonly KindToggle[] _kindToggles =
    {
        NewKindToggle(RemovableKind.Image, L10n.MenuShowImages),
        NewKindToggle(RemovableKind.Shape, L10n.MenuShowShapes),
        NewKindToggle(RemovableKind.Drawing, L10n.MenuShowDrawings),
        NewKindToggle(RemovableKind.Shadow, L10n.MenuShowShadows),
        NewKindToggle(RemovableKind.Text, L10n.MenuShowText),
    };

    static KindToggle NewKindToggle(RemovableKind kind, string caption) => new(
        kind,
        new ToolStripMenuItem(caption) { Checked = true, CheckOnClick = false },
        NewKindCheckBox(caption));

    /// <summary>
    /// One toolbar filter box. The caption is the same string the menu uses, so
    /// the two surfaces cannot describe the same filter differently, and the
    /// accessible name prefixes it with the submenu's own caption — a row of
    /// bare nouns needs to say what they are filtering.
    /// </summary>
    static CheckBox NewKindCheckBox(string caption) => new()
    {
        Text = caption,
        Checked = true,
        AutoSize = true,
        BackColor = SystemColors.Window,
        AccessibleName = $"{ShownTypesCaption}: {caption}",
    };

    /// <summary>The Shown Types caption without its access-key marker.</summary>
    static string ShownTypesCaption =>
        AccessKeyMarker.Replace(L10n.MenuShownTypes, string.Empty).Replace("&", string.Empty).Trim();

    /// <summary>
    /// A trailing access-key marker, as CJK menus spell it: 表示する種類(&amp;D).
    /// Dropping only the ampersand leaves "(D)" behind, and a screen reader reads
    /// it out — the caption was announced as "表示する種類(D): 画像". The whole
    /// parenthesis has to go. Latin captions put the marker inside the word
    /// (&amp;Shown Types), where removing the ampersand is already enough.
    /// </summary>
    static readonly System.Text.RegularExpressions.Regex AccessKeyMarker = new(@"\s*\(&[A-Za-z0-9]\)");

    /// <summary>
    /// Set while the two surfaces are being brought back into agreement, so the
    /// check boxes' own events do not re-enter the toggle they are reporting.
    /// </summary>
    bool _syncingKindToggles;

    // --- table view (§11.3) ------------------------------------------------
    // No AutoSizeMode here: ConfigureImageListGrid gives every column its mode
    // (None for the fixed ones, Fill for 警告) and AutoSizeContentColumns then
    // fits them to content. An AutoSizeMode set on the field would be silently
    // overwritten, and it would read as if the columns sized themselves — they
    // do not, and cannot: an auto-sized column is not user-resizable.
    readonly DataGridView _imageListGrid = new();
    readonly DataGridViewCheckBoxColumn _deleteColumn = new() { HeaderText = L10n.ColumnDelete };
    readonly DataGridViewImageColumn _thumbnailColumn = new() { HeaderText = L10n.ColumnThumbnail, ImageLayout = DataGridViewImageCellLayout.Zoom };
    readonly DataGridViewTextBoxColumn _objectIdColumn = new() { HeaderText = L10n.ColumnObjectId, ReadOnly = true };
    readonly DataGridViewTextBoxColumn _typeColumn = new() { HeaderText = L10n.ColumnType, ReadOnly = true };
    readonly DataGridViewTextBoxColumn _sizeColumn = new() { HeaderText = L10n.ColumnSize, ReadOnly = true };
    readonly DataGridViewTextBoxColumn _usageCountColumn = new() { HeaderText = L10n.ColumnUsageCount, ReadOnly = true };
    readonly DataGridViewTextBoxColumn _compressionColumn = new() { HeaderText = L10n.ColumnCompression, ReadOnly = true };
    readonly DataGridViewTextBoxColumn _estimatedSizeColumn = new() { HeaderText = L10n.ColumnEstimatedSize, ReadOnly = true };
    readonly DataGridViewTextBoxColumn _warningColumn = new() { HeaderText = L10n.ColumnWarning, ReadOnly = true };

    // --- workspace split ---------------------------------------------------
    // Flattening and deleting are opposite operations — one keeps the page's
    // appearance and empties its text layer, the other drops the appearance —
    // and a single ☑ column cannot mean both. They used to get a tab each, and
    // that hid flattening: a tab nobody opens is a feature nobody has. They
    // describe the same objects, so both are on screen at once instead — the
    // object list fills the window and the graphics objects panel docks to its
    // right, the way an image editor keeps its layers beside the canvas.
    //
    // FixedPanel is Panel2 so a drag holds: the panel stays the width the user
    // put it at. What it is worth when NOBODY has put it anywhere is decided by
    // ApplyWorkspaceSplitMetrics, and that is a share of the window rather than
    // a number of pixels — a fixed 300 came out as a sliver on a 2710-pixel
    // window, which made every resize the user's problem to drag back.
    readonly SplitContainer _workspaceSplit = new()
    {
        Dock = DockStyle.Fill,
        Orientation = Orientation.Vertical,
        FixedPanel = FixedPanel.Panel2,
    };
    readonly GraphicsObjectsPanel _graphicsObjectsPanel = new() { Dock = DockStyle.Fill };

    // Width of the graphics objects panel in LOGICAL pixels: seeded from the saved layout,
    // updated as the user drags, re-applied on every DPI change, written back at
    // shutdown. Held here rather than measured off the splitter on demand
    // because re-deriving it at a new scale would move the panel every time the
    // window changed monitor. Wide enough for a file name and an indented
    // object label is the starting point.
    int _graphicsObjectsPanelWidth = DefaultGraphicsObjectsPanelWidth;
    const int DefaultGraphicsObjectsPanelWidth = 300;

    // Whether that width is the USER's answer — dragged now or restored from a
    // session where they dragged it. Until it is, the panel takes a share of
    // the window instead, so a big window gets a big panel without anybody
    // having to ask for one. A drag is never overruled: the share is what to do
    // in the absence of a choice, not a correction of one.
    bool _graphicsObjectsPanelWidthIsTheUsersChoice;

    // The share is the golden section (see GoldenSection): a proportion rather
    // than a number of pixels answers the question the number could not — what
    // IS the right width for a panel beside a list, at whatever size the window
    // happens to be. The floor is there because below it the unit labels wrap,
    // and no proportion is worth an unreadable panel.
    const int MinimumSharedPanelWidth = 260;

    // Set by SplitterMoving, which ONLY fires while the user is dragging, and
    // consumed by the SplitterMoved that ends the drag. SplitterMoved alone is
    // not a signal that anything was chosen: the layout engine raises it too,
    // repeatedly, while the window is still being built — which recorded a
    // 1029-logical-pixel panel on a window that was showing 300.
    bool _workspaceSplitterDragged;

    // --- tile view ---------------------------------------------------------
    // One control that paints every tile itself. It replaced a panel holding
    // one control per object, which broke down at 2,015 of them.
    readonly TileView _tileView;

    // --- row/tile context menu ---------------------------------------------
    // One item, shown from either view's right-click. The group it acts on is
    // captured just before the menu opens.
    readonly ContextMenuStrip _rowContextMenu = new();
    readonly ToolStripMenuItem _usageLocationsMenuItem = new(L10n.ContextMenuUsageLocations);

    // Taking a picture back belongs HERE, on the picture's own row, and not in
    // the unit menu across the window: flattening ends the unit — its objects
    // became the picture — so by the time there is anything to undo, the unit
    // it came from is gone from the panel. What is left is this row.
    readonly ToolStripMenuItem _undoFlattenMenuItem = new(L10n.FlattenUndo);
    CrossFileImageGroup? _contextGroup;

    // --- status bar --------------------------------------------------------
    readonly StatusStrip _statusStrip = new();
    readonly ToolStripStatusLabel _statusLabel = new() { Text = L10n.StatusOpenPrompt, Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    readonly ToolStripProgressBar _progressIndicator = new() { Style = ProgressBarStyle.Marquee, Visible = false };

    // --- state -------------------------------------------------------------
    // Selection is keyed by the object's hash (not GroupId) because ids are
    // re-assigned whenever a file is added and the sort order shifts.
    readonly HashSet<string> _selectedHashes = new(StringComparer.Ordinal);
    // Bitmaps for what is on screen, backed by the on-disk store. Bounded by
    // the viewport, never by the size of the workspace.
    readonly ThumbnailCache _thumbnails;
    readonly Image _gridPlaceholderIcon = ImageListRow.CreatePlaceholderIcon();
    readonly Image _tilePlaceholderIcon = ImageListRow.CreatePlaceholderIcon(128, 96);
    // One icon per function, shared by the toolbar button and the menu item so
    // the same action always shows the same glyph. Not readonly: the bitmaps
    // bake in the theme's glyph color, so a theme change re-renders them
    // (RefreshToolbarIcons from OnSystemColorsChanged).
    Image _openIcon = ToolbarIcons.CreateOpenIcon();
    Image _saveIcon = ToolbarIcons.CreateSaveIcon();
    Image _selectAllIcon = ToolbarIcons.CreateSelectAllIcon();
    Image _clearSelectionIcon = ToolbarIcons.CreateClearSelectionIcon();
    bool _isTileView;
    bool _isBusy;
    bool _syncingSelection;

    // PDFs named on the command line, opened once the window is on screen.
    // Populated when the user drops files onto the app's icon.
    readonly IReadOnlyList<string> _startupPdfPaths;

    // The photograph this run was started to take, or null for an ordinary run.
    // A run holding a pose must not touch the remembered window placement: it
    // is a size and a splitter position chosen by a camera, not by the user.
    readonly ScreenshotRequest? _screenshot;

    // Timings for the two halves of opening a document. The form logs them
    // itself rather than calling through the workflow: they describe the UI's
    // own work, and the workflow has no business knowing about it.
    readonly ILogger _logger;

    // Supplies the file context the analyzer's reports lack.
    readonly OpenProgressReporter _openProgress = new();

    // Which object kinds the table / tile view currently shows (表示する種類).
    // At least one kind is always present.
    // Filled from _kindToggles in the constructor: every kind starts visible,
    // and there is one list of kinds rather than two that could disagree.
    readonly HashSet<RemovableKind> _visibleKinds = new();

    // Current sort. Defaults (and resets on every open) to 使用回数 descending.
    DataGridViewColumn _sortColumn = null!;
    bool _sortAscending;

    // The last sorted+filtered set the views render.
    CrossFileImageGroup[] _displayGroups = Array.Empty<CrossFileImageGroup>();

    // Cancels the background pass that renders and loads the viewport's
    // bitmaps. A rebuild disposes what the running pass is holding, so the old
    // pass has to stop before the new one starts.
    CancellationTokenSource? _thumbnailLoadCancellation;

    // Fires once the view has sat still long enough to be worth fetching
    // bitmaps for; restarted by every scroll and every rebuild.
    readonly System.Windows.Forms.Timer _thumbnailSettleTimer =
        new() { Interval = ThumbnailSettleMs };


    // Anchor row for Shift+click range checking in the ☑ column: the last row
    // clicked without Shift (i.e. the current row).
    int _checkAnchorRowIndex = -1;

    // Font used only for the ☑ delete-column header: the grid's UI font may
    // lack the ballot-box glyph outside Japanese locales, so we fall back to a
    // Windows standard symbol font. Null when the grid font already suffices.
    Font? _glyphHeaderFont;

    const int GridThumbnailMaxWidth = 90;
    const int GridThumbnailMaxHeight = 64;

    // Excel-like palette for the 表頭 / 表側 headers and gridlines: a FLAT pale
    // gray header (Excel uses no gradient) with a thin gray separator on the
    // bottom/right edges, and pale gray cell gridlines.
    //
    // Properties, not fields: under a high-contrast theme every fixed value
    // here would disappear against the theme's colors, so the palette defers
    // to SystemColors whenever HighContrast is on (accessibility review #4).
    // OnSystemColorsChanged re-applies the styles that captured these values.
    static bool HighContrast => SystemInformation.HighContrast;
    static Color HeaderFill => HighContrast ? SystemColors.Control : Color.FromArgb(0xF0, 0xF0, 0xF0);
    static Color HeaderBorder => HighContrast ? SystemColors.ControlDark : Color.FromArgb(0xC6, 0xC6, 0xC6);
    static Color HeaderText => HighContrast ? SystemColors.ControlText : Color.FromArgb(0x44, 0x44, 0x44);
    static Color GridLineColor => HighContrast ? SystemColors.ControlDark : Color.FromArgb(0xD6, 0xD6, 0xD6);
    // Windows' standard error red, dark enough to stay legible on the white row.
    // NOT on the blue selection highlight — it is illegible there, which is why
    // the warning cell keeps a light background when its row is selected. High
    // contrast drops the red: the theme owns all colors there, and the warning
    // text carries the meaning.
    static Color WarningText => WarningTextColour;

    /// <summary>
    /// The one red in the app. Shared with the graphics objects panel so the two places
    /// that warn about losing a whole page of content look like one warning.
    /// </summary>
    internal static Color WarningTextColour =>
        HighContrast ? SystemColors.WindowText : Color.FromArgb(0xC4, 0x2B, 0x1C);

    // Width reserved at the right of every header for the sort glyph, so any
    // column can become the sort key without its caption being clipped. Sized to
    // fit the (enlarged) triangle plus its right margin and a gap from the text.
    const int SortGlyphWidth = 20;

    // WM_SETREDRAW toggles a control's painting so a bulk rebuild (sort/filter)
    // does not repaint per row — the visible cause of the slow sort.
    const int WmSetRedraw = 0x000B;

    [DllImport("user32.dll")]
    static extern int SendMessage(IntPtr hWnd, int wMsg, bool wParam, int lParam);

    static void SuspendDrawing(Control control) =>
        SendMessage(control.Handle, WmSetRedraw, false, 0);

    static void ResumeDrawing(Control control)
    {
        SendMessage(control.Handle, WmSetRedraw, true, 0);
        control.Invalidate(invalidateChildren: true);
    }

    public MainForm(PdfCleaningWorkflow workflow, ThumbnailStore store, ILogger logger,
                    IReadOnlyList<string>? startupPdfPaths = null,
                    ScreenshotRequest? screenshot = null)
    {
        _screenshot = screenshot;
        _workflow = workflow;
        foreach (var toggle in _kindToggles) _visibleKinds.Add(toggle.Kind);
        _tileView = new TileView(TileVisualFor)
        {
            Dock = DockStyle.Fill,
            Visible = false,
        };
        _thumbnails = new ThumbnailCache(
            store,
            new Size(GridThumbnailMaxWidth, GridThumbnailMaxHeight),
            new Size(TileMetrics.ContentWidth, TileMetrics.ContentHeight));
        _logger = logger;
        _startupPdfPaths = startupPdfPaths ?? Array.Empty<string>();

        Text = L10n.AppTitle;
        // Window title-bar / taskbar icon from the embedded .ico (multi-size, so
        // the right resolution is picked per DPI).
        using (var iconStream = typeof(MainForm).Assembly.GetManifestResourceStream("appicon.ico"))
        {
            if (iconStream is not null) Icon = new Icon(iconStream);
        }
        MinimumSize = new Size(760, 480);
        // The golden section, like the workspace split inside it.
        ClientSize = new Size(920, 569);
        AllowDrop = true;

        // Restore the last window placement when the display arrangement is
        // unchanged; otherwise keep the default size above (centered by Windows).
        // The splitter sizes come back either way — they cannot put anything
        // off-screen, so a new monitor is no reason to forget them. Both are set
        // before the handles exist, which is when they are first applied.
        var savedLayout = _screenshot is null ? WindowLayoutStore.TryLoad() : null;
        if (savedLayout is not null)
        {
            if (WindowLayoutStore.PlacementIsUsable(savedLayout))
            {
                StartPosition = FormStartPosition.Manual;
                Bounds = new Rectangle(savedLayout.X, savedLayout.Y, savedLayout.Width, savedLayout.Height);
                if (savedLayout.Maximized) WindowState = FormWindowState.Maximized;
            }
            if (savedLayout.ObjectsPanelWidth > 0)
            {
                _graphicsObjectsPanelWidth = savedLayout.ObjectsPanelWidth;
                _graphicsObjectsPanelWidthIsTheUsersChoice = true;
            }
            _graphicsObjectsPanel.PreviewHeight = savedLayout.ObjectsPreviewHeight;
        }

        BuildMenu();
        BuildToolbar();
        BuildLayout();

        _openMenuItem.Click += OnOpenClicked;
        _saveMenuItem.Click += OnSaveClicked;
        _closeAllMenuItem.Click += OnCloseAllClicked;
        _exitMenuItem.Click += (_, _) => Close();
        _tableViewMenuItem.Click += (_, _) => SetViewMode(tileView: false);
        _tileViewMenuItem.Click += (_, _) => SetViewMode(tileView: true);
        // Both surfaces report the same intent — "this kind should be shown or
        // not" — and one method decides. A menu click is a toggle because the
        // item carries no state of its own; a check box already holds the
        // answer, so it passes what it now shows.
        foreach (var toggle in _kindToggles)
        {
            // The menu item toggles what it shows; the box reports what the user
            // just did to it. Both land in the same place, which is what keeps
            // the two surfaces from drifting.
            toggle.MenuItem.Click += (_, _) =>
                SetKindVisible(toggle.Kind, !_visibleKinds.Contains(toggle.Kind));
            toggle.Box.CheckedChanged += (_, _) => OnKindCheckChanged(toggle.Kind, toggle.Box);
        }
        _manualMenuItem.Click += OnManualClicked;
        _aboutMenuItem.Click += OnAboutClicked;

        _openToolButton.Click += OnOpenClicked;
        _saveToolButton.Click += OnSaveClicked;
        _selectAllToolButton.Click += OnSelectAllClicked;
        _clearSelectionToolButton.Click += OnClearSelectionClicked;

        _imageListGrid.CurrentCellDirtyStateChanged += OnGridCellDirtyStateChanged;
        _imageListGrid.CellValueChanged += OnGridCellValueChanged;
        _imageListGrid.CellPainting += OnGridCellPainting;
        _imageListGrid.ColumnHeaderMouseClick += OnColumnHeaderClicked;
        _imageListGrid.ColumnDividerDoubleClick += OnColumnDividerDoubleClick;
        // Whole ☑-cell hit area + Shift-range checking are handled via mouse
        // events (the built-in glyph-only toggle is disabled by ReadOnly cells).
        _imageListGrid.CellMouseDown += OnGridCellMouseDown;
        _imageListGrid.CellMouseUp += OnGridCellMouseUp;
        // …and by the space bar, which a read-only checkbox cell would ignore.
        _imageListGrid.KeyDown += OnGridKeyDown;
        // Scrolling either view restarts the settle timer; the tick is where
        // the viewport's bitmaps are actually fetched.
        _imageListGrid.Scroll += (_, _) => ScheduleThumbnailLoad();
        _tileView.ViewportChanged += (_, _) => ScheduleThumbnailLoad();
        _tileView.TileToggled += OnTileToggled;
        _tileView.RangeToggleRequested += OnTileRangeToggled;
        _tileView.TileContextRequested += OnTileContextRequested;

        _graphicsObjectsPanel.SelectionChanged += OnGraphicsObjectsSelectionChanged;
        _graphicsObjectsPanel.FlattenRequested += OnFlattenRequested;
        // An object is not drawn when this one placement of it is hidden, or
        // when the object itself is ticked for removal everywhere. Two marks
        // with two scopes, and the eye shows the result of both.
        _graphicsObjectsPanel.IsHidden = (filePath, pageNumber, placed) =>
            _workflow.IsPlacementHidden(filePath, pageNumber, placed)
            || (_workflow.ImageGroups.FirstOrDefault(g => g.Matches(placed)) is { } group
                && _selectedHashes.Contains(group.Hash));
        _graphicsObjectsPanel.VisibilityChangeRequested += OnObjectVisibilityChangeRequested;
        // A hidden object is shown by not being there: the preview renders a
        // copy of the document with those objects taken out, which the
        // workspace builds and keeps until they change.
        _graphicsObjectsPanel.PreviewSourceFor = filePath => _workflow.PreviewSourceAsync(filePath);
        // A hand-made merge or split goes back to the workspace, and the panel
        // is rebuilt from it — the panel describes the documents, so it must
        // not be the only place a correction exists.
        _graphicsObjectsPanel.UnitsEdited += (_, e) =>
        {
            _workflow.ReplaceOverlapRegions(e.FilePath, e.Units);
            _graphicsObjectsPanel.SetDocuments(_workflow.OpenDocuments);
            ShowGraphicsObjectsForCurrentRow();
        };
        // The panel holds no bitmaps and knows nothing of the workspace: it
        // asks while painting, so its rows can never outlive an eviction and it
        // never has to be told a thumbnail arrived.
        _graphicsObjectsPanel.ThumbnailFor = ObjectThumbnailFor;
        _graphicsObjectsPanel.ViewportChanged += (_, _) => ScheduleThumbnailLoad();
        // Whichever view is showing, moving the cursor re-aims the panel.
        _imageListGrid.CurrentCellChanged += (_, _) => ShowGraphicsObjectsForCurrentRow();
        _tileView.FocusedGroupChanged += (_, _) => ShowGraphicsObjectsForCurrentRow();

        _usageLocationsMenuItem.Click += OnUsageLocationsClicked;
        _undoFlattenMenuItem.Click += OnUndoFlattenRequested;
        _rowContextMenu.Items.AddRange(new ToolStripItem[]
        {
            _usageLocationsMenuItem, _undoFlattenMenuItem,
        });
        // Decided as it opens: there is one moment when it matters.
        _rowContextMenu.Opening += (_, _) =>
            _undoFlattenMenuItem.Enabled = FlattenBehindContextRow().Place is not null;
        _tileView.ToolTipFor = TileToolTipFor;
        _tileView.AccessibleNameFor = TileAccessibleNameFor;
        _thumbnailSettleTimer.Tick += OnThumbnailSettleTick;

        DragEnter += OnPdfDragEnter;
        DragDrop += OnPdfDragDrop;
        // Initial column sizing to the header widths once the grid has a handle.
        Load += (_, _) => AutoSizeContentColumns();
        // Remember where the user put the panel's edge. The panel's own
        // splitter reports itself; this one is the workspace split.
        _workspaceSplit.SplitterMoving += (_, _) => _workspaceSplitterDragged = true;
        _workspaceSplit.SplitterMoved += OnWorkspaceSplitterMoved;
        // Remember size/position, the display arrangement, and both splitters —
        // unless this run was posing for a photograph, whose size was chosen by
        // the camera and would overwrite the window the user actually arranged.
        FormClosing += (_, _) =>
        {
            if (_screenshot is null)
            {
                WindowLayoutStore.Save(this, _graphicsObjectsPanelWidth, _graphicsObjectsPanel.PreviewHeight);
            }
        };
        FormClosed += (_, _) => DisposeThumbnailImages(disposePlaceholder: true);
    }
}
