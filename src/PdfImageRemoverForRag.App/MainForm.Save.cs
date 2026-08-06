using PdfImageRemoverForRag.Core.Errors;
using PdfImageRemoverForRag.Core.Formatting;
using PdfImageRemoverForRag.Core.Models;

namespace PdfImageRemoverForRag.App;

internal sealed partial class MainForm
{
    // =======================================================================
    // Remove + save (§14–§16, multi-file)
    // =======================================================================

    async void OnSaveClicked(object? sender, EventArgs e)
    {
        if (await SaveSelectedAsync())
        {
            // The workspace now describes the files that were just written —
            // the save re-read them. So every surface is rebuilt from it rather
            // than adjusted by hand: what was removed is absent, what was
            // flattened is absent, and the picture flattening drew is a row of
            // its own. Keeping these in step by hand is what produced three
            // separate defects in one afternoon.
            //
            // The message the save put in the status bar is passed back in
            // because rebuilding clears the ticks, and a selection change
            // refreshes that line.
            RebuildAfterWorkspaceChanged(_statusLabel.Text ?? string.Empty);
        }
    }

    /// <summary>Move the grid's focus/selection to the first row after a rebuild.</summary>
    void FocusFirstRow()
    {
        _imageListGrid.ClearSelection();
        if (_imageListGrid.Rows.Count == 0) return;
        var firstRow = _imageListGrid.Rows[0];
        firstRow.Selected = true;
        // Land the current cell on a non-checkbox column so focus doesn't sit
        // on the ☑ cell (which would toggle on a stray space press).
        var focusCell = firstRow.Cells[_objectIdColumn.Index];
        if (focusCell.Visible) _imageListGrid.CurrentCell = focusCell;
        _imageListGrid.FirstDisplayedScrollingRowIndex = 0;
    }

    /// <summary>
    /// Run the remove-and-save flow for the current selection. Returns true
    /// only when files were actually written; false on no-op, cancel, or
    /// failure — so callers (e.g. the open-file confirm flow) know whether the
    /// work was safely saved before discarding it.
    /// </summary>
    async Task<bool> SaveSelectedAsync()
    {
        // Ticks in the object list are what a save removes; the flatten panel's
        // are not part of it any more, because flattening happens when it is
        // asked for. What a save writes from that side is what was already
        // flattened, which the workspace holds.
        if (_isBusy
            || (_selectedHashes.Count == 0
                && !_workflow.HasFlattenedPlaces
                && !_workflow.HasHiddenPlacements))
        {
            return false;
        }

        var affectedFiles = _workflow.GetAffectedFiles(_selectedHashes);
        if (affectedFiles.Count == 0) return false;

        if (!TryResolveDestinations(affectedFiles, out var destinations)) return false;

        SetBusy(true, L10n.StatusSaving);
        try
        {
            var result = await _workflow.RemoveAndSaveAsync(
                _selectedHashes.ToArray(), source => destinations[source],
                // Reading the saved files back is analysis, and on a long
                // document it takes as long as opening one — so it says so in
                // the status bar rather than looking hung. The same wording the
                // open path uses, from the same describer.
                new Progress<AnalysisProgress>(report => SetStatus(_openProgress.Describe(report))));
            SetStatus(L10n.StatusSaved(
                result.Files.Count, result.TotalDrawCallsRemoved, result.TotalRegionsFlattened));
            return true;
        }
        catch (PdfCleanerException ex)
        {
            SetStatus(L10n.StatusSaveFailed);
            if (ex.Kind != PdfCleanerErrorKind.UserCancelled) ErrorDialog.Show(this, ex);
            return false;
        }
        catch (Exception ex)
        {
            SetStatus(L10n.StatusSaveFailed);
            ErrorDialog.Show(this, new PdfCleanerException(
                PdfCleanerErrorKind.Unexpected, ex.Message, ex));
            return false;
        }
        finally
        {
            SetBusy(false);
        }
    }

    /// <summary>
    /// Choose the output path(s) before any work starts. One affected file
    /// keeps the classic save dialog (§15); several files ask for an output
    /// folder and auto-name each as 元ファイル名_cleaned.pdf with a numeric
    /// suffix on collisions.
    /// </summary>
    bool TryResolveDestinations(
        IReadOnlyList<string> affectedFiles,
        out Dictionary<string, string> destinations)
    {
        destinations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (affectedFiles.Count == 1)
        {
            var source = affectedFiles[0];
            using var dialog = new SaveFileDialog
            {
                Title = L10n.SaveDialogTitle,
                Filter = L10n.PdfFileFilter,
                InitialDirectory = Path.GetDirectoryName(source),
                FileName = Path.GetFileName(CleanedFileNamer.BuildDefaultDestination(source)),
            };
            while (true)
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return false;
                if (!CleanedFileNamer.WouldOverwriteSource(source, dialog.FileName))
                {
                    destinations[source] = dialog.FileName;
                    return true;
                }
                MessageBox.Show(this, L10n.SameAsSourceMessage, L10n.SameAsSourceTitle,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        using var folderDialog = new FolderBrowserDialog
        {
            Description = L10n.OutputFolderDescription,
            UseDescriptionForTitle = true,
        };
        if (folderDialog.ShowDialog(this) != DialogResult.OK) return false;

        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in affectedFiles)
        {
            var candidate = Path.Combine(folderDialog.SelectedPath,
                Path.GetFileName(CleanedFileNamer.BuildDefaultDestination(source)));
            var unique = UniquifyDestination(candidate, source, taken);
            taken.Add(unique);
            destinations[source] = unique;
        }
        return true;
    }

    static string UniquifyDestination(string candidate, string sourcePath, IReadOnlySet<string> taken)
    {
        // Auto-named outputs must never silently overwrite an existing file,
        // the source PDF, or another output of the same batch.
        var directory = Path.GetDirectoryName(candidate)!;
        var stem = Path.GetFileNameWithoutExtension(candidate);
        var extension = Path.GetExtension(candidate);
        var result = candidate;
        int counter = 2;
        while (File.Exists(result)
               || CleanedFileNamer.WouldOverwriteSource(sourcePath, result)
               || taken.Contains(result))
        {
            result = Path.Combine(directory, $"{stem} ({counter}){extension}");
            counter++;
        }
        return result;
    }
}
