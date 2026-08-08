using PdfImageRemoverForRag.Core.Models;

namespace PdfImageRemoverForRag.App;

internal sealed partial class MainForm
{
    // =======================================================================
    // Selection — hash-keyed, mirrored into whichever views exist
    // =======================================================================

    void OnGridCellDirtyStateChanged(object? sender, EventArgs e)
    {
        // Commit checkbox edits immediately so CellValueChanged fires per click.
        if (_objectListGrid.IsCurrentCellDirty)
        {
            _objectListGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        }
    }

    void OnGridCellPainting(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        // Header cells (column headers, row-number gutter, top-left corner) get
        // the Excel-style paint; the framework then draws their text + sort glyph.
        if (e.RowIndex == -1 || e.ColumnIndex == -1)
        {
            PaintExcelHeader(e);
            e.Handled = true;
            return;
        }

        // The thumbnail column is painted entirely by hand.
        //
        // Text groups draw their string, left-aligned and ellipsized — never
        // rasterized. Everything else draws whatever bitmap is resident right
        // now, asked for at paint time. That last part is not a style choice:
        // the cache disposes bitmaps as the viewport moves, and a cell holding
        // a reference to one would be drawing a disposed image the moment it
        // scrolled out of the window.
        if (e.RowIndex < 0 || e.ColumnIndex != _thumbnailColumn.Index) return;
        if (_objectListGrid.Rows[e.RowIndex].Tag is not CrossFileObjectGroup group) return;

        bool selected = (e.State & DataGridViewElementStates.Selected) != 0;
        var text = ObjectDisplay.ThumbnailText(group);
        if (text is null)
        {
            PaintThumbnailCell(e, group, selected);
            e.Handled = true;
            return;
        }

        e.PaintBackground(e.CellBounds, selected);
        var bounds = Rectangle.Inflate(e.CellBounds, -Dip(4), -Dip(2));
        var color = selected ? e.CellStyle!.SelectionForeColor : e.CellStyle!.ForeColor;
        const TextFormatFlags flags = TextFormatFlags.Left
            | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
        TextRenderer.DrawText(e.Graphics!, text, e.CellStyle.Font, bounds, color, flags);
        e.Handled = true;
    }

    /// <summary>
    /// Draw one image or shape row's thumbnail, scaled to fit and centered.
    /// A row whose bitmap is not loaded yet simply stays empty — it fills in
    /// within the settle interval — while one the store gave up on shows the
    /// placeholder icon, so "not ready" and "cannot be shown" never look alike.
    /// </summary>
    void PaintThumbnailCell(
        DataGridViewCellPaintingEventArgs e, CrossFileObjectGroup group, bool selected)
    {
        e.PaintBackground(e.CellBounds, selected);

        var bitmap = _thumbnails.Grid(group.Hash)
                     ?? (_thumbnails.IsUnrenderable(group.Hash) ? _gridPlaceholderIcon : null);
        if (bitmap is null) return;

        var area = Rectangle.Inflate(e.CellBounds, -Dip(2), -Dip(2));
        if (area.Width <= 0 || area.Height <= 0) return;

        e.Graphics!.DrawImage(bitmap, Fit.Inside(bitmap.Size, area, mayEnlarge: false));
    }

