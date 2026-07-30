using PdfImageRemoverForRag.Core.Models;

namespace PdfImageRemoverForRag.App;

internal sealed partial class MainForm
{
    // =======================================================================
    // The 統合 tab
    // =======================================================================
    //
    // The tab owns its own selection (a tree of ticked objects), so the pieces
    // shared with the delete side — the toolbar's select-all / clear, the save
    // button's enablement, the status line — have to ask which tab is in front.
    // Everything else about flattening lives in FlattenPanel; this file is the
    // seam between the two tabs.

    /// <summary>True when the 統合 tab is the one on screen.</summary>
    bool IsFlattenTab => _tabs.SelectedTab == _flattenTab;

    /// <summary>
    /// The places to flatten, per source file — each covering only the objects
    /// the user ticked. Empty when nothing is ticked, which is what makes a
    /// delete-only save go through the same path unchanged.
    /// </summary>
    IReadOnlyDictionary<string, IReadOnlyList<OverlapRegion>> FlattenSelection() =>
        _flattenPanel.SelectedRegionsByFile();

    void OnTabChanged(object? sender, EventArgs e)
    {
        // The toolbar and the status line describe whichever tab is in front,
        // so switching tabs has to re-derive both.
        UpdateSelectionState();
        RefreshSelectionStatus();
    }

    void OnFlattenSelectionChanged(object? sender, EventArgs e)
    {
        UpdateSelectionState();
        RefreshSelectionStatus();
    }
}
