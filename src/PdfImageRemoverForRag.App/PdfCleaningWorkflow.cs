using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PdfImageRemoverForRag.Core.Abstractions;
using PdfImageRemoverForRag.Core.Errors;
using PdfImageRemoverForRag.Core.Formatting;
using PdfImageRemoverForRag.Core.Grouping;
using PdfImageRemoverForRag.Core.Models;

namespace PdfImageRemoverForRag.App;

/// <summary>One saved output file inside a <see cref="BatchSaveResult"/>.</summary>
internal sealed record SavedFile(string SourcePath, string DestinationPath, int DrawCallsRemoved);

/// <summary>Aggregate outcome of a multi-file save run.</summary>
internal sealed record BatchSaveResult(IReadOnlyList<SavedFile> Files, int TotalDrawCallsRemoved);

/// <summary>
/// UI-free orchestration of the multi-document workspace: open PDFs are
/// analyzed once, their image groups merged across files by stream hash, and
/// a single save run cleans every affected file with the §15 sequence.
///
/// Memory policy (per the "not on-memory" requirement): after analysis only
/// metadata survives — file path, page count, hashes, occurrence rectangles —
/// plus ONE thumbnail PNG per unique image hash. Per-file thumbnails are
/// stripped, no PDF document object stays open, and cleaning re-reads the
/// source from disk.
/// </summary>
internal sealed class PdfCleaningWorkflow
{
    readonly IPdfDocumentAnalyzer _analyzer;
    readonly IPdfDocumentCleaner _cleaner;
    readonly IPdfDocumentVerifier _verifier;
    readonly ILogger _logger;

    readonly List<PdfDocumentInfo> _documents = new();
    readonly ThumbnailStore _store;

    /// <summary>Currently open documents (metadata only, thumbnails stripped).</summary>
    public IReadOnlyList<PdfDocumentInfo> OpenDocuments => _documents;

    /// <summary>Image groups merged across every open file.</summary>
    public IReadOnlyList<CrossFileImageGroup> ImageGroups { get; private set; } =
        Array.Empty<CrossFileImageGroup>();

    public PdfCleaningWorkflow(
        IPdfDocumentAnalyzer analyzer,
        IPdfDocumentCleaner cleaner,
        IPdfDocumentVerifier verifier,
        ThumbnailStore store,
        ILogger logger)
    {
        _analyzer = analyzer;
        _store = store;
        _cleaner = cleaner;
        _verifier = verifier;
        _logger = logger;
    }

    /// <summary>True when <paramref name="path"/> is already open.</summary>
    public bool IsOpen(string path) =>
        _documents.Any(d => CleanedFileNamer.WouldOverwriteSource(d.FilePath, path));

    /// <summary>
    /// Analyze one PDF and add it to the workspace. Returns false (no-op)
    /// when the same file is already open. Metrics logged per §19 — counts
    /// and durations only, never paths or content.
    /// </summary>
    public async Task<bool> AddAsync(
        string pdfFilePath,
        IProgress<AnalysisProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (IsOpen(pdfFilePath)) return false;

        var stopwatch = Stopwatch.StartNew();
        var info = await _analyzer
            .AnalyzeAsync(pdfFilePath, progress: progress, ct: ct)
            .ConfigureAwait(false);

        // Source bytes go straight to the on-disk store, one file per unique
        // hash, and are then dropped from the workspace. Nothing image-shaped
        // survives in memory: the same logo in five files costs one file on
        // disk and nothing in RAM.
        foreach (var group in info.ImageGroups)
        {
            if (group.ThumbnailBytes is { Length: > 0 } bytes)
            {
                _store.SaveSource(group.Hash, bytes);
            }
        }
        _documents.Add(info with
        {
            ImageGroups = info.ImageGroups.Select(g => g with { ThumbnailBytes = null }).ToArray(),
        });
        RebuildGroups();

        _logger.LogInformation(
            "analyzed: fileSize={FileSize} pages={Pages} encrypted={Encrypted} " +
            "imageGroups={Groups} occurrences={Occurrences} openFiles={OpenFiles} " +
            "crossFileGroups={CrossGroups} elapsedMs={ElapsedMs}",
            info.FileSize, info.PageCount, info.IsEncrypted,
            info.ImageKindCount, info.TotalUsageCount,
            _documents.Count, ImageGroups.Count, stopwatch.ElapsedMilliseconds);
        return true;
    }

