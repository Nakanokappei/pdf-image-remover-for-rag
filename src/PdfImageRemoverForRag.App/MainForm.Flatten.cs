using PdfImageRemoverForRag.Core.Errors;
using PdfImageRemoverForRag.Core.Models;

namespace PdfImageRemoverForRag.App;

internal sealed partial class MainForm
{
    // =======================================================================
    // The 統合 panel
    // =======================================================================
    //
    // The panel owns its own selection (the objects ticked inside units) and
    // sits beside the object list rather than behind a tab, so the pieces
    // shared with the delete side — the save button's enablement, the status
    // line — describe both at once.
    //
    // It is driven BY the list: whichever row is current decides what the panel
    // has to say. That direction and no other. The list row is one identity
    // across every open file, while a unit is one place on one page, so a row
    // names several units but a unit names exactly one row — following the
    // selection the other way would have to guess which unit was meant.

    /// <summary>
    /// The places to flatten, per source file — each covering only the objects
    /// the user ticked. Empty when nothing is ticked, which is what makes a
    /// delete-only save go through the same path unchanged.
    /// </summary>
    IReadOnlyDictionary<string, IReadOnlyList<OverlapRegion>> FlattenSelection() =>
        _flattenPanel.SelectedRegionsByFile();

    void OnFlattenSelectionChanged(object? sender, EventArgs e)
    {
        UpdateSelectionState();
        RefreshSelectionStatus();
    }

    /// <summary>
    /// Hide or show layers. A hidden layer is one PLACEMENT: this drawing of
    /// this object, on this page. The object list's tick is the other scope —
    /// the object gone from everywhere it appears — and the two stay separate,
    /// because hiding a caption on page 4 must not take the same caption off the
    /// other thirty pages.
    ///
    /// Showing one whose object is ticked is the one case where they meet: the
    /// tick is what is hiding it, so the tick goes. Everything of that object
    /// comes back, which is what "show it again" can honestly mean here.
    /// </summary>
    void OnLayerVisibilityChangeRequested(
        object? sender, FlattenPanel.VisibilityChangeEventArgs e)
    {
        foreach (var placed in e.Objects)
        {
            _workflow.SetPlacementHidden(
                e.FilePath, e.Page.PageNumber, e.Page, placed, e.Hide);

            if (e.Hide) continue;
            if (_workflow.ImageGroups.FirstOrDefault(g => g.Matches(placed)) is { } group)
            {
                SetSelected(group.Hash, false);
            }
        }

        SyncAllViewCheckStates();
        _flattenPanel.RefreshVisibility();
        UpdateSelectionState();
        RefreshSelectionStatus();
    }

    /// <summary>
    /// Flatten what is ticked, now. The result is a picture in place of those
    /// objects, and the lists are rebuilt from the document as it stands — which
    /// is where the user sees that anything happened. Nothing they own is
    /// written; saving is still what does that.
    /// </summary>
    async void OnFlattenRequested(object? sender, EventArgs e)
    {
        var places = FlattenSelection();
        if (_isBusy || places.Count == 0) return;

        SetBusy(true, L10n.StatusFlattening);
        try
        {
            int flattened = await _workflow.FlattenAsync(places,
                new Progress<AnalysisProgress>(report => SetStatus(_openProgress.Describe(report))));
            RebuildAfterWorkspaceChanged(L10n.StatusFlattened(flattened), keepSelection: true);
        }
        catch (Exception ex)
        {
            SetStatus(L10n.StatusSaveFailed);
            ErrorDialog.Show(this, ex as PdfCleanerException
                ?? new PdfCleanerException(PdfCleanerErrorKind.Unexpected, ex.Message, ex));
        }
        finally
        {
            SetBusy(false);
        }
    }