    /// <summary>
    /// Paint an Excel-like header cell entirely by hand — column header,
    /// row-number gutter, or the top-left corner. Everything (background, text,
    /// sort glyph) is drawn here and the framework is NOT asked to paint content,
    /// so there is no current-row marker in the row gutter and no selected-column
    /// highlight in the column header. Flat pale gray fill (no gradient) with a thin gray
    /// bottom/right separator, matching Excel's flat headers.
    /// </summary>
    void PaintExcelHeader(DataGridViewCellPaintingEventArgs e)
    {
        var bounds = e.CellBounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        var g = e.Graphics!;

        // Flat pale fill (no gradient — Excel headers are flat) plus a thin gray
        // separator on the bottom and right edges to delineate cells.
        using (var fill = new SolidBrush(HeaderFill))
        {
            g.FillRectangle(fill, bounds);
        }
        using (var border = new Pen(HeaderBorder))
        {
            g.DrawLine(border, bounds.Left, bounds.Bottom - 1, bounds.Right - 1, bounds.Bottom - 1);
            g.DrawLine(border, bounds.Right - 1, bounds.Top, bounds.Right - 1, bounds.Bottom - 1);
        }

        if (e.RowIndex == -1 && e.ColumnIndex >= 0)
        {
            // Column header: caption drawn inside the area LEFT of the reserved
            // sort-glyph zone. The zone is reserved ALWAYS (sorted or not) so the
            // caption's position never shifts when the column becomes the sort key.
            var column = _objectListGrid.Columns[e.ColumnIndex];
            bool sorted = column == _sortColumn && column != _thumbnailColumn;
            var textBounds = Rectangle.Inflate(bounds, -Dip(6), -Dip(2));
            textBounds.Width -= Dip(SortGlyphWidth);

            var font = column.HeaderCell.Style.Font
                       ?? _objectListGrid.ColumnHeadersDefaultCellStyle.Font
                       ?? _objectListGrid.Font;
            var flags = ToTextFlags(column.HeaderCell.Style.Alignment) | TextFormatFlags.EndEllipsis;
            TextRenderer.DrawText(g, column.HeaderText, font, textBounds, HeaderText, flags);
            if (sorted) DrawSortGlyph(g, bounds);
        }
        else if (e.ColumnIndex == -1 && e.RowIndex >= 0)
        {
            // Row-number gutter: the number, centered; no current-row marker.
            // NoPadding so the built-in left glyph padding does not push the
            // digits right (which made multi-digit numbers look right-aligned).
            var value = _objectListGrid.Rows[e.RowIndex].HeaderCell.Value?.ToString();
            if (!string.IsNullOrEmpty(value))
            {
                TextRenderer.DrawText(g, value, _objectListGrid.Font, bounds, HeaderText,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                    | TextFormatFlags.NoPadding);
            }
        }
        // The top-left corner (both -1) gets background only.
    }