    /// <summary>Close every document. The store keeps its files for the run.</summary>
    public void CloseAll()
    {
        _documents.Clear();
        ImageGroups = Array.Empty<CrossFileImageGroup>();
    }

    /// <summary>
    /// Drop the given object groups (by hash) from every open document and
    /// rebuild the cross-file grouping. Called after a successful save so the
    /// objects the user just removed leave the list. The source files on disk
    /// are untouched — this only updates the in-memory analysis to reflect
    /// what has already been cleaned.
    /// </summary>
    public void RemoveGroups(IReadOnlyCollection<string> hashes)
    {
        if (hashes.Count == 0) return;

        // Rewrite each document's group list without the removed hashes.
        for (int i = 0; i < _documents.Count; i++)
        {
            var document = _documents[i];
            var kept = document.ImageGroups.Where(g => !hashes.Contains(g.Hash)).ToArray();
            if (kept.Length != document.ImageGroups.Count)
            {
                _documents[i] = document with { ImageGroups = kept };
            }
        }

        RebuildGroups();
    }

    void RebuildGroups()
    {
        // Merge in Core. ThumbnailBytes stays null throughout the workspace —
        // the views load what they need from the store, by hash.
        ImageGroups = CrossFileImageGroupBuilder.Build(
            _documents.Select(d => (d.FilePath, d.ImageGroups))).ToArray();
    }

    /// <summary>
    /// Source files that contain at least one of the selected image hashes —
    /// the set of files a save run will touch.
    /// </summary>
    public IReadOnlyList<string> GetAffectedFiles(IReadOnlyCollection<string> selectedHashes)
    {
        return _documents
            .Where(d => d.ImageGroups.Any(g => selectedHashes.Contains(g.Hash)))
            .Select(d => d.FilePath)
            .ToArray();
    }

    /// <summary>
    /// Remove the selected image groups from every affected file. Each file
    /// runs the spec §15 sequence independently: clean into a temp file,
    /// verify the temp, move to the final name only on success, delete the
    /// temp on failure. <paramref name="resolveDestination"/> maps each
    /// source path to its output path (chosen by the UI beforehand).
    /// </summary>
    public async Task<BatchSaveResult> RemoveAndSaveAsync(
        IReadOnlyCollection<string> selectedHashes,
        Func<string, string> resolveDestination,
        CancellationToken ct = default)
    {
        if (selectedHashes.Count == 0)
        {
            throw new PdfCleanerException(PdfCleanerErrorKind.Unexpected, L10n.ErrorNoSelection);
        }

        // Defense in depth: the UI disables unsafe checkboxes, but re-check
        // here so a UI bug can never remove a group flagged unsafe (§14.3).
        var groupsByHash = ImageGroups.ToDictionary(g => g.Hash, StringComparer.Ordinal);
        foreach (var hash in selectedHashes)
        {
            if (!groupsByHash.TryGetValue(hash, out var group) || !group.IsSafelyRemovable)
            {
                throw new PdfCleanerException(PdfCleanerErrorKind.ImageRemovalUnsafe,
                    ErrorMessageCatalog.Resolve(PdfCleanerErrorKind.ImageRemovalUnsafe).Description);
            }
        }

        var savedFiles = new List<SavedFile>();
        int totalRemoved = 0;
        var stopwatch = Stopwatch.StartNew();

        foreach (var document in _documents)
        {
            ct.ThrowIfCancellationRequested();

            // Selections for this file: the checked groups that actually
            // occur in it, each carrying only this file's occurrences.
            var documentSelections = selectedHashes
                .Select(hash => groupsByHash[hash])
                .Select(group => (group, fileOccurrences: group.FileOccurrences
                    .FirstOrDefault(f => CleanedFileNamer.WouldOverwriteSource(f.FilePath, document.FilePath))))
                .Where(x => x.fileOccurrences is { Occurrences.Count: > 0 })
                .Select(x => new ImageRemovalSelection(
                    x.group.GroupId, x.fileOccurrences!.Occurrences, x.group.Kind,
                    x.group.TextValue, x.group.Hash))
                .ToList();
            if (documentSelections.Count == 0) continue;

            var destinationPath = resolveDestination(document.FilePath);
            var saved = await CleanVerifyCommitAsync(document, destinationPath, documentSelections, selectedHashes, ct)
                .ConfigureAwait(false);
            savedFiles.Add(saved);
            totalRemoved += saved.DrawCallsRemoved;
        }

        _logger.LogInformation(
            "saved: files={Files} drawCallsRemoved={Removed} elapsedMs={ElapsedMs}",
            savedFiles.Count, totalRemoved, stopwatch.ElapsedMilliseconds);
        return new BatchSaveResult(savedFiles, totalRemoved);
    }

