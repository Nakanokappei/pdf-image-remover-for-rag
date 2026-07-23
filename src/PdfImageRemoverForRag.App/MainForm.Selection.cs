using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using PdfImageRemoverForRag.Core.Errors;
using PdfImageRemoverForRag.Core.Formatting;
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
        if (_imageListGrid.IsCurrentCellDirty)
        {
            _imageListGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
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
        if (_imageListGrid.Rows[e.RowIndex].Tag is not CrossFileImageGroup group) return;

        bool selected = (e.State & DataGridViewElementStates.Selected) != 0;
        var text = ImageListRow.ThumbnailText(group);
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
    /// Draw one image or shape row's thumbnail, scaled to fit and centred.
    /// A row whose bitmap is not loaded yet simply stays empty — it fills in
    /// within the settle interval — while one the store gave up on shows the
    /// placeholder icon, so "not ready" and "cannot be shown" never look alike.
    /// </summary>
    void PaintThumbnailCell(
        DataGridViewCellPaintingEventArgs e, CrossFileImageGroup group, bool selected)
    {
        e.PaintBackground(e.CellBounds, selected);

        var bitmap = _thumbnails.Grid(group.Hash)
                     ?? (_thumbnails.IsUnrenderable(group.Hash) ? _gridPlaceholderIcon : null);
        if (bitmap is null) return;

        var area = Rectangle.Inflate(e.CellBounds, -Dip(2), -Dip(2));
        if (area.Width <= 0 || area.Height <= 0) return;

        // Fit inside the cell without ever enlarging the bitmap.
        double scale = Math.Min(1.0, Math.Min(
            (double)area.Width / bitmap.Width, (double)area.Height / bitmap.Height));
        int width = Math.Max(1, (int)(bitmap.Width * scale));
        int height = Math.Max(1, (int)(bitmap.Height * scale));
        e.Graphics!.DrawImage(bitmap, new Rectangle(
            area.X + ((area.Width - width) / 2),
            area.Y + ((area.Height - height) / 2),
            width, height));
    }

    /// <summary>
    /// Paint an Excel-like header cell entirely by hand — column header,
    /// row-number gutter, or the top-left corner. Everything (background, text,
    /// sort glyph) is drawn here and the framework is NOT asked to paint content,
    /// so there is no current-row marker in the row gutter and no selected-column
    /// highlight in the 表頭. Flat pale gray fill (no gradient) with a thin gray
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
            var column = _imageListGrid.Columns[e.ColumnIndex];
            bool sorted = column == _sortColumn && column != _thumbnailColumn;
            var textBounds = Rectangle.Inflate(bounds, -Dip(6), -Dip(2));
            textBounds.Width -= Dip(SortGlyphWidth);

            var font = column.HeaderCell.Style.Font
                       ?? _imageListGrid.ColumnHeadersDefaultCellStyle.Font
                       ?? _imageListGrid.Font;
            var flags = ToTextFlags(column.HeaderCell.Style.Alignment) | TextFormatFlags.EndEllipsis;
            TextRenderer.DrawText(g, column.HeaderText, font, textBounds, HeaderText, flags);
            if (sorted) DrawSortGlyph(g, bounds);
        }
        else if (e.ColumnIndex == -1 && e.RowIndex >= 0)
        {
            // Row-number gutter: the number, centered; no current-row marker.
            // NoPadding so the built-in left glyph padding does not push the
            // digits right (which made multi-digit numbers look right-aligned).
            var value = _imageListGrid.Rows[e.RowIndex].HeaderCell.Value?.ToString();
            if (!string.IsNullOrEmpty(value))
            {
                TextRenderer.DrawText(g, value, _imageListGrid.Font, bounds, HeaderText,
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
        // Track the range anchor: the last row clicked without Shift becomes the
        // current row and the anchor for a subsequent Shift+click.
        if (e.RowIndex < 0) return;
        if ((ModifierKeys & Keys.Shift) == 0) _checkAnchorRowIndex = e.RowIndex;
    }

    void OnGridCellMouseUp(object? sender, DataGridViewCellMouseEventArgs e)
    {
        // Whole-cell hit area for the ☑ column: a left click anywhere in the cell
        // toggles it. Shift+click checks (or unchecks) the whole range from the
        // anchor row to the clicked row.
        if (e.Button != MouseButtons.Left) return;
        if (e.RowIndex < 0 || e.ColumnIndex != _deleteColumn.Index) return;
        if (_imageListGrid.Rows[e.RowIndex].Cells[_deleteColumn.Index].Value is not { } current) return;

        // New state is the opposite of the clicked cell's current state; the whole
        // range (for Shift) or just this row (otherwise) is set to it.
        bool newState = current is not true;
        bool shift = (ModifierKeys & Keys.Shift) != 0
                     && _checkAnchorRowIndex >= 0
                     && _checkAnchorRowIndex < _imageListGrid.Rows.Count;
        int from = shift ? Math.Min(_checkAnchorRowIndex, e.RowIndex) : e.RowIndex;
        int to = shift ? Math.Max(_checkAnchorRowIndex, e.RowIndex) : e.RowIndex;

        _syncingSelection = true;
        try
        {
            for (int r = from; r <= to; r++)
            {
                var row = _imageListGrid.Rows[r];
                // Only safely-removable rows can be checked (§14.3).
                if (row.Tag is not CrossFileImageGroup group || !group.IsSafelyRemovable) continue;
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
        var row = _imageListGrid.Rows[e.RowIndex];
        if (row.Tag is not CrossFileImageGroup group) return;

        bool isChecked = row.Cells[_deleteColumn.Index].Value is true;
        SetSelected(group.Hash, isChecked);
        _tileView.Invalidate();
        UpdateSelectionState();
        RefreshSelectionStatus();
    }

    void OnTileToggled(object? sender, CrossFileImageGroup group)
    {
        if (_syncingSelection) return;
        bool isChecked = !_selectedHashes.Contains(group.Hash);
        SetSelected(group.Hash, isChecked);
        SyncGridRowCheckState(group.Hash, isChecked);
        UpdateSelectionState();
        RefreshSelectionStatus();
    }

    void SetSelected(string hash, bool selected)
    {
        if (selected) _selectedHashes.Add(hash);
        else _selectedHashes.Remove(hash);
    }

    void SyncGridRowCheckState(string hash, bool isChecked)
    {
        _syncingSelection = true;
        try
        {
            foreach (DataGridViewRow row in _imageListGrid.Rows)
            {
                if (row.Tag is CrossFileImageGroup g && g.Hash == hash)
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
        // Select only what the list currently shows: the 表示列 filter scopes
        // "select all" so hidden kinds are never silently marked for removal.
        foreach (var group in _workflow.ImageGroups
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
            foreach (DataGridViewRow row in _imageListGrid.Rows)
            {
                if (row.Tag is CrossFileImageGroup group)
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
    /// <see cref="SetBusy"/> does not clobber a just-set message (e.g. "保存
    /// しました") with the selection count.
    /// </summary>
    void UpdateSelectionState()
    {
        bool hasDocuments = _workflow.OpenDocuments.Count > 0;
        bool hasSelection = _selectedHashes.Count > 0;
        bool hasSelectable = _workflow.ImageGroups.Any(
            g => g.IsSafelyRemovable && _visibleKinds.Contains(g.Kind));

        _saveMenuItem.Enabled = !_isBusy && hasSelection;
        _saveToolButton.Enabled = !_isBusy && hasSelection;
        _selectAllToolButton.Enabled = !_isBusy && hasSelectable;
        _clearSelectionToolButton.Enabled = !_isBusy && hasSelection;
        _closeAllMenuItem.Enabled = !_isBusy && hasDocuments;
    }

    /// <summary>
    /// Reflect the current selection count in the status bar. Called from
    /// every selection-changing handler so the "N 件を選択中" text stays live
    /// — including when the count drops back to zero, where it falls back to
    /// the workspace state message. No-op while busy so an in-progress
    /// message is preserved.
    /// </summary>
    void RefreshSelectionStatus()
    {
        if (_isBusy) return;
        if (_selectedHashes.Count > 0)
        {
            SetStatus(L10n.StatusSelection(_selectedHashes.Count));
        }
        else
        {
            SetStatus(_workflow.OpenDocuments.Count > 0
                ? L10n.StatusAnalyzed
                : L10n.StatusOpenPrompt);
        }
    }
}
