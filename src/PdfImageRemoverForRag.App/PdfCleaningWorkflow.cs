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
    string SourcePath, string DestinationPath, int DrawCallsRemoved, int RegionsFlattened);

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

    // Flattening happens when the user asks for it, not when they save, so the
    // file the workspace describes is not always the file on their disk: it is
    // a working copy in the temp folder, rebuilt from their file every time the
    // set of flattened places changes.
    //
    // The document keeps THEIR path — it is the name every surface shows and
    // the name the save's destination is derived from — and these two say what
    // has been done to it and where the bytes really are. Every read of the
    // document's bytes goes through ReadPathOf; nothing else needs to know.
    readonly Dictionary<string, List<OverlapRegion>> _flattenedPlaces =
        new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, string> _workingCopies =
        new(StringComparer.OrdinalIgnoreCase);

    // Layers the user has hidden, as one region apiece: the object, where it is
    // drawn, on which page of which file. A hidden layer is NOT the same as an
    // object ticked for removal — that one goes from everywhere it appears,
    // while this is one placement of it and the rest stay. Kept per file for
    // the same reason the flattened places are: the save reads it back out.
    readonly Dictionary<string, List<OverlapRegion>> _hiddenPlacements =
        new(StringComparer.OrdinalIgnoreCase);

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
        var info = await AnalyzeForWorkspaceAsync(pdfFilePath, progress, ct).ConfigureAwait(false);
        _documents.Add(info);
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

    /// <summary>
    /// Analyze one file for the workspace: thumbnails go to the on-disk store,
    /// one file per unique hash, and are then dropped from what is kept.
    /// Nothing image-shaped survives in memory — the same logo in five files
    /// costs one file on disk and nothing in RAM.
    ///
    /// Shared by opening and by the re-read that follows a save, so a document
    /// that arrived either way is the same kind of thing afterwards.
    /// </summary>
    async Task<PdfDocumentInfo> AnalyzeForWorkspaceAsync(
        string pdfFilePath, IProgress<AnalysisProgress>? progress, CancellationToken ct)
    {
        var info = await _analyzer
            .AnalyzeAsync(pdfFilePath, progress: progress, ct: ct)
            .ConfigureAwait(false);

        foreach (var group in info.ImageGroups)
        {
            if (group.ThumbnailBytes is { Length: > 0 } bytes)
            {
                _store.SaveSource(group.Hash, bytes);
            }
        }

        return info with
        {
            ImageGroups = info.ImageGroups.Select(g => g with { ThumbnailBytes = null }).ToArray(),
        };
    }

    /// <summary>Close every document. The store keeps its files for the run.</summary>
    public void CloseAll()
    {
        // The working copies describe documents that are no longer open, so
        // they are deleted here rather than left for the temp folder to collect.
        foreach (var filePath in _workingCopies.Keys.ToArray()) DiscardWorkingCopy(filePath);
        _flattenedPlaces.Clear();
        _hiddenPlacements.Clear();
        _documents.Clear();
        ImageGroups = Array.Empty<CrossFileImageGroup>();
    }

    /// <summary>
    /// Replace one file's flatten units with a list the user edited by hand.
    /// Detection is right almost every time; when it is not, the correction has
    /// to live in the workspace, because that is what the save reads from and
    /// what the panel is rebuilt from.
    ///
    /// Not persisted: a save re-reads the file it wrote, and the units of that
    /// file are detected afresh. Carrying hand edits across a save would mean
    /// claiming they still describe a document that has changed underneath
    /// them.
    /// </summary>
    public void ReplaceOverlapRegions(string filePath, IReadOnlyList<OverlapRegion> regions)
    {
        int index = _documents.FindIndex(
            d => CleanedFileNamer.WouldOverwriteSource(d.FilePath, filePath));
        if (index < 0) return;
        _documents[index] = _documents[index] with { OverlapRegions = regions };
    }

    // =======================================================================
    // Hidden layers
    // =======================================================================

    /// <summary>
    /// Whether this drawing of this object, here, is hidden. One placement: the
    /// same image on the next page is a different one and answers for itself.
    /// </summary>
    public bool IsPlacementHidden(string filePath, int pageNumber, PlacedObject placed) =>
        _hiddenPlacements.TryGetValue(filePath, out var hidden)
        && hidden.Any(r => r.PageNumber == pageNumber && r.Members.Contains(placed));

    /// <summary>
    /// Hide or show one placement. Stored as the region covering it, because
    /// that is what a save needs: the objects inside a rectangle on a page, out,
    /// with nothing drawn in their place.
    /// </summary>
    public void SetPlacementHidden(
        string filePath, int pageNumber, PageDimensions page, PlacedObject placed, bool hide)
    {
        if (!_hiddenPlacements.TryGetValue(filePath, out var hidden))
        {
            hidden = new List<OverlapRegion>();
            _hiddenPlacements[filePath] = hidden;
        }

        int found = hidden.FindIndex(
            r => r.PageNumber == pageNumber && r.Members.Contains(placed));
        if (hide)
        {
            if (found < 0) hidden.Add(OverlapDetector.RegionCovering(page, new[] { placed }));
        }
        else if (found >= 0)
        {
            hidden.RemoveAt(found);
        }
    }

    /// <summary>True when a save has hidden layers to write out.</summary>
    public bool HasHiddenPlacements => _hiddenPlacements.Values.Any(list => list.Count > 0);

    IReadOnlyList<OverlapRegion> HiddenIn(string filePath) =>
        _hiddenPlacements.TryGetValue(filePath, out var hidden)
            ? hidden
            : Array.Empty<OverlapRegion>();

    // =======================================================================
    // Flattening, before the save
    // =======================================================================

    /// <summary>
    /// True when a place has been flattened but not yet written to a file the
    /// user owns. The save button asks, because a run with nothing ticked still
    /// has this to write.
    /// </summary>
    public bool HasFlattenedPlaces => _flattenedPlaces.Values.Any(places => places.Count > 0);

    /// <summary>Where a document's bytes actually are: its working copy, or the file itself.</summary>
    string ReadPathOf(string filePath) =>
        _workingCopies.TryGetValue(filePath, out var copy) ? copy : filePath;

    List<OverlapRegion> PlacesFlattenedIn(string filePath) =>
        _flattenedPlaces.TryGetValue(filePath, out var places)
            ? places
            : _flattenedPlaces[filePath] = new List<OverlapRegion>();

    int IndexOfDocument(string filePath) => _documents.FindIndex(
        d => CleanedFileNamer.WouldOverwriteSource(d.FilePath, filePath));

    /// <summary>
    /// Flatten these places now, so the result is on screen before anything is
    /// saved. Merging and splitting take effect the moment they are asked for,
    /// and flattening reserving itself for the save was the odd one out — a tick
    /// that did nothing visible until a file was written.
    ///
    /// Returns how many places were flattened.
    /// </summary>
    public async Task<int> FlattenAsync(
        IReadOnlyDictionary<string, IReadOnlyList<OverlapRegion>> placesByFile,
        IProgress<AnalysisProgress>? progress = null,
        CancellationToken ct = default)
    {
        int flattened = 0;
        foreach (var (filePath, places) in placesByFile)
        {
            ct.ThrowIfCancellationRequested();
            int index = IndexOfDocument(filePath);
            if (index < 0 || places.Count == 0) continue;

            var applied = PlacesFlattenedIn(_documents[index].FilePath);
            foreach (var place in places) AbsorbAndAdd(applied, place);
            await RebuildWorkingCopyAsync(index, progress, ct).ConfigureAwait(false);
            flattened += places.Count;
        }
        return flattened;
    }

    /// <summary>
    /// Add one place to what has been flattened in a file, absorbing any earlier
    /// flatten it swallows.
    ///
    /// The picture an earlier flatten drew is an object like any other, so the
    /// user can tick it and flatten it again together with a neighbour. Its
    /// bytes exist only in the working copy, and the copy is always rebuilt from
    /// the user's own file — so a place naming it would find nothing there. What
    /// the new place really covers is the objects the earlier one covered, and
    /// that is what is stored: the earlier place is dropped and its members join
    /// the new one.
    /// </summary>
    static void AbsorbAndAdd(List<OverlapRegion> applied, OverlapRegion place)
    {
        var members = new List<PlacedObject>();
        foreach (var member in place.Members)
        {
            int swallowed = member.Kind == RemovableKind.Image
                ? applied.FindIndex(earlier => earlier.PageNumber == place.PageNumber
                    && IsPictureAt(member.X, member.Y, member.Width, member.Height, earlier))
                : -1;
            if (swallowed < 0)
            {
                members.Add(member);
                continue;
            }
            members.AddRange(applied[swallowed].Members);
            applied.RemoveAt(swallowed);
        }
        applied.Add(place with { Members = members });
    }

    /// <summary>
    /// Whether a rectangle is where a flatten drew its picture: the cleaner
    /// places the picture over exactly the place it flattened, so the geometry
    /// identifies it. To within a tenth of a point — the rectangle makes a round
    /// trip through the file and comes back that close.
    /// </summary>
    static bool IsPictureAt(double x, double y, double width, double height, OverlapRegion place) =>
        Math.Abs(x - place.X) < 0.1
        && Math.Abs(y - place.Y) < 0.1
        && Math.Abs(width - place.Width) < 0.1
        && Math.Abs(height - place.Height) < 0.1;

    /// <summary>
    /// The place a flatten drew this occurrence for, or null when it is an
    /// ordinary image. What makes the undo button live, and what it acts on.
    /// </summary>
    public OverlapRegion? FlattenBehind(string filePath, PdfImageOccurrence occurrence)
    {
        int index = IndexOfDocument(filePath);
        if (index < 0 || !_flattenedPlaces.TryGetValue(_documents[index].FilePath, out var places))
        {
            return null;
        }
        return places.FirstOrDefault(place => place.PageNumber == occurrence.PageNumber
            && IsPictureAt(occurrence.X, occurrence.Y, occurrence.Width, occurrence.Height, place));
    }

    /// <summary>
    /// Take one flatten back: the place is dropped and the working copy is built
    /// again from the user's file with the rest, which puts the objects it
    /// covered back in the list.
    /// </summary>
    public async Task<bool> UndoFlattenAsync(
        string filePath,
        OverlapRegion place,
        IProgress<AnalysisProgress>? progress = null,
        CancellationToken ct = default)
    {
        int index = IndexOfDocument(filePath);
        if (index < 0) return false;

        var applied = PlacesFlattenedIn(_documents[index].FilePath);
        if (!applied.Remove(place)) return false;

        await RebuildWorkingCopyAsync(index, progress, ct).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Write the file the workspace describes: the user's file with every place
    /// they have flattened baked in, all in one pass. Then read it back, which
    /// is the same rule the save follows — the list says what a file holds, and
    /// never what we believe we did to it.
    ///
    /// Not verified. The verifier's question is whether a removal survived the
    /// write, this run removes nothing, and it costs a second parse of the whole
    /// document on every press. The save that finally writes the user's file
    /// verifies as it always did.
    /// </summary>
    async Task RebuildWorkingCopyAsync(
        int index, IProgress<AnalysisProgress>? progress, CancellationToken ct)
    {
        var document = _documents[index];
        var places = PlacesFlattenedIn(document.FilePath);

        var readPath = document.FilePath;
        string? nextCopy = null;
        if (places.Count > 0)
        {
            nextCopy = WorkingCopyPath(document.FilePath);
            // Not fitted to the screen: this copy is an intermediate the user
            // never keeps, and re-encoding its pictures here would only mean
            // encoding them again on the next rebuild.
            var result = await _cleaner.CleanAsync(
                    document.FilePath, nextCopy, Array.Empty<ImageRemovalSelection>(), places,
                    regionsToClear: null, fitImagesToScreen: false, ct)
                .ConfigureAwait(false);
            _logger.LogInformation(
                "flattened: placesAsked={Asked} placesFlattened={Flattened} pagesModified={Pages}",
                places.Count, result.RegionsFlattened, result.PagesModified);
            readPath = nextCopy;
        }

        var info = await AnalyzeForWorkspaceAsync(readPath, progress, ct).ConfigureAwait(false);
        DiscardWorkingCopy(document.FilePath);
        if (nextCopy is not null) _workingCopies[document.FilePath] = nextCopy;

        // Labelled with the user's path, whatever it was read from: that is the
        // document they opened, now showing what they have done to it.
        _documents[index] = info with { FilePath = document.FilePath };
        RebuildGroups();
    }

    static string WorkingCopyPath(string filePath)
    {
        // One folder for the run, a new name every rebuild: the previous copy is
        // deleted only after the new one has been read, and a re-used path could
        // not be deleted while it was still the file being described.
        var folder = Path.Combine(Path.GetTempPath(), "PdfImageRemoverForRag", "working");
        Directory.CreateDirectory(folder);
        return Path.Combine(folder,
            $"{Path.GetFileNameWithoutExtension(filePath)}-{Guid.NewGuid():N}.pdf");
    }

    void DiscardWorkingCopy(string filePath)
    {
        if (!_workingCopies.TryGetValue(filePath, out var copy)) return;
        _workingCopies.Remove(filePath);
        TryDeleteTempFile(copy);
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
                        || filesToFlatten?.Any(f => CleanedFileNamer.WouldOverwriteSource(f, d.FilePath)) == true
                        // A file whose places were flattened, or whose layers
                        // were hidden, already has something to write — even
                        // with nothing ticked.
                        || PlacesFlattenedIn(d.FilePath).Count > 0
                        || HiddenIn(d.FilePath).Count > 0)
            .Select(d => d.FilePath)
            .ToArray();
    }

    /// <summary>
    /// Remove the selected object groups from every affected file and write the
    /// result. Each file runs the spec §15 sequence independently: clean into a
    /// temp file, verify the temp, move to the final name only on success,
    /// delete the temp on failure.
    /// <paramref name="resolveDestination"/> maps each source path to its output
    /// path (chosen by the UI beforehand).
    ///
    /// Flattening is not part of this any more. It happens when the user asks
    /// for it, into a working copy this reads from, so what a save has left to
    /// do is the removals — and, when there are none, a copy.
    /// </summary>
    public async Task<BatchSaveResult> RemoveAndSaveAsync(
        IReadOnlyCollection<string> selectedHashes,
        Func<string, string> resolveDestination,
        IProgress<AnalysisProgress>? reanalysisProgress = null,
        CancellationToken ct = default)
    {
        if (selectedHashes.Count == 0 && !HasFlattenedPlaces && !HasHiddenPlacements)
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
            // Places flattened before the save are already in the working copy
            // this run reads from, so they are not done again — but they are
            // what is being written, and the count has to say so.
            int flattenedAlready = PlacesFlattenedIn(document.FilePath).Count;
            var hidden = HiddenIn(document.FilePath);
            if (documentSelections.Count == 0 && flattenedAlready == 0 && hidden.Count == 0)
            {
                continue;
            }

            var destinationPath = resolveDestination(document.FilePath);
            var saved = await CleanVerifyCommitAsync(
                    document, destinationPath, documentSelections, hidden, selectedHashes, ct)
                .ConfigureAwait(false);
            savedFiles.Add(saved);
            totalRemoved += saved.DrawCallsRemoved;
            totalFlattened += flattenedAlready;
        }

        // The workspace now describes the files that were just written, not the
        // ones that were open a moment ago. Reading them back is the only way
        // it can be right about everything at once: what was deleted is gone,
        // what was flattened is gone, and the picture flattening drew is there
        // — with its real identity, which nothing in memory could have known.
        // Keeping the list in step by hand was tried first and produced three
        // separate defects in one afternoon.
        foreach (var saved in savedFiles)
        {
            ct.ThrowIfCancellationRequested();
            int index = IndexOfDocument(saved.SourcePath);
            if (index < 0) continue;

            // The file the user owns now holds what was flattened, so the
            // working copy has done its job: it goes, and with it the record of
            // what had been flattened but not written.
            DiscardWorkingCopy(_documents[index].FilePath);
            _flattenedPlaces.Remove(_documents[index].FilePath);
            _hiddenPlacements.Remove(_documents[index].FilePath);

            _documents[index] = await AnalyzeForWorkspaceAsync(
                saved.DestinationPath, reanalysisProgress, ct).ConfigureAwait(false);
        }
        if (savedFiles.Count > 0) RebuildGroups();

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
        IReadOnlyList<OverlapRegion> hiddenPlacements,
        IReadOnlyCollection<string> selectedHashes,
        CancellationToken ct)
    {
        if (CleanedFileNamer.WouldOverwriteSource(document.FilePath, destinationPath))
        {
            throw new PdfCleanerException(PdfCleanerErrorKind.DestinationNotWritable,
                L10n.ErrorSameAsSource);
        }

        // What is read is the working copy when there is one — the file that
        // already holds the places flattened before the save. What is COMPARED
        // against, above and in the verifier, is still the user's own path.
        var sourcePath = ReadPathOf(document.FilePath);

        // Temp file in the destination directory so the final File.Move is
        // an atomic same-volume rename.
        var tempPath = destinationPath + ".part";
        try
        {
            // Nothing left to do to the bytes: the places the user flattened are
            // already in the working copy this reads from, and nothing is ticked
            // for removal. So the save is a copy. The cleaner refuses a run with
            // nothing in it, and rightly — being asked to change nothing is a
            // caller's mistake everywhere else.
            if (selections.Count == 0 && hiddenPlacements.Count == 0)
            {
                File.Copy(sourcePath, tempPath, overwrite: true);
                File.Move(tempPath, destinationPath, overwrite: true);
                _logger.LogInformation("saved: copied the working copy, nothing further to change");
                return new SavedFile(document.FilePath, destinationPath, 0, 0);
            }

            // This is the file the user keeps, so it is the one whose images are
            // redrawn at the size they will be looked at.
            var result = await _cleaner
                .CleanAsync(sourcePath, tempPath, selections,
                    regionsToFlatten: null, regionsToClear: hiddenPlacements,
                    fitImagesToScreen: true, ct)
                .ConfigureAwait(false);

            // Logged before verification so a verification failure can be read
            // against what the cleaner believed it did: zero draw calls removed
            // means the selection never matched anything, a full count means it
            // matched but the result did not survive the write.
            // imagesKeptBack is normally zero. When it is not, the file holds an
            // image something other than a page points at — an annotation, a
            // pattern — so its object had to stay even though its pages no
            // longer draw it. That is the one case where removal cannot fully
            // deliver, and without this line it would be invisible.
            _logger.LogInformation(
                "cleaned: selections={Selections} pagesModified={Pages} " +
                "drawCallsRemoved={Removed} imagesKeptBack={KeptBack}",
                selections.Count, result.PagesModified, result.DrawCallsRemoved,
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
            // An image redrawn at screen size is no longer the stream it was, so
            // looking for its old bytes in the output would report it missing.
            // It is not missing; it is smaller, which is what was asked for.
            var resized = new HashSet<string>(
                result.ResizedImageHashes ?? Array.Empty<string>(), StringComparer.Ordinal);
            var retainedHashes = verifiableHashes
                .Except(removedHashes, StringComparer.Ordinal)
                .Where(hash => !resized.Contains(hash))
                .ToArray();
            var report = await _verifier.VerifyAsync(
                sourcePath, tempPath, removedHashes, retainedHashes, ct)
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
                result.DrawCallsRemoved, result.RegionsFlattened);
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