    /// <summary>First few warnings, plus a count of the rest.</summary>
    static string SummariseWarnings(IReadOnlyList<string> warnings)
    {
        const int shown = 4;
        if (warnings.Count <= shown) return string.Join(" / ", warnings);
        return string.Join(" / ", warnings.Take(shown))
               + L10n.VerificationMoreWarnings(warnings.Count - shown);
    }

    /// <summary>The §15 sequence for one file.</summary>
    async Task<SavedFile> CleanVerifyCommitAsync(
        PdfDocumentInfo document,
        string destinationPath,
        IReadOnlyList<ImageRemovalSelection> selections,
        IReadOnlyCollection<string> selectedHashes,
        CancellationToken ct)
    {
        if (CleanedFileNamer.WouldOverwriteSource(document.FilePath, destinationPath))
        {
            throw new PdfCleanerException(PdfCleanerErrorKind.DestinationNotWritable,
                L10n.ErrorSameAsSource);
        }

        // Temp file in the destination directory so the final File.Move is
        // an atomic same-volume rename.
        var tempPath = destinationPath + ".part";
        try
        {
            var result = await _cleaner.CleanAsync(document.FilePath, tempPath, selections, ct)
                .ConfigureAwait(false);

            // Logged before verification so a verification failure can be read
            // against what the cleaner believed it did: zero draw calls removed
            // means the selection never matched anything, a full count means it
            // matched but the result did not survive the write.
            _logger.LogInformation(
                "cleaned: selections={Selections} pagesModified={Pages} drawCallsRemoved={Removed}",
                selections.Count, result.PagesModified, result.DrawCallsRemoved);

            // The verifier resolves hashes against Image XObjects, so it only
            // handles image groups; text removal is checked by tests, not here.
            var imageHashes = document.ImageGroups
                .Where(g => g.Kind == RemovableKind.Image)
                .Select(g => g.Hash)
                .ToArray();
            var removedHashes = imageHashes.Where(selectedHashes.Contains).ToArray();
            var retainedHashes = imageHashes.Except(removedHashes, StringComparer.Ordinal).ToArray();
            var report = await _verifier.VerifyAsync(
                document.FilePath, tempPath, removedHashes, retainedHashes, ct)
                .ConfigureAwait(false);

            if (!report.IsOverallOk)
            {
                // One warning per page is unreadable when a 176-page document
                // fails; show a few and say how many more there were. The
                // counts from the cleaner go first — they are what identifies
                // whether the selection matched at all.
                throw new PdfCleanerException(PdfCleanerErrorKind.PostSaveVerificationFailed,
                    L10n.ErrorVerificationFailedPrefix
                    + L10n.VerificationCleanerSummary(result.PagesModified, result.DrawCallsRemoved)
                    + SummariseWarnings(report.Warnings));
            }

            File.Move(tempPath, destinationPath, overwrite: true);
            return new SavedFile(document.FilePath, destinationPath, result.DrawCallsRemoved);
        }
        catch (Exception ex)
        {
            TryDeleteTempFile(tempPath);
            if (ex is not PdfCleanerException and not OperationCanceledException)
            {
                _logger.LogError(ex, "save failed");
            }
            throw;
        }
    }

    void TryDeleteTempFile(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "temp file cleanup failed");
        }
    }
}
