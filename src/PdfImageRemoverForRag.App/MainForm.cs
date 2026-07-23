using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using PdfImageRemoverForRag.Core.Errors;
using PdfImageRemoverForRag.Core.Formatting;
using PdfImageRemoverForRag.Core.Models;

namespace PdfImageRemoverForRag.App;

/// <summary>
/// The single-window UI: menu bar (ファイル / 表示 / ヘルプ), an icon toolbar,
/// an image list switchable between table and tile views, and a status bar.
/// Multiple PDFs can be open at once; identical images across files show as
/// one row/tile, and the per-file breakdown lives in the ファイル column and
/// its tooltip rather than a header panel. This class does layout and event
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
    // 表示列 submenu: per-type visibility filters. All three start checked;
    // CheckOnClick is off so MainForm can veto turning off the last one.
    readonly ToolStripMenuItem _shownTypesMenuItem = new(L10n.MenuShownTypes);
    readonly ToolStripMenuItem _showImagesMenuItem = new(L10n.MenuShowImages) { Checked = true, CheckOnClick = false };
    readonly ToolStripMenuItem _showShapesMenuItem = new(L10n.MenuShowShapes) { Checked = true, CheckOnClick = false };
    readonly ToolStripMenuItem _showTextMenuItem = new(L10n.MenuShowText) { Checked = true, CheckOnClick = false };
    readonly ToolStripMenuItem _manualMenuItem = new(L10n.MenuManual);
    readonly ToolStripMenuItem _aboutMenuItem = new(L10n.MenuAbout);

    // --- toolbar (icon buttons) --------------------------------------------
    readonly ToolStrip _toolStrip = new() { GripStyle = ToolStripGripStyle.Hidden };
    readonly ToolStripButton _openToolButton = new() { Enabled = true };
    readonly ToolStripButton _saveToolButton = new() { Enabled = false };
    readonly ToolStripButton _selectAllToolButton = new() { Enabled = false };
    readonly ToolStripButton _clearSelectionToolButton = new() { Enabled = false };

    // --- table view (§11.3) ------------------------------------------------
    // Every column except ファイル sizes to the wider of its header and its
    // content (AllCells); ファイル gets a fixed width of ~20 full-width
    // characters, computed in ConfigureImageListGrid.
    readonly DataGridView _imageListGrid = new();
    readonly DataGridViewCheckBoxColumn _deleteColumn = new() { HeaderText = L10n.ColumnDelete, AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells };
    readonly DataGridViewImageColumn _thumbnailColumn = new() { HeaderText = L10n.ColumnThumbnail, ImageLayout = DataGridViewImageCellLayout.Zoom, AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells };
    readonly DataGridViewTextBoxColumn _imageIdColumn = new() { HeaderText = L10n.ColumnImageId, ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells };
    readonly DataGridViewTextBoxColumn _typeColumn = new() { HeaderText = L10n.ColumnType, ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells };
    readonly DataGridViewTextBoxColumn _sizeColumn = new() { HeaderText = L10n.ColumnSize, ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells };
    readonly DataGridViewTextBoxColumn _usageCountColumn = new() { HeaderText = L10n.ColumnUsageCount, ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells };
    readonly DataGridViewTextBoxColumn _compressionColumn = new() { HeaderText = L10n.ColumnCompression, ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells };
    readonly DataGridViewTextBoxColumn _estimatedSizeColumn = new() { HeaderText = L10n.ColumnEstimatedSize, ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells };
    readonly DataGridViewTextBoxColumn _warningColumn = new() { HeaderText = L10n.ColumnWarning, ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells };

    // --- tile view ---------------------------------------------------------
    // One control that paints every tile itself. It replaced a panel holding
    // one control per object, which broke down at 2,015 of them.
    readonly TileView _tileView;

    // --- status bar --------------------------------------------------------
    readonly StatusStrip _statusStrip = new();
    readonly ToolStripStatusLabel _statusLabel = new() { Text = L10n.StatusOpenPrompt, Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    readonly ToolStripProgressBar _progressIndicator = new() { Style = ProgressBarStyle.Marquee, Visible = false };

    // --- state -------------------------------------------------------------
    // Selection is keyed by image hash (not GroupId) because ids are
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

    // Timings for the two halves of opening a document. The form logs them
    // itself rather than calling through the workflow: they describe the UI's
    // own work, and the workflow has no business knowing about it.
    readonly ILogger _logger;

    // Supplies the file context the analyzer's reports lack.
    readonly OpenProgressReporter _openProgress = new();

    // Which object types the table / tile view currently shows (表示列 filter).
    // At least one kind is always present.
    readonly HashSet<RemovableKind> _visibleKinds = new()
    {
        RemovableKind.Image, RemovableKind.Shape, RemovableKind.Text,
    };

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
    // Windows' standard error red, dark enough to stay legible on the white row
    // and on the blue selection highlight. High contrast drops the red — the
    // theme owns all colors there, and the warning text carries the meaning.
    static Color WarningText => HighContrast ? SystemColors.WindowText : Color.FromArgb(0xC4, 0x2B, 0x1C);

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
                    IReadOnlyList<string>? startupPdfPaths = null)
    {
        _workflow = workflow;
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
        ClientSize = new Size(920, 600);
        AllowDrop = true;

        // Restore the last window placement when the display arrangement is
        // unchanged; otherwise keep the default size above (centered by Windows).
        var savedLayout = WindowLayoutStore.TryLoad();
        if (savedLayout is not null)
        {
            StartPosition = FormStartPosition.Manual;
            Bounds = new Rectangle(savedLayout.X, savedLayout.Y, savedLayout.Width, savedLayout.Height);
            if (savedLayout.Maximized) WindowState = FormWindowState.Maximized;
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
        _showImagesMenuItem.Click += (_, _) => ToggleKindVisibility(RemovableKind.Image, _showImagesMenuItem);
        _showShapesMenuItem.Click += (_, _) => ToggleKindVisibility(RemovableKind.Shape, _showShapesMenuItem);
        _showTextMenuItem.Click += (_, _) => ToggleKindVisibility(RemovableKind.Text, _showTextMenuItem);
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
        // Scrolling either view restarts the settle timer; the tick is where
        // the viewport's bitmaps are actually fetched.
        _imageListGrid.Scroll += (_, _) => ScheduleThumbnailLoad();
        _tileView.ViewportChanged += (_, _) => ScheduleThumbnailLoad();
        _tileView.TileToggled += OnTileToggled;
        _tileView.ToolTipFor = TileToolTipFor;
        _tileView.AccessibleNameFor = TileAccessibleNameFor;
        _thumbnailSettleTimer.Tick += OnThumbnailSettleTick;

        DragEnter += OnPdfDragEnter;
        DragDrop += OnPdfDragDrop;
        // Initial column sizing to the header widths once the grid has a handle.
        Load += (_, _) => AutoSizeContentColumns();
        // Remember size/position (and the display arrangement) for next launch.
        FormClosing += (_, _) => WindowLayoutStore.Save(this);
        FormClosed += (_, _) => DisposeThumbnailImages(disposePlaceholder: true);
    }
}
