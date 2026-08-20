using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PdfImageRemoverForRag.Core.Formatting;
using PdfImageRemoverForRag.Core.Models;

namespace PdfImageRemoverForRag.App;

internal sealed partial class MainForm
{
    // =======================================================================
    // Workspace refresh — grid, tiles, info panel from workflow state
    // =======================================================================

    void RefreshWorkspace()
    {
        var groups = _workflow.ObjectGroups;

        // Selection survives rebuilds via hashes; drop entries whose object
        // is no longer present (e.g. after close-all or a save that removed it).
        _selectedHashes.RemoveWhere(hash => !groups.Any(g => g.Hash == hash));

        // Thumbnails are cached for every live group regardless of the type
        // filter, so toggling the filter never re-decodes bitmaps.
        RefreshThumbnailImages(groups);
        // The graphics objects panel reads the documents directly (overlap regions are
        // per page, not merged across files like the object groups), so it is
        // rebuilt from the same event rather than from the group list.
        //
        // BEFORE the views, not after: rebuilding them ends by pointing the
        // panel at the current row, and a panel rebuilt afterwards would clear
        // that again — which showed as an empty panel until the user moved.
        _graphicsObjectsPanel.SetDocuments(_workflow.OpenDocuments);
        RebuildDisplay();
    }

    /// <summary>
    /// Rebuild the table and tile views from the current workspace, honoring
    /// the Shown Types filter and the active sort. Thumbnails are assumed
    /// already current (see <see cref="RefreshThumbnailImages"/>).
    /// </summary>
    void RebuildDisplay()
    {
        _displayGroups = SortForDisplay(
            _workflow.ObjectGroups.Where(g => _visibleKinds.Contains(g.Kind))).ToArray();

        // Show the hourglass and block column resizing for the duration of the
        // rebuild so a header drag can't fight the sort/refresh in progress.
        // Cursor.Current (not UseWaitCursor) so it shows during this synchronous
        // block — UseWaitCursor only updates on the next message-pump cycle.
        var previousCursor = Cursor.Current;
        Cursor.Current = Cursors.WaitCursor;
        _objectListGrid.AllowUserToResizeColumns = false;
        try
        {
            // The grid is always rebuilt — it's cheap and is the source of truth
            // for column auto-sizing and focus. Tiles are far more expensive (one
            // control per row), so they are rebuilt lazily: only when the tile
            // view is actually showing, otherwise marked dirty for the next switch.
            RebuildGridRows(_displayGroups);
            // Both views are refreshed every time. Handing the tile view its
            // list is O(1) now that it paints instead of building controls, so
            // the old "rebuild it lazily on first show" state is gone.
            RebuildTiles(_displayGroups);
            UpdateSelectionState();
            // Re-aim the graphics objects panel at whatever the cursor ended up on. The
            // grid's CurrentCellChanged does fire during the rebuild, but on the
            // first row added — before its Tag names a group — so the panel
            // would be left describing nothing until the user moved.
            ShowGraphicsObjectsForCurrentRow();
            // Whichever view is showing, fetch the bitmaps its viewport needs.
            ScheduleThumbnailLoad();
            // Re-assert which view is on screen: a rebuild must never leave the
            // View menu claiming one view while the other is showing.
            ApplyViewVisibility();
            // The sort glyph is drawn by PaintExcelHeader from _sortColumn /
            // _sortAscending; the rebuild's invalidate repaints the headers.
        }
        finally
        {
            _objectListGrid.AllowUserToResizeColumns = true;
            Cursor.Current = previousCursor;
        }
    }

    /// <summary>
    /// Order groups by the active sort column and direction. A stable
    /// secondary key (usage descending, then hash) keeps ties in a sensible,
    /// repeatable order.
    /// </summary>
    IEnumerable<CrossFileObjectGroup> SortForDisplay(IEnumerable<CrossFileObjectGroup> groups)
    {
        var ordered = _sortAscending
            ? groups.OrderBy(SortKey)
            : groups.OrderByDescending(SortKey);
        return ordered
            .ThenByDescending(g => g.UsageCount)
            .ThenBy(g => g.Hash, StringComparer.Ordinal);
    }