    /// <summary>
    /// Draw the sort-direction indicator at the right edge of a header cell using
    /// the Windows icon font — a light chevron (ascending → up, descending →
    /// down), not a heavy CJK triangle.
    /// </summary>
    void DrawSortGlyph(Graphics g, Rectangle bounds)
    {
        // Segoe Fluent Icons / MDL2: ChevronUp / ChevronDown.
        string glyph = _sortAscending ? "\uE70E" : "\uE70D"; // ChevronUp / ChevronDown
        using var font = ToolbarIcons.ResolveIconFont(Dip(8));
        var area = new Rectangle(
            bounds.Right - Dip(SortGlyphWidth), bounds.Top, Dip(SortGlyphWidth), bounds.Height);
        TextRenderer.DrawText(g, glyph, font, area, HeaderText,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    static TextFormatFlags ToTextFlags(DataGridViewContentAlignment alignment) => alignment switch
    {
        DataGridViewContentAlignment.TopCenter or DataGridViewContentAlignment.MiddleCenter
            or DataGridViewContentAlignment.BottomCenter
            => TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter,
        DataGridViewContentAlignment.TopRight or DataGridViewContentAlignment.MiddleRight
            or DataGridViewContentAlignment.BottomRight
            => TextFormatFlags.Right | TextFormatFlags.VerticalCenter,
        _ => TextFormatFlags.Left | TextFormatFlags.VerticalCenter,
    };

    void OnGridCellMouseDown(object? sender, DataGridViewCellMouseEventArgs e)
    {
        if (e.RowIndex < 0) return;

        // Right-click shows the row context menu on the row under the pointer,
        // the same menu the tile view shows.
        if (e.Button == MouseButtons.Right)
        {
            ShowRowContextMenu(e.RowIndex);
            return;
        }

        // Track the range anchor: the last row clicked without Shift becomes the
        // current row and the anchor for a subsequent Shift+click.
        if ((ModifierKeys & Keys.Shift) == 0) _checkAnchorRowIndex = e.RowIndex;
    }

    void OnGridCellMouseUp(object? sender, DataGridViewCellMouseEventArgs e)
    {
        // Whole-cell hit area for the ☑ column: a left click anywhere in the cell
        // toggles it. Shift+click checks (or unchecks) the whole range from the
        // anchor row to the clicked row.
        if (e.Button != MouseButtons.Left) return;
        if (e.RowIndex < 0 || e.ColumnIndex != _deleteColumn.Index) return;
        if (_objectListGrid.Rows[e.RowIndex].Cells[_deleteColumn.Index].Value is not { } current) return;

        // New state is the opposite of the clicked cell's current state; the whole
        // range (for Shift) or just this row (otherwise) is set to it.
        bool newState = current is not true;
        bool shift = (ModifierKeys & Keys.Shift) != 0
                     && _checkAnchorRowIndex >= 0
                     && _checkAnchorRowIndex < _objectListGrid.Rows.Count;
        ToggleDeleteRange(shift ? _checkAnchorRowIndex : e.RowIndex, e.RowIndex, newState);
    }

    /// <summary>
    /// Space toggles the current row's ☑; Shift+Space extends from the anchor
    /// row, the same as Shift+click.
    ///
    /// Without this the column is reachable by keyboard but not operable by it.
    /// Its cells are <c>ReadOnly</c> so that the built-in glyph-only toggle stays
    /// out of the way of the whole-cell hit area the mouse handler provides — and
    /// a read-only checkbox cell ignores the space bar, so nothing happened at
    /// all. The tile view has answered Space since it was written; this is the
    /// table catching up.
    ///
    /// It acts on the row rather than on the ☑ cell alone, so arrowing down the
    /// list and pressing space works whichever column the cursor is in. Nothing
    /// else in this grid uses the space bar.
    /// </summary>
    void OnGridKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Space || e.Control || e.Alt) return;

        int rowIndex = _objectListGrid.CurrentCell?.RowIndex ?? -1;
        if (rowIndex < 0 || rowIndex >= _objectListGrid.Rows.Count) return;
        if (_objectListGrid.Rows[rowIndex].Cells[_deleteColumn.Index].Value is not { } current) return;

        // Plain Space is also what sets the anchor, mirroring a click without
        // Shift — so Shift+Space always extends from the last row deliberately
        // toggled, whether that was done with the mouse or the keyboard.
        bool shift = e.Shift
                     && _checkAnchorRowIndex >= 0
                     && _checkAnchorRowIndex < _objectListGrid.Rows.Count;
        if (!shift) _checkAnchorRowIndex = rowIndex;

        ToggleDeleteRange(shift ? _checkAnchorRowIndex : rowIndex, rowIndex, current is not true);
        // Handled AND suppressed: the grid hands an unhandled key on to the
        // current cell, and the character would otherwise ring the buffer.
        e.Handled = true;
        e.SuppressKeyPress = true;
    }

    /// <summary>
    /// Set every safely-removable row between the two indices (inclusive, in
    /// either order) to <paramref name="newState"/>. The mouse and the keyboard
    /// both come through here so the two cannot drift apart.
    /// </summary>
    void ToggleDeleteRange(int anchorRow, int currentRow, bool newState)
    {
        int from = Math.Min(anchorRow, currentRow);
        int to = Math.Max(anchorRow, currentRow);

        _syncingSelection = true;
        try
        {
            for (int r = from; r <= to; r++)
            {
                var row = _objectListGrid.Rows[r];
                // Only safely-removable rows can be checked (§14.3).
                if (row.Tag is not CrossFileObjectGroup group || !group.IsSafelyRemovable) continue;
                SetSelected(group.Hash, newState);
                row.Cells[_deleteColumn.Index].Value = newState;
            }
        }
        finally
        {
            _syncingSelection = false;
        }
        UpdateSelectionState();
        RefreshSelectionStatus();
    }