    /// <summary>
    /// Take back the flatten the current row is the picture of. The objects it
    /// covered are listed again, because the document is built afresh from the
    /// user's file with that place left out.
    /// </summary>
    async void OnUndoFlattenRequested(object? sender, EventArgs e)
    {
        var (filePath, place) = CurrentFlatten();
        if (_isBusy || filePath is null || place is null) return;

        SetBusy(true, L10n.StatusFlattening);
        try
        {
            if (await _workflow.UndoFlattenAsync(filePath, place,
                    new Progress<AnalysisProgress>(report => SetStatus(_openProgress.Describe(report)))))
            {
                RebuildAfterWorkspaceChanged(L10n.StatusFlattenUndone, keepSelection: true);
            }
        }
        catch (Exception ex)
        {
            SetStatus(L10n.StatusSaveFailed);
            ErrorDialog.Show(this, ex as PdfCleanerException
                ?? new PdfCleanerException(PdfCleanerErrorKind.Unexpected, ex.Message, ex));
        }
        finally
        {
            SetBusy(false);
        }
    }

    /// <summary>
    /// The flatten the current row is the picture of, with the file it is in —
    /// null when the row is an ordinary object. One lookup for both the undo
    /// command's enablement and what it acts on, so the two can never disagree.
    /// </summary>
    (string? FilePath, OverlapRegion? Place) CurrentFlatten()
    {
        if (CurrentDisplayGroup() is not { Kind: RemovableKind.Image } group) return (null, null);

        foreach (var file in group.FileOccurrences)
        {
            foreach (var occurrence in file.Occurrences)
            {
                var place = _workflow.FlattenBehind(file.FilePath, occurrence);
                if (place is not null) return (file.FilePath, place);
            }
        }
        return (null, null);
    }

    /// <summary>
    /// Show the units the current row takes part in. Called from every place
    /// the current row can change, in either view.
    /// </summary>
    void ShowFlattenPanelForCurrentRow()
    {
        _flattenPanel.ShowFor(CurrentDisplayGroup());
        _flattenPanel.CanUndoFlatten = CurrentFlatten().Place is not null;
    }

    /// <summary>
    /// Put every surface back in step with a workspace that has just changed
    /// under it. The same sequence a save runs, and for the same reason: the
    /// document was re-read, so nothing may be carried over by hand.
    /// </summary>
    /// <param name="keepSelection">
    /// True after a flatten, where the object list's ticks are still meaningful:
    /// the objects they name are mostly still there, and dropping them would
    /// throw away a removal selection the user had built up. False after a save,
    /// where clearing them is what stops a second save repeating the work.
    /// </param>
    void RebuildAfterWorkspaceChanged(string status, bool keepSelection = false)
    {
        var kept = keepSelection
            ? _workflow.ImageGroups.Select(g => g.Hash).Where(_selectedHashes.Contains).ToArray()
            : Array.Empty<string>();
        _selectedHashes.Clear();
        foreach (var hash in kept) _selectedHashes.Add(hash);
        RefreshThumbnailImages(_workflow.ImageGroups);
        RebuildDisplay();
        AutoSizeContentColumns();
        FocusFirstRow();
        _flattenPanel.SetDocuments(_workflow.OpenDocuments);
        SetStatus(status);
    }

    /// <summary>
    /// The group the user is currently on: the grid's current row, or the tile
    /// view's focused tile. Null when neither has landed anywhere.
    /// </summary>
    CrossFileImageGroup? CurrentDisplayGroup()
    {
        if (_isTileView) return _tileView.FocusedGroup;
        return _imageListGrid.CurrentRow?.Tag as CrossFileImageGroup;
    }

    /// <summary>
    /// The list row an object in a unit belongs to, and what the panel needs to
    /// draw it: the group, its bitmap if one is resident, and whether one can
    /// ever exist. The last part is why this returns a triple rather than an
    /// image — a null bitmap alone cannot tell "still rendering" from "never
    /// will", and the panel guessing at that is how the tile view once came to
    /// promise a thumbnail forever.
    ///
    /// Returns a null group when the object has no row at all: a string shown
    /// once is not a removable text group, but it is still drawn on the page
    /// and still belongs to a unit.
    /// </summary>
    LayerThumbnail LayerThumbnailFor(PlacedObject placed)
    {
        var group = _workflow.ImageGroups.FirstOrDefault(g => g.Matches(placed));
        if (group is null) return default;

        var bitmap = _thumbnails.Grid(group.Hash);
        return new LayerThumbnail(
            group,
            bitmap ?? (_thumbnails.IsUnrenderable(group.Hash) ? _tilePlaceholderIcon : null),
            CanEverRender: bitmap is not null || !_thumbnails.IsUnrenderable(group.Hash));
    }
}