    /// <summary>Sort key for the active column — matches what the cell shows.</summary>
    IComparable SortKey(CrossFileObjectGroup group)
    {
        if (_sortColumn == _deleteColumn) return _selectedHashes.Contains(group.Hash) ? 0 : 1;
        if (_sortColumn == _objectIdColumn) return group.GroupId;
        if (_sortColumn == _typeColumn) return (int)group.Kind;
        if (_sortColumn == _sizeColumn) return SizeSortValue(group);
        if (_sortColumn == _usageCountColumn) return group.UsageCount;
        if (_sortColumn == _compressionColumn) return ObjectDisplay.CompressionLabel(group);
        if (_sortColumn == _estimatedSizeColumn) return group.EstimatedSize;
        if (_sortColumn == _warningColumn) return WarningSortValue(group);
        return group.UsageCount; // thumbnail column and any fallback
    }

    // Size compares by pixel area for images/shapes and character count for
    // text — the same magnitude the cell conveys.
    static double SizeSortValue(CrossFileObjectGroup group) => group.Kind == RemovableKind.Text
        ? group.TextValue?.Length ?? 0
        : (double)group.PixelWidth * group.PixelHeight;

    // Warning ordering: not-removable first, then possible full-page, then clear.
    static int WarningSortValue(CrossFileObjectGroup group)
    {
        if (!group.IsSafelyRemovable) return 0;
        if (group.IsPossibleFullPageImage) return 1;
        return 2;
    }

    void ResetSortToDefault()
    {
        _sortColumn = _usageCountColumn;
        _sortAscending = false;
    }

    void OnColumnHeaderClicked(object? sender, DataGridViewCellMouseEventArgs e)
    {
        if (e.ColumnIndex < 0) return;
        var column = _objectListGrid.Columns[e.ColumnIndex];
        // The thumbnail column has no meaningful ordering.
        if (column == _thumbnailColumn) return;

        if (column == _sortColumn)
        {
            _sortAscending = !_sortAscending;
        }
        else
        {
            _sortColumn = column;
            // Numeric-style columns read most-useful largest-first.
            _sortAscending = column != _usageCountColumn
                          && column != _estimatedSizeColumn
                          && column != _sizeColumn;
        }
        RebuildDisplay();
    }

    /// <summary>
    /// Double-clicking a column divider auto-fits the column to the LEFT of it
    /// to its content (like a spreadsheet). e.ColumnIndex is that left column.
    /// </summary>
    void OnColumnDividerDoubleClick(object? sender, DataGridViewColumnDividerDoubleClickEventArgs e)
    {
        if (e.ColumnIndex < 0) return;
        // The Fill column sizes itself to the remaining width; auto-fitting it
        // to content would just be undone by the fill layout.
        if (_objectListGrid.Columns[e.ColumnIndex].AutoSizeMode == DataGridViewAutoSizeColumnMode.Fill) return;
        _objectListGrid.AutoResizeColumn(e.ColumnIndex, DataGridViewAutoSizeColumnMode.AllCells);
        e.Handled = true;
    }

    /// <summary>
    /// Show or hide one removable kind. Enforces the "at least one kind
    /// visible" rule: the last remaining kind cannot be turned off.
    ///
    /// The menu and the toolbar both come here, and both are put back in step
    /// afterwards — including when the request is refused, which is what makes
    /// a check box bounce back instead of showing a filter that is not applied.
    /// </summary>
    void SetKindVisible(RemovableKind kind, bool visible)
    {
        bool alreadyVisible = _visibleKinds.Contains(kind);
        // Refused or redundant: nothing changes, but the surfaces may still be
        // out of step — a check box the user just cleared is showing the wrong
        // answer until it is told otherwise.
        if (visible == alreadyVisible || (!visible && _visibleKinds.Count == 1))
        {
            SyncKindToggles();
            return;
        }

        if (visible) _visibleKinds.Add(kind);
        else _visibleKinds.Remove(kind);
        SyncKindToggles();
        RebuildDisplay();
    }

    /// <summary>
    /// A check box reporting what the user just did to it. Ignored while the
    /// two surfaces are being synchronised, or the correction would be read as
    /// a fresh instruction and undo itself.
    /// </summary>
    void OnKindCheckChanged(RemovableKind kind, CheckBox box)
    {
        if (_syncingKindToggles) return;
        SetKindVisible(kind, box.Checked);
    }