    void OnGridCellValueChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (_syncingSelection || e.RowIndex < 0 || e.ColumnIndex != _deleteColumn.Index) return;
        var row = _objectListGrid.Rows[e.RowIndex];
        if (row.Tag is not CrossFileObjectGroup group) return;

        bool isChecked = row.Cells[_deleteColumn.Index].Value is true;
        SetSelected(group.Hash, isChecked);
        _tileView.Invalidate();
        UpdateSelectionState();
        RefreshSelectionStatus();
    }

    void OnTileToggled(object? sender, CrossFileObjectGroup group)
    {
        if (_syncingSelection) return;
        bool isChecked = !_selectedHashes.Contains(group.Hash);
        SetSelected(group.Hash, isChecked);
        SyncGridRowCheckState(group.Hash, isChecked);
        UpdateSelectionState();
        RefreshSelectionStatus();
    }

    /// <summary>
    /// Apply a tile-view Shift+click range: set every group in the range to the
    /// requested state. Mirrors the grid's Shift+click; both views are synced
    /// from <see cref="_selectedHashes"/> afterwards.
    /// </summary>
    void OnTileRangeToggled(IReadOnlyList<CrossFileObjectGroup> groups, bool select)
    {
        if (_syncingSelection) return;
        foreach (var group in groups) SetSelected(group.Hash, select);
        SyncAllViewCheckStates();
        UpdateSelectionState();
        RefreshSelectionStatus();
    }

    void SetSelected(string hash, bool selected)
    {
        if (selected) _selectedHashes.Add(hash);
        else _selectedHashes.Remove(hash);
        // The panel draws the same fact as an eye, so it has to be told:
        // a tick made in the list closes the eye on the other side of the
        // window, and nothing else would keep the two in step.
        _graphicsObjectsPanel.RefreshVisibility();
    }

    // --- row/tile context menu (Show Usage Locations…) --------------------------------

    /// <summary>
    /// Capture the right-clicked grid row's group and show the menu at the
    /// pointer.
    ///
    /// This is driven from the grid's mouse-down rather than from
    /// <c>CellContextMenuStripNeeded</c>: that event only fires for a grid with
    /// a DataSource or in virtual mode, and this one is neither, so the table
    /// view had no context menu at all while the tile view did.
    /// </summary>
    void ShowRowContextMenu(int rowIndex)
    {
        if (_objectListGrid.Rows[rowIndex].Tag is not CrossFileObjectGroup group) return;
        _contextGroup = group;
        _rowContextMenu.Show(_objectListGrid, _objectListGrid.PointToClient(Cursor.Position));
    }

    /// <summary>Capture the right-clicked tile's group and show the menu there.</summary>
    void OnTileContextRequested(CrossFileObjectGroup group, Point location)
    {
        _contextGroup = group;
        _rowContextMenu.Show(_tileView, location);
    }

    void OnUsageLocationsClicked(object? sender, EventArgs e)
    {
        if (_contextGroup is { } group) OpenUsageLocations(group);
    }

    void SyncGridRowCheckState(string hash, bool isChecked)
    {
        _syncingSelection = true;
        try
        {
            foreach (DataGridViewRow row in _objectListGrid.Rows)
            {
                if (row.Tag is CrossFileObjectGroup g && g.Hash == hash)
                {
                    row.Cells[_deleteColumn.Index].Value = isChecked;
                    break;
                }
            }
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    void OnSelectAllClicked(object? sender, EventArgs e)
    {
        // These act on the object list only, never on the graphics objects
        // panel. Both are on screen now, so "everything" would have to mean
        // both — and one click that bakes every overlap on every page is not
        // something to offer beside a button whose usual job is ticking a list.
        // Flattening is asked for one unit at a time, from that unit's own
        // menu, which is the granularity it acts at.
        //
        // Select only what the list currently shows: the Shown Types filter scopes
        // "select all" so hidden kinds are never silently marked for removal.
        foreach (var group in _workflow.ObjectGroups
                     .Where(g => g.IsSafelyRemovable && _visibleKinds.Contains(g.Kind)))
        {
            _selectedHashes.Add(group.Hash);
        }
        SyncAllViewCheckStates();
        UpdateSelectionState();
        RefreshSelectionStatus();
    }

    void OnClearSelectionClicked(object? sender, EventArgs e)
    {
        _selectedHashes.Clear();
        SyncAllViewCheckStates();
        UpdateSelectionState();
        RefreshSelectionStatus();
    }

    void SyncAllViewCheckStates()
    {
        _syncingSelection = true;
        try
        {
            foreach (DataGridViewRow row in _objectListGrid.Rows)
            {
                if (row.Tag is CrossFileObjectGroup group)
                {
                    row.Cells[_deleteColumn.Index].Value = _selectedHashes.Contains(group.Hash);
                }
            }
            _tileView.Invalidate();
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    /// <summary>
    /// Enable/disable the menu items and toolbar buttons that depend on the
    /// current selection and open documents. Status text is handled by
    /// <see cref="RefreshSelectionStatus"/> so the busy → idle transition in
    /// <see cref="SetBusy"/> does not clobber a just-set message (e.g. "Saved
    /// …") with the selection count.
    /// </summary>
    void UpdateSelectionState()
    {
        bool hasDocuments = _workflow.OpenDocuments.Count > 0;
        // One save run does both, so the button follows either selection — and
        // a place already flattened is work waiting to be written even when
        // nothing at all is ticked.
        bool canSave = _selectedHashes.Count > 0
                       || _workflow.HasFlattenedPlaces
                       || _workflow.HasHiddenPlacements;
        // Select-all / clear act on the object list alone, so their enablement
        // describes the list and ignores whatever the panel has selected.
        bool hasSelectable =
            _workflow.ObjectGroups.Any(g => g.IsSafelyRemovable && _visibleKinds.Contains(g.Kind));
        bool hasSelected = _selectedHashes.Count > 0;

        _saveMenuItem.Enabled = !_isBusy && canSave;
        _saveToolButton.Enabled = !_isBusy && canSave;
        _selectAllToolButton.Enabled = !_isBusy && hasSelectable;
        _clearSelectionToolButton.Enabled = !_isBusy && hasSelected;
        _closeAllMenuItem.Enabled = !_isBusy && hasDocuments;
    }

    /// <summary>
    /// Reflect the current selection count in the status bar. Called from
    /// every selection-changing handler so the "N object(s) selected for removal" text stays live
    /// — including when the count drops back to zero, where it falls back to
    /// the workspace state message. No-op while busy so an in-progress
    /// message is preserved.
    /// </summary>
    void RefreshSelectionStatus()
    {
        if (_isBusy) return;

        // Both selections are on screen at once, so both are reported. They stay
        // separate sentences rather than one total: deleting and flattening are
        // different operations on differently counted things — groups across
        // every file against objects at one place on one page — and a single
        // number would describe neither.
        var parts = new List<string>(2);
        if (_selectedHashes.Count > 0) parts.Add(L10n.StatusSelection(_selectedHashes.Count));
        int flattenCount = _graphicsObjectsPanel.SelectedObjectCount;
        if (flattenCount > 0) parts.Add(L10n.StatusFlattenSelection(flattenCount));

        if (parts.Count > 0)
        {
            SetStatus(string.Join(" / ", parts));
        }
        else
        {
            SetStatus(_workflow.OpenDocuments.Count > 0
                ? L10n.StatusAnalyzed
                : L10n.StatusOpenPrompt);
        }
    }
}
