namespace PdfImageRemoverForRag.App;

internal sealed partial class MainForm
{
    // =======================================================================
    // Layout
    // =======================================================================

    void BuildMenu()
    {
        // Same icons as the matching toolbar buttons.
        _openMenuItem.Image = _openIcon;
        _saveMenuItem.Image = _saveIcon;

        var fileMenu = new ToolStripMenuItem(L10n.MenuFile);
        fileMenu.DropDownItems.AddRange(new ToolStripItem[]
        {
            _openMenuItem, _saveMenuItem, _closeAllMenuItem,
            new ToolStripSeparator(), _exitMenuItem,
        });
        _shownTypesMenuItem.DropDownItems.AddRange(new ToolStripItem[]
        {
            _showImagesMenuItem, _showShapesMenuItem, _showDrawingsMenuItem,
            _showShadowsMenuItem, _showTextMenuItem,
        });
        var viewMenu = new ToolStripMenuItem(L10n.MenuView);
        viewMenu.DropDownItems.AddRange(new ToolStripItem[]
        {
            _tableViewMenuItem, _tileViewMenuItem,
            new ToolStripSeparator(), _shownTypesMenuItem,
        });
        var helpMenu = new ToolStripMenuItem(L10n.MenuHelp);
        helpMenu.DropDownItems.AddRange(new ToolStripItem[]
        {
            _manualMenuItem, new ToolStripSeparator(), _aboutMenuItem,
        });

        _menuStrip.Items.AddRange(new ToolStripItem[] { fileMenu, viewMenu, helpMenu });
        MainMenuStrip = _menuStrip;
    }

    void BuildToolbar()
    {
        // Windows 11 command-bar look: flat white strip, larger monochrome icons,
        // rounded highlight on hover (via the custom renderer).
        _toolStrip.Renderer = new Windows11ToolStripRenderer();
        _toolStrip.BackColor = SystemColors.Window;
        _toolStrip.AutoSize = true;
        // ImageScalingSize + button padding are DPI-scaled in ApplyDpiDependentLayout.
        _toolStrip.Padding = new Padding(6, 4, 6, 4);

        // Open / Save: icon only — the glyphs are self-explanatory and the label
        // lives in the tooltip.
        SetIconOnly(_openToolButton, _openIcon, L10n.ToolOpen);
        SetIconOnly(_saveToolButton, _saveIcon, L10n.ToolSave);
        // Select-all / clear: keep a caption so the two are unmistakable.
        SetIconAndText(_selectAllToolButton, _selectAllIcon, L10n.ToolSelectAll);
        SetIconAndText(_clearSelectionToolButton, _clearSelectionIcon, L10n.ToolClearSelection);

        _toolStrip.Items.AddRange(new ToolStripItem[]
        {
            _openToolButton, _saveToolButton,
            new ToolStripSeparator(),
            _selectAllToolButton, _clearSelectionToolButton,
            _filterSeparator,
            // The kind filters last, so the commands keep the positions the
            // user already knows. Five short captions with no icons: the
            // caption IS the filter, and a glyph for "text" next to a glyph for
            // "drawing" would be two more things to learn.
            HostKindCheckBox(_showImagesCheck),
            HostKindCheckBox(_showShapesCheck),
            HostKindCheckBox(_showDrawingsCheck),
            HostKindCheckBox(_showShadowsCheck),
            HostKindCheckBox(_showTextCheck),
        });

        // Sizes (ImageScalingSize, button padding) are DPI-dependent and set in
        // ApplyDpiDependentLayout; here we only set content/display.
        static void SetIconOnly(ToolStripButton button, Image icon, string toolTip)
        {
            button.Image = icon;
            button.DisplayStyle = ToolStripItemDisplayStyle.Image;
            button.AutoToolTip = false;
            button.ToolTipText = toolTip;
            // An icon-only button has no Text for UI Automation to derive a name
            // from — a screen reader would announce nothing. The tooltip text is
            // NOT the accessible name, so set it explicitly. Same string, so the
            // spoken name matches the visible label (WCAG 2.5.3 Label in Name).
            button.AccessibleName = toolTip;
            button.Margin = new Padding(2, 1, 2, 1);
        }

        // Put one filter box on the strip. The tooltip carries the submenu's
        // caption because four bare nouns do not say what they are filtering,
        // and a visible label for them would cost width the toolbar may not
        // have at 200 %.
        static ToolStripControlHost HostKindCheckBox(CheckBox box) =>
            new(box)
            {
                AutoSize = true,
                ToolTipText = ShownTypesCaption,
                // Hosted controls are not painted by the ToolStrip renderer, so
                // the host has to carry the strip's own colour or the box sits
                // on a grey patch.
                BackColor = SystemColors.Window,
            };

        static void SetIconAndText(ToolStripButton button, Image icon, string caption)
        {
            button.Image = icon;
            button.Text = caption;
            button.DisplayStyle = ToolStripItemDisplayStyle.ImageAndText;
            button.AutoToolTip = false;
            button.Margin = new Padding(2, 1, 2, 1);
        }
    }


