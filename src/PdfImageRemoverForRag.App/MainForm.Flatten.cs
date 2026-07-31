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
    /// Show the units the current row takes part in. Called from every place
    /// the current row can change, in either view.
    /// </summary>
    void ShowFlattenPanelForCurrentRow() => _flattenPanel.ShowFor(CurrentDisplayGroup());

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
    /// The list row an object in a unit belongs to. Images are grouped by
    /// stream hash and the other kinds by their match key, which is exactly
    /// what a <see cref="PlacedObject.Identity"/> carries — so this is a
    /// lookup, not a second opinion about identity.
    ///
    /// Returns null when the object has no row at all: a string shown once is
    /// not a removable text group, but it is still drawn on the page and still
    /// belongs to a unit.
    /// </summary>
    CrossFileImageGroup? FindGroupFor(PlacedObject placed) =>
        _workflow.ImageGroups.FirstOrDefault(group => group.Kind == placed.Kind
            && (placed.Kind == RemovableKind.Image
                ? group.Hash == placed.Identity
                : group.TextValue == placed.Identity));
}
