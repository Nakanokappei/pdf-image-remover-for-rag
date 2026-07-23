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
    // Remove + save (§14–§16, multi-file)
    // =======================================================================

    async void OnSaveClicked(object? sender, EventArgs e)
    {
        // Capture the removed set before saving; on success those objects
        // leave the workspace so the list shows only what remains.
        var savedHashes = _selectedHashes.ToArray();
        if (await SaveSelectedAsync())
        {
            _selectedHashes.Clear();          // 選択をクリア
            _workflow.RemoveGroups(savedHashes); // ✓された行を削除
            RefreshThumbnailImages(_workflow.ImageGroups);
            RebuildDisplay();                 // 再ソート（現在の並び順で）
            AutoSizeContentColumns();         // 残ったデータに合わせて再フィット
            FocusFirstRow();                  // フォーカス行を先頭行へ
            // Keep the "保存しました" status set by SaveSelectedAsync — do not
            // overwrite it with the selection/workspace message.
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
        var focusCell = firstRow.Cells[_imageIdColumn.Index];
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
        if (_isBusy || _selectedHashes.Count == 0) return false;

        var affectedFiles = _workflow.GetAffectedFiles(_selectedHashes);
        if (affectedFiles.Count == 0) return false;

        if (!TryResolveDestinations(affectedFiles, out var destinations)) return false;

        SetBusy(true, L10n.StatusSaving);
        try
        {
            var result = await _workflow.RemoveAndSaveAsync(
                _selectedHashes.ToArray(), source => destinations[source]);
            SetStatus(L10n.StatusSaved(result.Files.Count, result.TotalDrawCallsRemoved));
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
