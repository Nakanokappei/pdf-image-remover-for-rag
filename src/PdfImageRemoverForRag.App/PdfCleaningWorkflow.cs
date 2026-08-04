using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PdfImageRemoverForRag.Core.Abstractions;
using PdfImageRemoverForRag.Core.Errors;
using PdfImageRemoverForRag.Core.Formatting;
using PdfImageRemoverForRag.Core.Grouping;
using PdfImageRemoverForRag.Core.Models;

namespace PdfImageRemoverForRag.App;

/// <summary>One saved output file inside a <see cref="BatchSaveResult"/>.</summary>
internal sealed record SavedFile(
    string SourcePath, string DestinationPath, int DrawCallsRemoved, int RegionsFlattened,
    IReadOnlyList<FlattenedPart> FlattenedParts);

/// <summary>
/// Aggregate outcome of a multi-file save run. The two totals stay apart all
/// the way to the status bar: one save run can delete, flatten, or both, and a
/// single number could not say which of them happened.
/// </summary>
internal sealed record BatchSaveResult(
    IReadOnlyList<SavedFile> Files, int TotalDrawCallsRemoved, int TotalRegionsFlattened);

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

    /// <summary>
    /// Bring one document's in-memory analysis in line with the file that was
    /// just written: the placements flattening deleted leave the object list,
    /// and the units that were flattened leave the Flatten panel.
    ///
    /// Deletion has always worked this way — the list is meant to describe what
    /// a saved output still holds — and flattening was the exception, on the
    /// grounds that it left the objects in the document. It no longer does, so
    /// keeping the rows showed the user parts that the output does not contain.
    ///
    /// One placement per reported part, not the whole group: a string shown on
    /// twenty pages that was flattened on one is still on nineteen.
    /// </summary>
    void PruneFlattened(
        string filePath,
        IReadOnlyList<OverlapRegion> flattenedRegions,
        IReadOnlyList<FlattenedPart> parts)
    {
        int index = _documents.FindIndex(d => CleanedFileNamer.WouldOverwriteSource(d.FilePath, filePath));
        if (index < 0) return;
        var document = _documents[index];

        var groups = document.ImageGroups.ToList();
        foreach (var part in parts)
        {
            // An image is reported as Image whatever the object turned out to
            // be, so a shadow is found through the same stream hash rather than
            // by matching the kind exactly.
            int groupIndex = groups.FindIndex(g => part.Kind == RemovableKind.Image
                ? g.Kind.IsImageXObject() && string.Equals(g.Hash, part.Identity, StringComparison.Ordinal)
                : g.Kind == part.Kind && string.Equals(g.TextValue, part.Identity, StringComparison.Ordinal));
            if (groupIndex < 0) continue;

            var group = groups[groupIndex];
            int occurrenceIndex = group.Occurrences
                .ToList().FindIndex(o => o.PageNumber == part.PageNumber);
            if (occurrenceIndex < 0) continue;

            var kept = group.Occurrences.Where((_, i) => i != occurrenceIndex).ToArray();
            if (kept.Length == 0) groups.RemoveAt(groupIndex);
            else groups[groupIndex] = group with { Occurrences = kept };
        }

        // The units that were flattened are not there to flatten again; the
        // page now holds one picture where they were.
        var regionsKept = document.OverlapRegions
            .Where(r => !flattenedRegions.Contains(r))
            .ToArray();

        _documents[index] = document with
        {
            ImageGroups = groups.ToArray(),
            OverlapRegions = regionsKept,
        };
    }

    void RebuildGroups()
    {
        // Merge in Core. ThumbnailBytes stays null throughout the workspace —
        // the views load what they need from the store, by hash.
        ImageGroups = CrossFileImageGroupBuilder.Build(
            _documents.Select(d => (d.FilePath, d.ImageGroups))).ToArray();
    }

    /// <summary>
    /// Source files a save run will touch: those holding at least one of the
    /// selected object groups, plus those with a place to flatten. Either alone
    /// is enough — a run can flatten without removing anything.
    /// </summary>
    public IReadOnlyList<string> GetAffectedFiles(
        IReadOnlyCollection<string> selectedHashes,
        IReadOnlyCollection<string>? filesToFlatten = null)
    {
        return _documents
            .Where(d => d.ImageGroups.Any(g => selectedHashes.Contains(g.Hash))
                        || filesToFlatten?.Any(f => CleanedFileNamer.WouldOverwriteSource(f, d.FilePath)) == true)
            .Select(d => d.FilePath)
            .ToArray();
    }

    /// <summary>
    /// Remove the selected object groups from every affected file, and flatten
    /// the chosen places into images. Each file runs the spec §15 sequence
    /// independently: clean into a temp file, verify the temp, move to the final
    /// name only on success, delete the temp on failure.
    /// <paramref name="resolveDestination"/> maps each source path to its output
    /// path (chosen by the UI beforehand).
    /// </summary>
    /// <param name="regionsToFlattenByFile">
    /// Per source file, the places to bake into an image, each covering only the
    /// objects the user ticked. The cleaner flattens before it removes, so both
    /// can be asked for in one run — the order matters, because removing a group
    /// first would take away the very instances a region is made of.
    /// </param>
    public async Task<BatchSaveResult> RemoveAndSaveAsync(
        IReadOnlyCollection<string> selectedHashes,
        Func<string, string> resolveDestination,
        IReadOnlyDictionary<string, IReadOnlyList<OverlapRegion>>? regionsToFlattenByFile = null,
        CancellationToken ct = default)
    {
        var flattenByFile = regionsToFlattenByFile
            ?? new Dictionary<string, IReadOnlyList<OverlapRegion>>();
        if (selectedHashes.Count == 0 && flattenByFile.Count == 0)
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
        var flattened = new List<(
            string FilePath,
            IReadOnlyList<OverlapRegion> Regions,
            IReadOnlyList<FlattenedPart> Parts)>();
        int totalRemoved = 0;
        int totalFlattened = 0;
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
            // Places to flatten in this file. Keyed by path, so the same
            // path-comparison the rest of the workflow uses decides the match.
            var documentRegions = flattenByFile
                .FirstOrDefault(kv => CleanedFileNamer.WouldOverwriteSource(kv.Key, document.FilePath))
                .Value ?? Array.Empty<OverlapRegion>();
            if (documentSelections.Count == 0 && documentRegions.Count == 0) continue;

            var destinationPath = resolveDestination(document.FilePath);
            var saved = await CleanVerifyCommitAsync(
                    document, destinationPath, documentSelections, documentRegions, selectedHashes, ct)
                .ConfigureAwait(false);
            savedFiles.Add(saved);
            totalRemoved += saved.DrawCallsRemoved;
            totalFlattened += saved.RegionsFlattened;
            // Applied after the loop: _documents is being enumerated, and
            // pruning rewrites its entries.
            if (saved.FlattenedParts.Count > 0 || documentRegions.Count > 0)
            {
                flattened.Add((document.FilePath, documentRegions, saved.FlattenedParts));
            }
        }

        foreach (var (filePath, regions, parts) in flattened)
        {
            PruneFlattened(filePath, regions, parts);
        }
        if (flattened.Count > 0) RebuildGroups();

        _logger.LogInformation(
            "saved: files={Files} drawCallsRemoved={Removed} regionsFlattened={Flattened} " +
            "elapsedMs={ElapsedMs}",
            savedFiles.Count, totalRemoved, totalFlattened, stopwatch.ElapsedMilliseconds);
        return new BatchSaveResult(savedFiles, totalRemoved, totalFlattened);
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
        IReadOnlyList<OverlapRegion> regionsToFlatten,
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
            var result = await _cleaner
                .CleanAsync(document.FilePath, tempPath, selections, regionsToFlatten, ct)
                .ConfigureAwait(false);

            // Logged before verification so a verification failure can be read
            // against what the cleaner believed it did: zero draw calls removed
            // means the selection never matched anything, a full count means it
            // matched but the result did not survive the write. The flatten
            // counts are here too, because a region asked for but not flattened
            // (nothing rendered, or nothing found where it was detected) is
            // otherwise invisible.
            // imagesKeptBack is normally zero. When it is not, the file holds an
            // image something other than a page points at — an annotation, a
            // pattern — so its object had to stay even though its pages no
            // longer draw it. That is the one case where removal cannot fully
            // deliver, and without this line it would be invisible.
            _logger.LogInformation(
                "cleaned: selections={Selections} regionsAsked={RegionsAsked} " +
                "regionsFlattened={RegionsFlattened} pagesModified={Pages} drawCallsRemoved={Removed} " +
                "imagesKeptBack={KeptBack}",
                selections.Count, regionsToFlatten.Count, result.RegionsFlattened,
                result.PagesModified, result.DrawCallsRemoved,
                result.ImagesKeptForOtherReferences);

            // The verifier resolves hashes against the XObjects a page names, so
            // it covers images and drawings — both are streams the file stores
            // and can be found again by hash. Text and shapes live as operators
            // inside a content stream with no hash to resolve, and their removal
            // is checked by tests rather than here.
            var verifiableHashes = document.ImageGroups
                .Where(g => g.Kind.IsIdentifiedByStreamHash())
                .Select(g => g.Hash)
                .ToArray();
            var removedHashes = verifiableHashes.Where(selectedHashes.Contains).ToArray();
            var retainedHashes = verifiableHashes.Except(removedHashes, StringComparer.Ordinal).ToArray();
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
            return new SavedFile(document.FilePath, destinationPath,
                result.DrawCallsRemoved, result.RegionsFlattened, result.FlattenedParts);
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