    /// <summary>
    /// Make every menu item and check box agree with the filter that is
    /// actually applied. One writer for both surfaces, so they cannot drift.
    /// </summary>
    void SyncKindToggles()
    {
        _syncingKindToggles = true;
        try
        {
            foreach (var toggle in _kindToggles)
            {
                toggle.MenuItem.Checked = toggle.Box.Checked = _visibleKinds.Contains(toggle.Kind);
            }
        }
        finally
        {
            _syncingKindToggles = false;
        }
    }

    void RefreshThumbnailImages(IReadOnlyList<CrossFileObjectGroup> groups)
    {
        // Eviction disposes bitmaps, so no load pass may be mid-flight.
        CancelThumbnailLoad();
        _thumbnails.Retain(groups.Select(g => g.Hash));
    }

    /// <summary>Stop the background load, if one is running.</summary>
    void CancelThumbnailLoad()
    {
        _thumbnailLoadCancellation?.Cancel();
        _thumbnailLoadCancellation = null;
    }

    // =======================================================================
    // Viewport-driven thumbnail loading
    // =======================================================================

    /// <summary>
    /// How long the view must sit still before thumbnails are fetched for it.
    /// Half a second is about twice a person's reaction time to a visual
    /// change, so a scroll that is still moving never triggers work, and one
    /// that has stopped feels immediate.
    /// </summary>
    const int ThumbnailSettleMs = 250;

    /// <summary>
    /// Restart the settle timer. Called from every scroll and from every
    /// rebuild — without the rebuild case nothing would ever load until the
    /// user happened to scroll.
    /// </summary>
    void ScheduleThumbnailLoad()
    {
        _thumbnailSettleTimer.Stop();
        _thumbnailSettleTimer.Start();
    }

    async void OnThumbnailSettleTick(object? sender, EventArgs e)
    {
        _thumbnailSettleTimer.Stop();
        await LoadVisibleThumbnailsAsync();
    }

