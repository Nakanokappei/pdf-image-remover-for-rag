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
    // Open + analyze (§13; multiple files)
    // =======================================================================

    async void OnOpenClicked(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Title = L10n.OpenDialogTitle,
            Filter = L10n.PdfFileFilter,
            Multiselect = true,
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (!await ConfirmReplaceWorkspaceAsync()) return;
        await OpenPdfFilesAsync(dialog.FileNames);
    }

    /// <summary>
    /// Decide what to do with the current work before opening replaces it.
    /// Nothing open → proceed. Objects selected for removal → offer to save
    /// first (Yes saves then opens, No discards and opens, Cancel aborts).
    /// Files open but nothing selected → just confirm discarding them.
    /// Returns true to proceed with the open, false to abort.
    /// </summary>
    async Task<bool> ConfirmReplaceWorkspaceAsync()
    {
        if (_workflow.OpenDocuments.Count == 0) return true;

        if (_selectedHashes.Count > 0)
        {
            var choice = MessageBox.Show(this, L10n.ConfirmSaveBeforeOpen, L10n.ConfirmTitle,
                MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
            return choice switch
            {
                DialogResult.Yes => await SaveSelectedAsync(), // open only if the save succeeded
                DialogResult.No => true,                        // discard selection, open
                _ => false,                                     // cancel — keep current work
            };
        }

        var confirm = MessageBox.Show(this, L10n.ConfirmDiscardBeforeOpen, L10n.ConfirmTitle,
            MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        return confirm == DialogResult.Yes;
    }

    void OnPdfDragEnter(object? sender, DragEventArgs e)
    {
        e.Effect = !_isBusy && GetDroppedPdfPaths(e).Count > 0
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    async void OnPdfDragDrop(object? sender, DragEventArgs e)
    {
        if (_isBusy) return;
        var paths = GetDroppedPdfPaths(e);
        if (paths.Count == 0) return;
        if (!await ConfirmReplaceWorkspaceAsync()) return;
        await OpenPdfFilesAsync(paths);
    }

    static IReadOnlyList<string> GetDroppedPdfPaths(DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is not string[] files)
        {
            return Array.Empty<string>();
        }
        return files
            .Where(f => string.Equals(Path.GetExtension(f), ".pdf", StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    /// <summary>
    /// How long the analysis may run before the progress dialog appears. Small
    /// files finish inside this window, so the common case shows no dialog at
    /// all — only the status bar ticking over.
    /// </summary>
    static readonly TimeSpan ProgressDialogDelay = TimeSpan.FromSeconds(2);

    async Task OpenPdfFilesAsync(IReadOnlyList<string> paths)
    {
        // Opening replaces the current workspace — the caller has already
        // confirmed (and optionally saved) via ConfirmReplaceWorkspaceAsync.
        _workflow.CloseAll();
        _selectedHashes.Clear();

        SetBusy(true, L10n.StatusAnalyzing);
        using var cancellation = new CancellationTokenSource();
        // The dialog exists only after the delay, so reports go to whatever is
        // on screen at the time: status bar always, dialog when it is up.
        AnalysisProgressDialog? dialog = null;
        var progress = new Progress<AnalysisProgress>(report =>
        {
            var text = _openProgress.Describe(report);
            SetStatus(text);
            dialog?.Update(text, report.Fraction);
        });

        try
        {
            // Analysis is now the only slow part of opening: thumbnails are
            // built for the viewport afterwards, not up front.
            var work = AnalyzeAllAsync(paths, progress, cancellation.Token);

            // Wait briefly before disturbing the user with a modal window.
            var firstFinished = await Task.WhenAny(work, Task.Delay(ProgressDialogDelay));
            if (firstFinished != work)
            {
                AnalysisProgressDialog.ShowFor(
                    this, work, cancellation, L10n.StatusAnalyzing,
                    created => dialog = created);
                dialog = null;
            }
            await work;

            // Freshly opened files start sorted by 使用回数 descending.
            var viewStopwatch = Stopwatch.StartNew();
            ResetSortToDefault();
            RefreshWorkspace();
            // Fit columns to header + data now that the rows exist.
            AutoSizeContentColumns();
            _logger.LogInformation(
                "view prepared: groups={Groups} rows={Rows} elapsedMs={ElapsedMs}",
                _workflow.ImageGroups.Count, _imageListGrid.Rows.Count,
                viewStopwatch.ElapsedMilliseconds);
            SetStatus(_workflow.OpenDocuments.Count > 0 ? L10n.StatusAnalyzed : L10n.StatusOpenFailed);
        }
        catch (OperationCanceledException)
        {
            // Cancelling abandons the whole open: a workspace holding "the two
            // files that happened to finish first" is harder to reason about
            // than an empty one.
            _workflow.CloseAll();
            _selectedHashes.Clear();
            RefreshWorkspace();
            AutoSizeContentColumns();
            SetStatus(L10n.StatusCancelled);
        }
        catch (Exception ex)
        {
            SetStatus(L10n.StatusOpenFailed);
            ErrorDialog.Show(this, new PdfCleanerException(
                PdfCleanerErrorKind.Unexpected, ex.Message, ex));
        }
        finally
        {
            SetBusy(false);
        }
    }

    /// <summary>
    /// Analyze every path in turn, prefixing progress with the file position
    /// when there is more than one.
    /// </summary>
    async Task AnalyzeAllAsync(IReadOnlyList<string> paths,
                               IProgress<AnalysisProgress> progress,
                               CancellationToken ct)
    {
        for (int i = 0; i < paths.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            _openProgress.BeginFile(Path.GetFileName(paths[i]), i + 1, paths.Count);
            try
            {
                // Duplicates return false and are silently skipped.
                await _workflow.AddAsync(paths[i], progress, ct);
            }
            catch (PdfCleanerException ex)
            {
                // One bad file must not block the rest of the batch.
                if (ex.Kind != PdfCleanerErrorKind.UserCancelled) ErrorDialog.Show(this, ex);
            }
        }
    }

    void OnCloseAllClicked(object? sender, EventArgs e)
    {
        _workflow.CloseAll();
        _selectedHashes.Clear();
        RefreshWorkspace();
        // Back to empty: size columns to their headers.
        AutoSizeContentColumns();
        SetStatus(L10n.StatusOpenPrompt);
    }
}