    // =======================================================================
    // DPI — scale custom-drawn/sized UI to the monitor (200% on the test VM)
    // =======================================================================

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyDpiDependentLayout();
    }

    /// <summary>
    /// Open the PDFs passed on the command line, once the window is visible.
    ///
    /// Deferred to OnShown rather than the constructor so the user sees the
    /// window and its "解析しています…" status while the analysis runs, instead
    /// of a delay before anything appears. No workspace exists yet at startup,
    /// so this skips the replace-confirmation the other open paths need.
    /// </summary>
    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (_startupPdfPaths.Count > 0) await OpenPdfFilesAsync(_startupPdfPaths);
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        ApplyDpiDependentLayout();
    }

    /// <summary>
    /// Apply all DPI-dependent sizes: the toolbar glyph size and button padding
    /// (16- and 10-logical px), and a column re-fit. Called on handle creation
    /// and whenever the monitor DPI changes.
    /// </summary>
    void ApplyDpiDependentLayout()
    {
        // 16-logical glyphs; 10-logical padding all round → 36-logical buttons.
        _toolStrip.ImageScalingSize = new Size(Dip(16), Dip(16));
        // Horizontal padding is what sets the distance between neighbours: two
        // adjacent buttons are separated by one's right padding plus the
        // other's left, and nothing else. At 10 each that came to 20 logical px
        // between one command and the next, which read as five loose buttons
        // rather than two pairs. Halved sideways; the vertical padding is what
        // gives the hover highlight its height and is left alone.
        _openToolButton.Padding = new Padding(Dip(5), Dip(10), Dip(5), Dip(10));
        _saveToolButton.Padding = new Padding(Dip(5), Dip(10), Dip(5), Dip(10));
        _selectAllToolButton.Padding = new Padding(Dip(5), Dip(10), Dip(6), Dip(10));
        _clearSelectionToolButton.Padding = new Padding(Dip(5), Dip(10), Dip(6), Dip(10));

        // The filter boxes size themselves from their text, but the gap between
        // them is a hand-picked number and so has to be scaled like every other
        // one. 8 logical px apart, which keeps five of them from reading as one
        // block without spending the width a separator would.
        foreach (var box in new[]
                 {
                     _showImagesCheck, _showShapesCheck, _showDrawingsCheck,
                     _showShadowsCheck, _showTextCheck,
                 })
        {
            box.Margin = new Padding(Dip(4), Dip(2), Dip(4), Dip(2));
        }

        // The gap the separator leaves on its right, before the first filter
        // box. The separator's own default is nearly nothing, so the line and
        // the first check box read as one crowded lump; the space has to come
        // from the separator because the boxes' own margins are what sets their
        // spacing from each other.
        _filterSeparator.Margin = new Padding(Dip(2), 0, Dip(12), 0);

        // The tile view measures itself by hand, so its bitmap size follows the
        // scale here. TileView reads the metrics through its own Dip and needs
        // no help beyond a repaint.
        if (_thumbnails.SetTileSize(
                new Size(Dip(TileMetrics.ContentWidth), Dip(TileMetrics.ContentHeight))))
        {
            // The bitmaps built at the old scale were just thrown away; the
            // settle timer fetches them again at the new one.
            _tileView.Invalidate();
            ScheduleThumbnailLoad();
        }

        ApplyWorkspaceSplitMetrics();

        if (_imageListGrid.Columns.Count > 0) AutoSizeContentColumns();
    }

    /// <summary>
    /// Give the flatten panel the width in <see cref="_flattenPanelWidth"/>,
    /// which is in logical pixels. Re-applied on a DPI change because
    /// <see cref="SplitContainer.FixedPanel"/> holds a panel to its size in
    /// device pixels — left alone, the panel would shrink by half when the
    /// window moved to a 200 % display.
    /// </summary>
    void ApplyWorkspaceSplitMetrics()
    {
        _workspaceSplit.SplitterWidth = Math.Max(4, Dip(4));
        _workspaceSplit.Panel1MinSize = Dip(320);
        _workspaceSplit.Panel2MinSize = Dip(200);

        int wanted = Dip(_flattenPanelWidth);
        int room = _workspaceSplit.Width
                 - _workspaceSplit.Panel1MinSize - _workspaceSplit.SplitterWidth;
        if (room < _workspaceSplit.Panel2MinSize) return;

        int panelWidth = Math.Max(_workspaceSplit.Panel2MinSize, Math.Min(wanted, room));
        _workspaceSplit.SplitterDistance =
            _workspaceSplit.Width - panelWidth - _workspaceSplit.SplitterWidth;
    }

    /// <summary>
    /// Record where the user dragged the workspace splitter to, so the width
    /// survives a DPI change now and a restart later. Only a move that began
    /// with <see cref="SplitContainer.SplitterMoving"/> counts — see the field.
    /// </summary>
    void OnWorkspaceSplitterMoved(object? sender, SplitterEventArgs e)
    {
        if (!_workspaceSplitterDragged) return;
        _workspaceSplitterDragged = false;
        int panelWidth = _workspaceSplit.Width
                       - _workspaceSplit.SplitterDistance - _workspaceSplit.SplitterWidth;
        _flattenPanelWidth = (int)Math.Round(panelWidth * 96.0 / DeviceDpi);
    }

    void BuildLayout()
    {
        ConfigureImageListGrid();

        // Table and tile views share one host panel; visibility is toggled
        // by the 表示 menu. Neither needs a header of its own: what a header
        // would say about one file is wrong as soon as several are open.
        var viewHost = new Panel { Dock = DockStyle.Fill };
        viewHost.Controls.Add(_imageListGrid);
        viewHost.Controls.Add(_tileView);

        // Object list left, flatten tree right — both always visible; see the
        // comment on the field for why they are not tabs and cannot be one list.
        // The tree stays put when the view switches to tiles: it describes the
        // document, not the way the objects happen to be drawn.
        _workspaceSplit.Panel1.Controls.Add(viewHost);
        _workspaceSplit.Panel2.Controls.Add(_flattenPanel);

        _statusStrip.Items.Add(_statusLabel);
        _statusStrip.Items.Add(_progressIndicator);

        // Dock order: fill content first, then top strips, so the menu ends
        // up above the toolbar and both above the content.
        Controls.Add(_workspaceSplit);
        Controls.Add(_toolStrip);
        Controls.Add(_menuStrip);
        Controls.Add(_statusStrip);
    }

    void ConfigureImageListGrid()
    {
        _imageListGrid.Dock = DockStyle.Fill;
        _imageListGrid.AllowUserToAddRows = false;
        _imageListGrid.AllowUserToDeleteRows = false;
        _imageListGrid.AllowUserToResizeRows = false;
        _imageListGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _imageListGrid.MultiSelect = false;
        // Tab leaves the grid instead of walking its cells. The default is the
        // other way round, which made the grid a keyboard trap: once focus
        // arrived there it could not be moved on to the toolbar or the Flatten
        // panel, and a keyboard-only user was stuck. Nothing is lost by it —
        // whole rows are selected, so the arrow keys are how you move here, and
        // Ctrl+Tab still steps through cells for anyone who wants that.
        _imageListGrid.StandardTab = true;
        _imageListGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        // Every row is cloned from this template, so replacing it is what gives
        // all of them the screen-reader name that counts from 1. Set before the
        // height below, since assigning the template discards the old one.
        _imageListGrid.RowTemplate = new ObjectListRow();
        _imageListGrid.RowTemplate.Height = 68;
        // Trailing area past the last column reads as empty spreadsheet space.
        _imageListGrid.BackgroundColor = SystemColors.Window;
        // Users can drag column boundaries to resize (§ resize requirement);
        // double-click auto-fit is wired via ColumnDividerDoubleClick.
        _imageListGrid.AllowUserToResizeColumns = true;

        // Excel-style 表頭 / 表側: the column-header row and the row-number gutter
        // are custom-painted (OnGridCellPainting → PaintExcelHeader) with the
        // sampled highlight / shadow colors. Visual styles off so our painting is
        // not overdrawn; the border styles below are just the non-painted fallback.
        _imageListGrid.EnableHeadersVisualStyles = false;
        _imageListGrid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
        _imageListGrid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;

        // 表側 = row-number gutter (DataGridView row headers), same Excel look.
        _imageListGrid.RowHeadersVisible = true;
        _imageListGrid.RowHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
        // Width is set by hand in FitRowHeaderWidth after each rebuild.
        //
        // NOT AutoSizeToAllHeaders: that re-measures every row header each time
        // one header value is assigned, so filling 2,015 row numbers cost
        // ~2,000 x 2,000 text measurements — 3 minutes 37 seconds, measured.
        // (It was also why sorting 254 rows used to take 2.5 seconds.)
        _imageListGrid.RowHeadersWidthSizeMode = DataGridViewRowHeadersWidthSizeMode.DisableResizing;
        _imageListGrid.RowHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;

        _imageListGrid.CellBorderStyle = DataGridViewCellBorderStyle.Single;

        // The palette values captured by cell styles live in one place so a
        // theme change (notably high contrast on/off) can re-apply them.
        ApplyGridPalette();
        _thumbnailColumn.DefaultCellStyle.NullValue = null;

        _imageListGrid.Columns.AddRange(
            _deleteColumn, _thumbnailColumn, _objectIdColumn, _typeColumn, _sizeColumn,
            _usageCountColumn, _compressionColumn, _estimatedSizeColumn, _warningColumn);

        // Give the ☑ column a spoken name: its glyph header reads as "ballot box"
        // to a screen reader otherwise. Replacing the header cell clears its
        // value, so re-apply the glyph as the header text on the new cell.
        _deleteColumn.HeaderCell = new DeleteColumnHeaderCell();
        _deleteColumn.HeaderText = L10n.ColumnDelete;

        // Give the thumbnail cells a spoken name — they hold no value, so a
        // screen reader would announce them as empty. Swapping the template
        // resets the column's ImageLayout (it lives on the template cell), so
        // re-apply it.
        _thumbnailColumn.CellTemplate = new ThumbnailCell();
        _thumbnailColumn.ImageLayout = DataGridViewImageCellLayout.Zoom;

        // Non-Fill columns are None mode = fixed pixel width, which is what makes
        // them user-resizable AND lets AutoSizeContentColumns set them to fit
        // content. These are just pre-Load fallbacks; AutoSizeContentColumns
        // refines them on open / file-open.
        SetColumnWidth(_deleteColumn, 46);
        SetColumnWidth(_thumbnailColumn, 100);
        SetColumnWidth(_objectIdColumn, 76);
        SetColumnWidth(_typeColumn, 60);
        SetColumnWidth(_sizeColumn, 92);
        SetColumnWidth(_usageCountColumn, 64);
        SetColumnWidth(_compressionColumn, 90);
        SetColumnWidth(_estimatedSizeColumn, 90);
        // The rightmost column fills the leftover width (帳尻合わせ): on a wider
        // window it grows, so the table always tracks the window with no gap; on
        // a narrower window the other (fixed) columns force a horizontal scroll.
        _warningColumn.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        _warningColumn.MinimumWidth = 100;

        // Column alignment: ☑ / タイプ / 圧縮 centered; サイズ / 使用回数 / 推定容量
        // right-aligned (numeric-style columns). Header cells match their body.
        SetColumnAlignment(_deleteColumn, DataGridViewContentAlignment.MiddleCenter);
        // No left/edge padding so the centered checkbox has no excess margin.
        _deleteColumn.DefaultCellStyle.Padding = Padding.Empty;
        SetColumnAlignment(_typeColumn, DataGridViewContentAlignment.MiddleCenter);
        SetColumnAlignment(_compressionColumn, DataGridViewContentAlignment.MiddleCenter);
        SetColumnAlignment(_sizeColumn, DataGridViewContentAlignment.MiddleRight);
        SetColumnAlignment(_usageCountColumn, DataGridViewContentAlignment.MiddleRight);
        SetColumnAlignment(_estimatedSizeColumn, DataGridViewContentAlignment.MiddleRight);

        // Sorting is handled manually (the grid is unbound and rows are drawn
        // with custom painting), so every column uses Programmatic sort mode —
        // this lets us show a sort glyph without the built-in sort firing.
        foreach (DataGridViewColumn column in _imageListGrid.Columns)
        {
            column.SortMode = DataGridViewColumnSortMode.Programmatic;
        }

        // The ☑ header glyph (U+2611) is not in every UI font outside Japanese
        // locales; swap the header's font to a Windows standard symbol font
        // when the grid font cannot render it.
        _glyphHeaderFont = ResolveGlyphHeaderFont(_imageListGrid.Font);
        if (_glyphHeaderFont is not null)
        {
            _deleteColumn.HeaderCell.Style.Font = _glyphHeaderFont;
        }

        // Initial / reset sort order: 使用回数 descending (§ open behaviour).
        _sortColumn = _usageCountColumn;
        _sortAscending = false;
    }

    /// <summary>
    /// Apply the header / gridline palette to the styles that capture color
    /// VALUES (a cell style stores the ARGB it was given, unlike a property
    /// holding a SystemColors known-color, which re-resolves on every paint).
    /// Called once from <see cref="ConfigureImageListGrid"/> and again from
    /// <see cref="OnSystemColorsChanged"/> so a high-contrast switch takes
    /// effect without restarting.
    /// </summary>
    void ApplyGridPalette()
    {
        _imageListGrid.ColumnHeadersDefaultCellStyle.BackColor = HeaderFill;
        _imageListGrid.ColumnHeadersDefaultCellStyle.ForeColor = HeaderText;
        // Selecting a cell must not recolor its header (no white-on-select text).
        _imageListGrid.ColumnHeadersDefaultCellStyle.SelectionBackColor = HeaderFill;
        _imageListGrid.ColumnHeadersDefaultCellStyle.SelectionForeColor = HeaderText;
        // Same on the row gutter: a selected row keeps dark text on gray (the
        // framework otherwise draws it white — invisible on our light header).
        _imageListGrid.RowHeadersDefaultCellStyle.BackColor = HeaderFill;
        _imageListGrid.RowHeadersDefaultCellStyle.ForeColor = HeaderText;
        _imageListGrid.RowHeadersDefaultCellStyle.SelectionBackColor = HeaderFill;
        _imageListGrid.RowHeadersDefaultCellStyle.SelectionForeColor = HeaderText;
        // Pale gray cell gridlines (Excel-like) instead of the default dark ones.
        _imageListGrid.GridColor = GridLineColor;
        // Window (not literal white) keeps transparent images readable (§12)
        // while following the theme's background under high contrast.
        _thumbnailColumn.DefaultCellStyle.BackColor = SystemColors.Window;
    }

    /// <summary>
    /// React to a theme change — in particular high contrast being turned on
    /// or off while the app runs. The captured cell-style colors and the
    /// pre-rendered toolbar glyphs are the two things that do NOT follow the
    /// theme by themselves; per-row warning colors are re-applied by the
    /// rebuild. Everything painted live already reads the palette properties.
    /// </summary>
    protected override void OnSystemColorsChanged(EventArgs e)
    {
        base.OnSystemColorsChanged(e);
        ApplyGridPalette();
        RefreshToolbarIcons();
        if (!_isBusy && _workflow.OpenDocuments.Count > 0) RebuildDisplay();
        Invalidate(invalidateChildren: true);
    }

    /// <summary>
    /// Re-render the toolbar glyphs in the current theme's color and swap them
    /// onto every button and menu item that shows them. The old bitmaps are
    /// disposed only after nothing references them.
    /// </summary>
    void RefreshToolbarIcons()
    {
        var oldIcons = new[] { _openIcon, _saveIcon, _selectAllIcon, _clearSelectionIcon };

        _openIcon = ToolbarIcons.CreateOpenIcon();
        _saveIcon = ToolbarIcons.CreateSaveIcon();
        _selectAllIcon = ToolbarIcons.CreateSelectAllIcon();
        _clearSelectionIcon = ToolbarIcons.CreateClearSelectionIcon();

        _openToolButton.Image = _openIcon;
        _saveToolButton.Image = _saveIcon;
        _selectAllToolButton.Image = _selectAllIcon;
        _clearSelectionToolButton.Image = _clearSelectionIcon;
        _openMenuItem.Image = _openIcon;
        _saveMenuItem.Image = _saveIcon;

        foreach (var icon in oldIcons) icon.Dispose();
    }

    static void SetColumnAlignment(DataGridViewColumn column, DataGridViewContentAlignment alignment)
    {
        column.DefaultCellStyle.Alignment = alignment;
        column.HeaderCell.Style.Alignment = alignment;
    }

    // Fixed starting width; AutoSizeMode None is what makes the column
    // user-resizable (auto-sized columns cannot be dragged).
    static void SetColumnWidth(DataGridViewColumn column, int width)
    {
        column.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
        column.Width = width;
    }

    /// <summary>Scale a 96-DPI logical pixel value to the current device DPI
    /// (200% on the test VM), so custom drawing/sizing is not half size.</summary>
    int Dip(int logical) => LogicalToDeviceUnits(logical);

    /// <summary>
    /// Above this many rows, column fitting measures only the rows on screen.
    /// AllCells measures every cell of every column — 2,015 rows x 9 columns is
    /// ~18,000 text measurements and takes long enough to look like a hang.
    /// </summary>
    const int AllCellsMeasurementLimit = 300;

    /// <summary>
    /// Size each fixed-width column to the larger of (a) its widest DATA cell and
    /// (b) its header caption plus room for a space and the sort glyph — the same
    /// <see cref="SortGlyphWidth"/> reserved by <see cref="PaintExcelHeader"/>, so
    /// a column becoming the sort key never clips. The rightmost column is Fill
    /// and only gets a content floor (it absorbs slack / forces a scrollbar).
    /// Called on open / file-open / close / post-save — not on sort or filter,
    /// so a manual drag survives until the data set next changes.
    /// </summary>
    void AutoSizeContentColumns()
    {
        // GetPreferredWidth is DPI-aware and covers the header caption + the
        // widest data cell; we then add the (DPI-scaled) sort-glyph reserve so
        // any column can become the sort key without clipping.
        int glyphReserve = Dip(SortGlyphWidth);
        // On a big document, fit to what is visible instead. A value further
        // down can then be wider than its column, but it stays readable via the
        // scroll bar and a manual double-click on the divider still fits it —
        // that is a better trade than freezing for tens of seconds on open.
        var measurement = _imageListGrid.Rows.Count > AllCellsMeasurementLimit
            ? DataGridViewAutoSizeColumnMode.DisplayedCells
            : DataGridViewAutoSizeColumnMode.AllCells;
        foreach (DataGridViewColumn column in _imageListGrid.Columns)
        {
            int content = column.GetPreferredWidth(measurement, fixedHeight: true);
            if (column == _deleteColumn)
            {
                // The ☑ cell has no text; count it as two characters wide.
                int twoChars = TextRenderer.MeasureText("00", _imageListGrid.Font).Width;
                content = Math.Max(content, twoChars);
            }

            int width = content + glyphReserve;
            if (column == _warningColumn)
            {
                _warningColumn.MinimumWidth = width;
            }
            else
            {
                column.Width = width;
            }
        }
    }

    /// <summary>
    /// Pick a font for the ☑ delete-column header. The grid's UI font may lack
    /// the ballot-box glyph (U+2611) outside Japanese locales, and GDI+ offers
    /// no reliable glyph-coverage query — so we hand the header to a Windows
    /// standard symbol font that is guaranteed to contain it. Returns null (keep
    /// the grid font) only when none of those fonts is installed.
    /// </summary>
    static Font? ResolveGlyphHeaderFont(Font gridFont)
    {
        // Segoe UI Symbol / Segoe UI Emoji ship with every supported Windows and
        // both contain the ballot-box glyphs. Constructing a Font with a missing
        // family silently substitutes another, so confirm the resolved name.
        foreach (var familyName in new[] { "Segoe UI Symbol", "Segoe UI Emoji" })
        {
            var candidate = new Font(familyName, gridFont.Size);
            if (string.Equals(candidate.Name, familyName, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
            candidate.Dispose();
        }
        return null;
    }
}
