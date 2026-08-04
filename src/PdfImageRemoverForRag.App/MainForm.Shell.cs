namespace PdfImageRemoverForRag.App;

internal sealed partial class MainForm
{
    // =======================================================================
    // Help
    // =======================================================================

    void OnManualClicked(object? sender, EventArgs e) => WebLink.Open(this, L10n.ManualUrl);

    void OnAboutClicked(object? sender, EventArgs e) => AboutDialog.ShowFor(this);


    // =======================================================================
    // Busy / status
    // =======================================================================

    void SetBusy(bool busy, string? statusText = null)
    {
        _isBusy = busy;
        _openMenuItem.Enabled = !busy;
        _openToolButton.Enabled = !busy;
        AllowDrop = !busy;
        _imageListGrid.Enabled = !busy;
        _tileView.Enabled = !busy;
        _flattenPanel.Enabled = !busy;
        _progressIndicator.Visible = busy;
        // The pointer says it too. Analysis and saving both run off the UI
        // thread, so the window keeps repainting and looks ready for input it
        // is not taking; the status bar is at the far edge of the screen and a
        // user watching the list does not see it change.
        UseWaitCursor = busy;
        if (statusText is not null) SetStatus(statusText);
        UpdateSelectionState();
    }

    void SetStatus(string text) => _statusLabel.Text = text;

    void DisposeThumbnailImages(bool disposePlaceholder)
    {
        // Clear() disposes the very bitmaps a running fill is about to hand out.
        CancelThumbnailLoad();
        _thumbnailSettleTimer.Stop();
        _thumbnails.Clear();
        if (disposePlaceholder)
        {
            _gridPlaceholderIcon.Dispose();
            _tilePlaceholderIcon.Dispose();
            _openIcon.Dispose();
            _saveIcon.Dispose();
            _selectAllIcon.Dispose();
            _clearSelectionIcon.Dispose();
            _glyphHeaderFont?.Dispose();
            _thumbnailSettleTimer.Dispose();
            _rowContextMenu.Dispose();
        }
    }
}