    /// <summary>
    /// Render and load the bitmaps for what is on screen, plus as much again
    /// on either side so a short scroll lands on ready pixels. Everything
    /// outside that window is disposed.
    /// </summary>
    async Task LoadVisibleThumbnailsAsync()
    {
        if (_displayGroups.Length == 0) return;

        CancelThumbnailLoad();
        var cancellation = new CancellationTokenSource();
        _thumbnailLoadCancellation = cancellation;

        var window = CurrentViewportWindow();
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = await _thumbnails.LoadWindowAsync(window, cancellation.Token);
            ApplyLoadedThumbnails();

            // Only worth a line when work actually happened; a settled view
            // ticks over with nothing to do. Attempted and rendered are logged
            // separately because "tried 37" and "37 succeeded" look identical
            // in a single counter and mean opposite things.
            if (result.Attempted > 0)
            {
                var (residentGrid, residentTile) = _thumbnails.ResidentCounts;
                _logger.LogInformation(
                    "thumbnails: window={Window} attempted={Attempted} rendered={Rendered} " +
                    "failed={Failed} residentGrid={ResidentGrid} residentTile={ResidentTile} " +
                    "view={View} elapsedMs={ElapsedMs}",
                    window.Count, result.Attempted, result.Rendered, result.Failed,
                    residentGrid, residentTile, _isTileView ? "tile" : "table",
                    stopwatch.ElapsedMilliseconds);
            }
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer scroll or rebuild.
        }
        finally
        {
            if (ReferenceEquals(_thumbnailLoadCancellation, cancellation))
            {
                _thumbnailLoadCancellation = null;
            }
            cancellation.Dispose();
        }
    }

    /// <summary>
    /// The objects to keep bitmaps for: the visible range grown by its own
    /// length on each side (so "visible x2" in total, as specified), clamped
    /// to the list.
    /// </summary>
    IReadOnlyList<CrossFileObjectGroup> CurrentViewportWindow()
    {
        // The graphics objects panel draws thumbnails too, and its rows are not a slice
        // of the object list — they are whichever objects share a unit with the
        // current row, which can sit anywhere in it. So its visible rows are
        // added to the window rather than assumed to be inside it.
        var alsoNeeded = _graphicsObjectsPanel.VisibleThumbnailGroups();

        var (first, count) = _isTileView ? VisibleTileRange() : VisibleRowRange();
        if (count <= 0) return alsoNeeded;

        int margin = Math.Max(count, 1) / 2;
        int start = Math.Max(0, first - margin);
        int end = Math.Min(_displayGroups.Length, first + count + margin);
        if (alsoNeeded.Count == 0) return _displayGroups[start..end];

        var window = new List<CrossFileObjectGroup>(end - start + alsoNeeded.Count);
        window.AddRange(_displayGroups[start..end]);
        window.AddRange(alsoNeeded);
        return window;
    }

    /// <summary>First visible row and how many rows are showing.</summary>
    (int First, int Count) VisibleRowRange()
    {
        if (_objectListGrid.Rows.Count == 0) return (0, 0);

        int first = Math.Max(0, _objectListGrid.FirstDisplayedScrollingRowIndex);
        // Partially visible rows count: their thumbnails are on screen too.
        int count = Math.Max(1, _objectListGrid.DisplayedRowCount(includePartialRow: true));
        return (first, count);
    }

    /// <summary>First visible tile and how many tiles are showing.</summary>
    (int First, int Count) VisibleTileRange() => _tileView.VisibleRange();

    /// <summary>
    /// Push the freshly loaded bitmaps into whichever view is showing. Both
    /// views are updated from the same window so switching views does not have
    /// to wait for another pass.
    /// </summary>
    void ApplyLoadedThumbnails()
    {
        // Neither view holds a bitmap: both ask the cache while painting, so
        // there is nothing to hand over and nothing that can outlive an
        // eviction. Repainting is the whole update.
        if (_isTileView) _tileView.Invalidate();
        else ApplyLoadedRowThumbnails();
        // And the panel, which draws thumbnails of its own from the same cache.
        // It was left out, so its rows stayed empty until the pointer crossed
        // them and Windows repainted for its own reasons.
        _graphicsObjectsPanel.RefreshThumbnails();
    }

    /// <summary>
    /// Repaint the visible thumbnail cells now that their bitmaps are loaded.
    /// The cells hold no value, so this is an invalidate and nothing more.
    /// </summary>
    void ApplyLoadedRowThumbnails()
    {
        var (first, count) = VisibleRowRange();
        int last = Math.Min(_objectListGrid.Rows.Count, first + count);
        for (int i = Math.Max(0, first); i < last; i++)
        {
            _objectListGrid.InvalidateCell(_thumbnailColumn.Index, i);
        }
    }

    void RebuildGridRows(IReadOnlyList<CrossFileObjectGroup> groups)
    {
        _syncingSelection = true;
        SuspendDrawing(_objectListGrid);
        // Temporarily drop the Fill column so it does not recompute its width on
        // every single Rows.Add — that recompute is O(rows) per add, i.e. the
        // dominant cost when adding hundreds of rows.
        var savedWarningMode = _warningColumn.AutoSizeMode;
        _warningColumn.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
        try
        {
            _objectListGrid.Rows.Clear();
            foreach (var group in groups)
            {
                // Text rows carry no image (null); their thumbnail cell is drawn
                // as ellipsized text by OnGridCellPainting. Image and shape rows
                // use a bitmap (shapes are rendered from their geometry).
                // The thumbnail cell holds no value at all: PaintThumbnailCell
                // asks the cache at paint time, so no cell can outlive the
                // bitmap it would otherwise be holding.
                object? thumbnailCell = null;

                int rowIndex = _objectListGrid.Rows.Add(
                    _selectedHashes.Contains(group.Hash),
                    // The row takes its values as a non-null array, so the empty
                    // cell has to be handed over as one. It is read back as null.
                    thumbnailCell!,
                    group.GroupId,
                    ObjectDisplay.TypeLabel(group),
                    ObjectDisplay.SizeLabel(group),
                    group.UsageCount,
                    ObjectDisplay.CompressionLabel(group),
                    ByteSizeFormatter.Format(group.EstimatedSize),
                    ObjectDisplay.WarningLabel(group));

                var row = _objectListGrid.Rows[rowIndex];
                row.Tag = group;
                // Row-gutter number = display position (1-based), reassigned every
                // rebuild so it stays sequential top-to-bottom under any sort.
                row.HeaderCell.Value = (rowIndex + 1).ToString();
                if (group.Kind == RemovableKind.Text)
                {
                    // Full text in the tooltip since the cell may be truncated.
                    row.Cells[_thumbnailColumn.Index].ToolTipText = group.TextValue ?? string.Empty;
                }
                else if (group.Kind is RemovableKind.Shape or RemovableKind.Drawing)
                {
                    row.Cells[_thumbnailColumn.Index].ToolTipText = ObjectDisplay.SizeLabel(group);
                }

                var warningToolTip = ObjectDisplay.WarningToolTip(group);
                if (warningToolTip.Length > 0)
                {
                    row.Cells[_warningColumn.Index].ToolTipText = warningToolTip;
                }
                // Toggling is handled manually in OnGridCellMouseUp (whole-cell
                // hit area), so the built-in glyph toggle is disabled everywhere.
                row.Cells[_deleteColumn.Index].ReadOnly = true;
                if (!group.IsSafelyRemovable)
                {
                    // §14.3: unsafe rows cannot be checked (and render grayed).
                    row.DefaultCellStyle.ForeColor = SystemColors.GrayText;
                }
                else if (group.IsPossibleFullPageImage)
                {
                    // Only this warning goes red: checking such a row blanks a
                    // whole page. "Not removable" is a state, not a hazard, so it
                    // stays in the row's normal (grayed) color.
                    //
                    // It keeps its own colors while the row is selected, and
                    // that is not a flourish: a selected cell is painted in
                    // SelectionForeColor — white on the blue highlight — so the
                    // red was simply gone, and a file holding ONE object is
                    // selected from the moment it opens. Red on that blue is
                    // illegible (about 1.2:1), so the cell stays out of the
                    // highlight rather than joining it in a color nobody can
                    // read.
                    var warning = row.Cells[_warningColumn.Index].Style;
                    warning.ForeColor = WarningText;
                    warning.SelectionForeColor = WarningText;
                    warning.SelectionBackColor = SystemColors.Window;
                }
            }
        }
        finally
        {
            _warningColumn.AutoSizeMode = savedWarningMode;
            FitRowHeaderWidth();
            ResumeDrawing(_objectListGrid);
            _syncingSelection = false;
        }
    }

    /// <summary>
    /// Size the row-number gutter from the widest number it will hold.
    ///
    /// One measurement instead of the grid's own auto-sizing, which measures
    /// every header again on every assignment (see the note where
    /// RowHeadersWidthSizeMode is set).
    /// </summary>
    void FitRowHeaderWidth()
    {
        int digits = Math.Max(2, _objectListGrid.Rows.Count.ToString().Length);
        int text = TextRenderer.MeasureText(
            new string('0', digits), _objectListGrid.RowHeadersDefaultCellStyle.Font
                                     ?? _objectListGrid.Font).Width;
        _objectListGrid.RowHeadersWidth = Math.Max(Dip(24), text + Dip(12));
    }

    /// <summary>
    /// Hand the tile view its contents. There is no per-object control to
    /// create any more, so this is O(1) work regardless of the document size —
    /// the old version built one control per object and could not cope.
    /// </summary>
    void RebuildTiles(IReadOnlyList<CrossFileObjectGroup> groups)
    {
        _tileView.SetItems(groups);
        ScheduleThumbnailLoad();
    }

    /// <summary>How one group is drawn in the tile view, asked at paint time.</summary>
    TileVisual TileVisualFor(CrossFileObjectGroup group)
    {
        // Text draws its string; everything else draws a bitmap when one is
        // resident, the placeholder when it can never be produced, and says so
        // in words while it is still on its way.
        var text = ObjectDisplay.ThumbnailText(group);
        var bitmap = text is null ? _thumbnails.Tile(group.Hash) : null;
        bool unrenderable = text is null && bitmap is null
                            && _thumbnails.IsUnrenderable(group.Hash);

        return new TileVisual(
            Kind: group.Kind,
            Thumbnail: bitmap ?? (unrenderable ? _tilePlaceholderIcon : null),
            TextContent: text,
            IsThumbnailPending: text is null && bitmap is null && !unrenderable,
            IsChecked: _selectedHashes.Contains(group.Hash),
            IsCheckable: group.IsSafelyRemovable,
            UsageCount: group.UsageCount);
    }

    /// <summary>Tooltip text for one tile: the full string, size, or warning.</summary>
    string? TileToolTipFor(CrossFileObjectGroup group)
    {
        if (group.Kind == RemovableKind.Text) return group.TextValue ?? string.Empty;
        if (group.Kind is RemovableKind.Shape or RemovableKind.Drawing) return ObjectDisplay.SizeLabel(group);
        var warning = ObjectDisplay.WarningToolTip(group);
        return warning.Length > 0 ? warning : null;
    }

    /// <summary>
    /// Screen-reader name for one tile: what the object is and how often it is
    /// used. Composed from already-localized pieces (the type label and the
    /// usage-count column header), so no new translated string is needed. The
    /// checked / not-removable state is reported separately as a UIA state flag,
    /// which the screen reader voices in the user's language on its own.
    /// </summary>
    string TileAccessibleNameFor(CrossFileObjectGroup group)
    {
        var type = ObjectDisplay.TypeLabel(group);
        var usage = $"{L10n.ColumnUsageCount} {group.UsageCount}";
        // A text tile's string is its most useful identity, so include it.
        return group.Kind == RemovableKind.Text
            ? $"{type}: {group.TextValue}, {usage}"
            : $"{type}, {usage}";
    }

    // =======================================================================
    // Usage-locations window (right-click Show Usage Locations…)
    // =======================================================================

    /// <summary>
    /// Open the usage-locations window for one object: every file and page it
    /// is drawn on, with the object's location outlined on a full-page
    /// thumbnail. The data comes straight from the group's per-file
    /// occurrences — no document is re-opened here (the window renders pages
    /// itself, on demand).
    /// </summary>
    void OpenUsageLocations(CrossFileObjectGroup group)
    {
        using var dialog = new UsageLocationsDialog(UsageWindowTitle(group), BuildUsageRows(group));
        dialog.ShowDialog(this);
    }

    /// <summary>
    /// One row per (file, page): the object's bounding boxes on that page,
    /// ordered by file (open order) then page. All three kinds carry
    /// coordinates, so a string shown three times on a page outlines three
    /// places, the same as an image drawn three times.
    /// </summary>
    static IReadOnlyList<UsageRow> BuildUsageRows(CrossFileObjectGroup group)
    {
        var rows = new List<UsageRow>();
        foreach (var file in group.FileOccurrences)
        {
            var name = Path.GetFileNameWithoutExtension(file.FilePath);
            foreach (var pageGroup in file.Occurrences.GroupBy(o => o.PageNumber).OrderBy(p => p.Key))
            {
                // A rule is a path of zero height ("495x0 pt" in the object
                // list), so an occurrence only has to extend in ONE direction
                // to be worth outlining; the window gives such a box a visible
                // thickness when it draws it.
                var boxes = pageGroup
                    .Where(o => o.Width > 0 || o.Height > 0)
                    .Select(o => new RectangleF((float)o.X, (float)o.Y, (float)o.Width, (float)o.Height))
                    .ToArray();
                rows.Add(new UsageRow(file.FilePath, name, pageGroup.Key, boxes));
            }
        }
        return rows;
    }

    /// <summary>Window title: the clean menu caption plus a short object label.</summary>
    static string UsageWindowTitle(CrossFileObjectGroup group)
    {
        var caption = L10n.ContextMenuUsageLocations.Replace("&", string.Empty).TrimEnd('…', '.');
        var label = group.Kind == RemovableKind.Text
            ? ObjectDisplay.ThumbnailText(group) ?? group.GroupId
            : $"{ObjectDisplay.TypeLabel(group)} {group.GroupId}";
        return $"{caption} — {label}";
    }

    // =======================================================================
    // View switching (View menu)
    // =======================================================================

    void SetViewMode(bool tileView)
    {
        _isTileView = tileView;
        ApplyViewVisibility();
        // Switching view changes which range is visible, so the newly shown
        // view asks for its own thumbnails.
        ScheduleThumbnailLoad();
    }

    /// <summary>
    /// Make the visible view match <see cref="_isTileView"/> — which is what
    /// the View menu's check marks report.
    ///
    /// Called from <see cref="SetViewMode"/> and again from every
    /// <see cref="RebuildDisplay"/>, because opening a file while the tile view
    /// was showing has been observed to leave the table on screen with the menu
    /// still checked on Tiles. The mechanism is not understood — nothing in
    /// this code sets either Visible outside this method — so rather than guess
    /// at it, the invariant is simply re-asserted after every rebuild. It is two
    /// property writes; WinForms ignores both when the value is unchanged.
    /// </summary>
    void ApplyViewVisibility()
    {
        _tableViewMenuItem.Checked = !_isTileView;
        _tileViewMenuItem.Checked = _isTileView;
        _objectListGrid.Visible = !_isTileView;
        _tileView.Visible = _isTileView;
    }
}
