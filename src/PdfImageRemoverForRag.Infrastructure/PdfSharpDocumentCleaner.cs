using System.Diagnostics;
using PdfImageRemoverForRag.Core.Abstractions;
using PdfImageRemoverForRag.Core.Errors;
using PdfImageRemoverForRag.Core.Validation;
using PdfImageRemoverForRag.Core.Formatting;
using PdfImageRemoverForRag.Core.Models;
using PdfImageRemoverForRag.Infrastructure.Internal;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.Content;
using PdfSharp.Pdf.Content.Objects;
using PdfSharp.Pdf.IO;

namespace PdfImageRemoverForRag.Infrastructure;

/// <summary>
/// PDFsharp-backed implementation of <see cref="IPdfDocumentCleaner"/>.
/// Given a list of removal selections, rewrites each affected page's
/// content stream to drop the target <c>Do</c> operators, then saves the
/// document through a temp file so a partial write never corrupts the
/// destination (spec §15).
///
/// It also performs the opposite operation, flattening: an overlap region is
/// rendered to pixels, the objects that make it up are deleted at that one
/// place, and the rendering is drawn in their stead. The page looks the same and
/// its text layer is that much cleaner, which is the point — a RAG pipeline
/// reads the text layer, not the picture.
/// </summary>
public sealed class PdfSharpDocumentCleaner : IPdfDocumentCleaner
{
    /// <summary>
    /// Resolution flattened regions are rendered at. 200 dpi keeps small labels
    /// legible in the replacement image without turning a chart into a megabyte;
    /// the rasterizer renders below it when the region is large enough to hit its
    /// own pixel ceiling.
    /// </summary>
    const int FlattenDpi = 200;

    /// <summary>
    /// The screen the output is for, and the quality a JPEG is written at.
    /// Both come from what the file is for: it goes to a RAG pipeline whose
    /// reader displays it on an ordinary screen, and whose upload limit a manual
    /// full of screenshots reaches easily.
    /// </summary>
    const int ScreenWidth = 1920;
    const int ScreenHeight = 1080;
    const int ScreenJpegQuality = 85;

    readonly IPageRasterizer? _rasterizer;
    readonly IImageResampler? _resampler;

    /// <param name="rasterizer">
    /// Renders the regions a caller asks to flatten. Optional because plain
    /// removal needs no renderer, and because the only real implementation is
    /// Windows-only (see <see cref="IPageRasterizer"/>) while this assembly has
    /// to keep building and testing on macOS.
    /// </param>
    /// <param name="resampler">
    /// Redraws images at the size they will be looked at. Optional for the same
    /// two reasons, and absent means images are written out as they came in.
    /// </param>
    public PdfSharpDocumentCleaner(
        IPageRasterizer? rasterizer = null, IImageResampler? resampler = null)
    {
        _rasterizer = rasterizer;
        _resampler = resampler;
    }

    public async Task<CleaningResult> CleanAsync(
        string sourcePath,
        string destinationPath,
        IReadOnlyList<ObjectRemovalSelection> selections,
        IReadOnlyList<OverlapRegion>? regionsToFlatten = null,
        IReadOnlyList<OverlapRegion>? regionsToClear = null,
        bool fitImagesToScreen = false,
        CancellationToken ct = default)
    {
        var regions = regionsToFlatten ?? Array.Empty<OverlapRegion>();
        var cleared = regionsToClear ?? Array.Empty<OverlapRegion>();

        // Hard rule from the spec: never overwrite the source, even if the
        // App accidentally supplies the same path.
        if (CleanedFileNamer.WouldOverwriteSource(sourcePath, destinationPath))
        {
            throw new PdfCleanerException(PdfCleanerErrorKind.DestinationNotWritable,
                "元 PDF と同じパスへの保存はできません。別名を指定してください。");
        }
        // Being asked to change nothing is a caller's mistake — EXCEPT when the
        // fitting is the job. A save that only flattened has nothing left to
        // remove, and the flattening is already in the file this reads from; the
        // one thing still owed to the file the user keeps is its images at the
        // size they will be looked at. Refusing that run is what silently
        // skipped the fitting on every flatten-only save.
        if (selections.Count == 0 && regions.Count == 0 && cleared.Count == 0
            && !fitImagesToScreen)
        {
            throw new PdfCleanerException(PdfCleanerErrorKind.Unexpected,
                "削除対象の画像が指定されていません。");
        }
        // A caller that asks for flattening without supplying a renderer has a
        // wiring bug, and quietly saving a file with nothing flattened would
        // hide it. Compare the runtime case below, where a region that will not
        // render is skipped: that one is the file's fault, not the code's.
        if (regions.Count > 0 && _rasterizer is null)
        {
            throw new PdfCleanerException(PdfCleanerErrorKind.Unexpected,
                "画像に統合するための描画機能が利用できません。");
        }
        // Re-checked rather than trusted from the open: the file has been
        // sitting on disk since it was analyzed and may have been replaced.
        if (!PdfFileSignature.LooksLikePdf(sourcePath))
        {
            throw new PdfCleanerException(PdfCleanerErrorKind.NotAPdf,
                "選択されたファイルは PDF ではありません。");
        }

        // Rendering happens before anything is rewritten, and from the source
        // file: the replacement has to show the region as it looks now. It is
        // also the only asynchronous part of the job, so it stays out here
        // rather than being waited on inside the synchronous rewrite.
        var flattenImages = await RenderRegionsAsync(sourcePath, regions, ct).ConfigureAwait(false);
        // The places to empty need no rendering, so they join the same list with
        // nothing to draw: one pass over the page handles both.
        flattenImages.AddRange(cleared.Select(region => new FlattenImage(region, null)));

        // Fitting is asked for only when the file being written is the one the
        // user keeps, and it needs a resampler to do it with.
        var resampler = fitImagesToScreen ? _resampler : null;

        return await Task
            .Run(() => CleanSync(sourcePath, destinationPath, selections, flattenImages, resampler, ct), ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// One region and the pixels that will replace it — or no pixels, which is
    /// a region whose objects are simply taken out. Hiding a layer is that: the
    /// objects go and nothing is drawn in their place, at ONE place on ONE page,
    /// which is what tells it apart from removing the object everywhere.
    /// </summary>
    sealed record FlattenImage(OverlapRegion Region, byte[]? Png);

    /// <summary>
    /// Render every requested region, dropping the ones that cannot be rendered.
    /// Skipping is the only safe response: deleting a region's objects and then
    /// having nothing to draw would leave a white hole in the page, which is
    /// worse than not flattening it at all.
    /// </summary>
    async Task<List<FlattenImage>> RenderRegionsAsync(
        string sourcePath, IReadOnlyList<OverlapRegion> regions, CancellationToken ct)
    {
        var images = new List<FlattenImage>(regions.Count);
        foreach (var region in regions)
        {
            ct.ThrowIfCancellationRequested();

            // Rendered from a copy holding only the ticked objects, on a
            // transparent background — see FlattenSourceIsolator for what
            // rendering the page as it stands did instead. Falling back to the
            // page itself keeps the old behavior when the copy cannot be
            // written, which is a cropped neighbor in the picture rather than
            // no picture at all.
            var isolatedPath = Path.Combine(
                Path.GetTempPath(), $"pdfimageremover-flatten-{Guid.NewGuid():N}.pdf");
            var rendered = FlattenSourceIsolator.Write(sourcePath, region, isolatedPath);

            try
            {
                var png = await _rasterizer!
                    .RenderRegionAsync(
                        rendered ?? sourcePath,
                        rendered is null ? region.PageNumber : 1,
                        PageRegion.Of(region), FlattenDpi,
                        transparentBackground: rendered is not null, ct)
                    .ConfigureAwait(false);
                if (png is null || png.Length == 0) continue;
                images.Add(new FlattenImage(region, png));
            }
            finally
            {
                if (rendered is not null) TryDelete(rendered);
            }
        }
        return images;
    }

    static CleaningResult CleanSync(string sourcePath, string destinationPath,
        IReadOnlyList<ObjectRemovalSelection> selections,
        IReadOnlyList<FlattenImage> flattenImages,
        IImageResampler? resampler, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        // Selections carry a GroupId but the cleaner needs a set of PDF
        // indirect-object identifiers so it can match Image XObjects in the
        // /XObject dictionary. Every occurrence carries the objectId of the
        // Image XObject it draws, so unioning those gives us the target set
        // without a separate look-up table.
        // Images are matched by stream hash — the same identity used to group
        // them in the list and to verify the saved file.
        //
        // Matching on the indirect-object id of each occurrence looked
        // equivalent and is not: a document can hold the same image bytes as
        // several distinct objects (one per page is common), and the occurrence
        // list then names only the objects that were seen. Pages referencing a
        // different copy kept their image, and the save failed verification
        // with "page N still draws /ImX" for most of the document.
        //
        // Shadows join the images here rather than getting a branch of their
        // own: a shadow layer IS an Image XObject, drawn by the same Do
        // operator and named in the same resource dictionary. Only the list
        // the user reads tells them apart.
        var selectedImageHashes = new HashSet<string>(
            selections
                .Where(s => s.Kind.IsImageXObject() && s.Hash is not null)
                .Select(s => s.Hash!),
            StringComparer.Ordinal);

        // Text selections are matched by their shown string, not an object id.
        var selectedTextValues = new HashSet<string>(
            selections
                .Where(s => s.Kind == RemovableKind.Text && s.TextValue is not null)
                .Select(s => s.TextValue!),
            StringComparer.Ordinal);

        // Drawing selections are matched by the form's stream hash, for the same
        // reason images are: a form is a stream the file stores, and the same
        // artwork can sit in the file as more than one object.
        var selectedDrawingHashes = new HashSet<string>(
            selections
                .Where(s => s.Kind == RemovableKind.Drawing && s.Hash is not null)
                .Select(s => s.Hash!),
            StringComparer.Ordinal);

        // Shape selections are matched by their path signature (stored in TextValue).
        var selectedShapeSignatures = new HashSet<string>(
            selections
                .Where(s => s.Kind == RemovableKind.Shape && s.TextValue is not null)
                .Select(s => s.TextValue!),
            StringComparer.Ordinal);

        int pagesModified = 0;
        int totalRemovedOps = 0;
        int regionsFlattened = 0;
        var removedHashes = new HashSet<string>(StringComparer.Ordinal);
        // Filled while the pages are swept, and acted on once at the end —
        // deleting an object while its page is still being rewritten would be
        // pulling the floor up behind us.
        var doomedImages = new Dictionary<PdfObjectID, PdfDictionary>();
        var resourceEntriesToDrop = new List<(PdfResources Resources, HashSet<string> Names)>();
        // XImage keeps its stream and is encoded into the document when it is
        // saved, so the images drawn for flattened regions have to outlive the
        // page loop.
        var placedImages = new List<IDisposable>();

        try
        {
            using var doc = PdfReader.Open(sourcePath, PdfDocumentOpenMode.Modify);

            for (int i = 0; i < doc.PageCount; i++)
            {
                ct.ThrowIfCancellationRequested();
                var page = doc.Pages[i];
                int pageNumber = i + 1;
                var pageFlatten = flattenImages.Where(f => f.Region.PageNumber == pageNumber).ToList();
                var namesToDrop = ResolveNamesForHashes(
                    page.Resources, selectedImageHashes, removedHashes, doomedImages);
                // A drawing joins the same set once its form is found by hash,
                // which gives it the whole treatment images get: the page's Do
                // call goes, the resource entry goes, and the object itself goes
                // if nothing else in the document still points at it. The form's
                // own content stream is never rewritten — that is what makes
                // removing a drawing from a page safe for every other page.
                namesToDrop.UnionWith(ResolveFormNamesForHashes(
                    page.Resources, selectedDrawingHashes, removedHashes, doomedImages));
                // Recorded before the page can be skipped below: a page may list
                // the image without drawing it (a leftover from an earlier pass)
                // and that entry has to go too.
                if (namesToDrop.Count > 0 && page.Resources is not null)
                {
                    resourceEntriesToDrop.Add((page.Resources, namesToDrop));
                }
                if (namesToDrop.Count == 0
                    && selectedTextValues.Count == 0
                    && selectedShapeSignatures.Count == 0
                    && pageFlatten.Count == 0) continue;

                var contentBytes = PageContentAccessor.ReadMergedBytes(page);
                var sequence = ContentReader.ReadContent(contentBytes);
                int removed = 0;

                // Flattening runs first because it deletes only the instances
                // that sit inside one region, and it can only find them while
                // they are still there. Removing a group afterwards may take
                // other showings of the same string with it — which is exactly
                // what the delete side is for.
                var flattenedHere = new List<FlattenImage>();
                // The resource names whose draw calls flattening deleted. Kept
                // so the page's entry for an image nothing draws any more can go
                // with it — see the block below ReplaceContent.
                var flattenedNames = new HashSet<string>(StringComparer.Ordinal);
                // Counted apart from the rest: flattening takes draw calls out
                // too, but it puts a picture of them back, so reporting the two
                // together would tell a user who only flattened that N things
                // were deleted. They are added back into `removed` because that
                // is the "did this page change at all" gate, and subtracted
                // again when the run's removal total is accumulated.
                int flattenedOps = 0;
                foreach (var flatten in pageFlatten)
                {
                    // Where the picture belongs is decided BEFORE the members
                    // go, because it is decided by where the lowest of them was.
                    var (removedForRegion, firstIndex) = RemoveRegionMembers(
                        page, sequence, flatten.Region, flattenedNames);
                    // Nothing matched: the objects are no longer where analysis
                    // saw them, so there is nothing to replace and drawing the
                    // rendering would just lay a second copy over the original.
                    if (removedForRegion == 0) continue;
                    removed += removedForRegion;
                    // A region with nothing to draw is a deletion and counts as
                    // one; only a region that puts a picture back is subtracted
                    // from the removal total.
                    if (flatten.Png is null) continue;
                    flattenedOps += removedForRegion;
                    flattenedHere.Add(flatten);
                    // Drawn into the sequence as it stands, and not appended to
                    // the page afterwards: the picture stands in for objects
                    // that were somewhere in the middle of the drawing order,
                    // and drawing it last put it over things it used to be
                    // under. Everything removed later shifts it along, which is
                    // why it goes in now rather than at the end of the page.
                    PlacePicture(page, sequence, flatten, firstIndex, placedImages);
                }

                if (namesToDrop.Count > 0)
                {
                    removed += ContentStreamWalker.RemoveDoOperators(sequence, namesToDrop);
                }
                if (selectedTextValues.Count > 0)
                {
                    var textDecoder = new PdfTextDecoder(page.Resources);
                    removed += ContentStreamWalker.RemoveTextOperators(
                        sequence, selectedTextValues, textDecoder);
                }
                if (selectedShapeSignatures.Count > 0)
                {
                    removed += ContentStreamWalker.RemoveShapes(sequence, selectedShapeSignatures);
                }
                if (removed == 0) continue;

                // Flattening replaces a place on the page with a picture of
                // itself, so an image whose only draw call was inside the
                // region is not drawn any more — but its entry in the page's
                // resources, and the object behind it, were being left in the
                // file. A reader that enumerates objects still handed the
                // original picture to whatever consumed the document, which is
                // exactly the fault that once forced a release to be withdrawn
                // for the removal path. Asked of the rewritten stream, so an
                // image the page draws somewhere else as well is kept, entry
                // and all.
                if (flattenedNames.Count > 0)
                {
                    var stillDrawn = ContentStreamWalker.FindDrawCalls(sequence)
                        .Select(call => call.ResourceName)
                        .ToHashSet(StringComparer.Ordinal);
                    var undrawnNames = flattenedNames
                        .Where(name => !stillDrawn.Contains(name))
                        .ToHashSet(StringComparer.Ordinal);

                    if (undrawnNames.Count > 0 && page.Resources is not null)
                    {
                        // The objects themselves are only candidates: whether
                        // they can go is decided once every page has been
                        // rewritten, by asking the document what still points
                        // at them.
                        foreach (var entry in ImageXObjectCollector.EnumerateImageEntries(page.Resources))
                        {
                            if (undrawnNames.Contains(entry.ResourceName))
                                doomedImages[entry.Dictionary.Internals.ObjectID] = entry.Dictionary;
                        }
                        resourceEntriesToDrop.Add((page.Resources, undrawnNames));
                    }
                }

                // The pictures are already in the sequence, each at the place
                // its objects were drawn. ReplaceContent collapses the page into
                // one stream, which is also what throws away the scratch streams
                // PDFsharp wrote while the images were being made.
                page.Contents.ReplaceContent(sequence);
                regionsFlattened += flattenedHere.Count;
                pagesModified++;
                totalRemovedOps += removed - flattenedOps;
            }

            // Dropping the draw calls only stops the image being PAINTED. The
            // XObject and its bytes stay in the file, and a tool that reads a
            // PDF by enumerating objects rather than by rendering it — which is
            // what a RAG ingestion pipeline does — still finds every image the
            // user asked to be rid of. Measured on a real 39-page manual: 27
            // "removed" images and 26 of their soft masks were all still there
            // and all still extractable. For a product whose whole purpose is
            // keeping images out of RAG, removing the reference is the job.
            int imagesKeptBack = PruneRemovedImages(doc, resourceEntriesToDrop, doomedImages);

            // Last, because it acts on what the file will actually hold: the
            // pictures flattening drew are images like any other by now, and
            // anything removed above is no longer here to be redrawn.
            var resizedImages = resampler is null
                ? Array.Empty<string>()
                : ScreenFitPass.Apply(
                    doc, resampler, ScreenWidth, ScreenHeight, ScreenJpegQuality, ct);

            // Save via a temp file. On disposal-time failure we clean up the
            // temp so the caller never has to reason about half-written state.
            var tempPath = destinationPath + ".tmp";
            try
            {
                doc.Save(tempPath);
                if (File.Exists(destinationPath)) File.Delete(destinationPath);
                File.Move(tempPath, destinationPath);
            }
            catch
            {
                if (File.Exists(tempPath)) TryDelete(tempPath);
                throw;
            }

            return new CleaningResult(
                SourcePath: sourcePath,
                DestinationPath: destinationPath,
                RemovedGroupHashes: removedHashes.ToArray(),
                PagesModified: pagesModified,
                DrawCallsRemoved: totalRemovedOps,
                Elapsed: sw.Elapsed,
                RegionsFlattened: regionsFlattened,
                ImagesKeptForOtherReferences: imagesKeptBack,
                ResizedImageHashes: resizedImages);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (PdfCleanerException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw PdfsharpExceptionMapper.Map(ex, "PDF 保存");
        }
        finally
        {
            foreach (var image in placedImages) image.Dispose();
        }
    }

    /// <summary>
    /// Delete the region's members at that one place on the page, and return how
    /// many operators (or operator ranges) went.
    /// </summary>
    static (int Removed, int FirstIndex) RemoveRegionMembers(
        PdfPage page, CSequence sequence, OverlapRegion region, HashSet<string> flattenedNames)
    {
        // The region names its image members by stream hash, while the page
        // draws them through resource names, so the same resolution plain
        // removal does is needed here. The names are handed back to the caller,
        // which decides afterwards — from the rewritten stream — whether the
        // page still draws them.
        //
        // What is deliberately NOT done is recording those hashes as removed,
        // or those objects as doomed here: a flattened image is usually still
        // drawn elsewhere, and marking it at this point would tear it out of
        // every other page that draws it. Both collectors are given throwaways.
        // Shadows count as images here for the same reason they do everywhere
        // else: they are drawn by a Do naming an image entry. Leaving them out
        // would flatten a region and then paint the shadow back over the
        // rendering.
        var imageHashes = new HashSet<string>(
            region.Members.Where(m => m.Kind.IsImageXObject()).Select(m => m.Identity),
            StringComparer.Ordinal);
        var namesInRegion = ResolveNamesForHashes(
            page.Resources, imageHashes,
            new HashSet<string>(StringComparer.Ordinal),
            new Dictionary<PdfObjectID, PdfDictionary>());
        flattenedNames.UnionWith(namesInRegion);

        return ContentStreamWalker.RemoveInRegion(
            sequence, region, namesInRegion,
            new PdfTextDecoder(page.Resources), new PdfFontMetrics(page.Resources));
    }

    /// <summary>
    /// Put the region's rendering into the page's own drawing order, where the
    /// lowest object it replaces was — so a picture that stood under something
    /// goes on standing under it.
    ///
    /// It used to be appended to the page as a stream of its own, which drew it
    /// last and so over everything: an object the user had kept inside the
    /// region disappeared behind the picture of its neighbors.
    ///
    /// The image is added to <paramref name="keepAlive"/> rather than disposed
    /// here because PDFsharp reads its stream when the document is saved.
    /// </summary>
    static void PlacePicture(
        PdfPage page, CSequence sequence, FlattenImage flatten, int firstIndex,
        List<IDisposable> keepAlive)
    {
        var operators = PictureOperators(page, flatten, keepAlive);

        // Where the block may go without landing inside somebody else's saved
        // state. -1 means the page transforms its whole top level, which this
        // cannot honor — there the picture goes last, as it always did.
        int at = ContentStreamWalker.InsertionPointFor(sequence, firstIndex);
        if (at < 0) at = sequence.Count;

        for (int i = operators.Count - 1; i >= 0; i--) sequence.Insert(at, operators[i]);
    }

    /// <summary>
    /// Make the picture an image of the page and answer the operators that draw
    /// it.
    ///
    /// PDFsharp has no way to say "give me an image XObject": the drawing is
    /// what makes one. So it draws into a scratch content stream — which
    /// registers the image and names it in the page's resources — and only the
    /// operators are taken. The stream itself is left for ReplaceContent to
    /// discard, since the whole point is that the picture goes somewhere else.
    ///
    /// XGraphics measures from the top-left with y growing down, while a region
    /// is in PDF space with the origin at the bottom-left, so the top edge is
    /// the page height less the region's upper edge. (Pages with a /Rotate entry
    /// are not handled specially — analysis reads content-stream coordinates
    /// too, so both sides are consistent.)
    /// </summary>
    static CSequence PictureOperators(
        PdfPage page, FlattenImage flatten, List<IDisposable> keepAlive)
    {
        var region = flatten.Region;
        using (var gfx = XGraphics.FromPdfPage(
                   page, XGraphicsPdfPageOptions.Append, XGraphicsUnit.Point))
        {
            var image = XImage.FromStream(new MemoryStream(flatten.Png!));
            keepAlive.Add(image);
            double top = page.Height.Point - (region.Y + region.Height);
            gfx.DrawImage(image, new XRect(region.X, top, region.Width, region.Height));
        }

        var written = page.Contents.Elements[page.Contents.Elements.Count - 1];
        var scratch = (written is PdfReference reference ? reference.Value : written) as PdfDictionary;
        return scratch is null
            ? new CSequence()
            : ContentReader.ReadContent(scratch.Stream.UnfilteredValue);
    }

    /// <summary>
    /// Take the removed images out of the document itself: their entry in every
    /// page's <c>/XObject</c> resources, the image object, and the soft mask
    /// hanging off it.
    ///
    /// The mask goes with its parent and never on its own. A <c>/SMask</c> is
    /// the parent image's alpha channel, so it is meaningless without it — but
    /// equally, a mask belonging to an image the user KEPT must stay, or that
    /// image loses its transparency. This is also why masks are not offered in
    /// the object list: they are not objects a person put on the page.
    ///
    /// An object is deleted only once NOTHING in the document still points at
    /// it. That rule is checked, not argued: a page is far from the only thing
    /// that can name an image. Annotation appearance streams, tiling patterns,
    /// soft-mask groups in an ExtGState and Type3 glyph procedures all carry
    /// their own resources, none of which analysis looks at — so an image drawn
    /// on a page AND used by an annotation is listed, is selectable, and would
    /// leave a dangling reference behind if the object were simply removed.
    /// Dropping the reference from the page is always safe; dropping the object
    /// is not, and this is what tells the two apart.
    /// </summary>
    /// <returns>
    /// How many images had to be left in the file because something else still
    /// referenced them. Their pages no longer draw or list them either way.
    /// </returns>
    static int PruneRemovedImages(
        PdfDocument doc,
        IReadOnlyList<(PdfResources Resources, HashSet<string> Names)> pageEntries,
        IReadOnlyDictionary<PdfObjectID, PdfDictionary> doomed)
    {
        foreach (var (resources, names) in pageEntries)
        {
            ImageXObjectCollector.RemoveEntries(resources, names);
        }
        if (doomed.Count == 0) return 0;

        // Asked AFTER the page entries are gone, so those references do not
        // keep their own images alive.
        var stillReferenced = ReferencedObjectIds(doc);
        var orphanedMasks = new List<PdfDictionary>();
        int keptBack = 0;

        foreach (var image in doomed.Values)
        {
            if (stillReferenced.Contains(image.Internals.ObjectID))
            {
                keptBack++;
                continue;
            }
            orphanedMasks.AddRange(MasksOf(image));
            TryRemoveObject(doc, image);
        }

        // A mask is reachable only through its parent, so it can only be judged
        // once the parents are gone — hence the second look rather than one
        // combined pass.
        if (orphanedMasks.Count == 0) return keptBack;
        var afterImages = ReferencedObjectIds(doc);
        foreach (var mask in orphanedMasks)
        {
            if (!afterImages.Contains(mask.Internals.ObjectID)) TryRemoveObject(doc, mask);
        }
        return keptBack;
    }

    /// <summary>
    /// The image objects an image depends on: its soft mask, its stencil mask,
    /// or both. A <c>/Mask</c> may instead be an array of color-key ranges,
    /// which is not an object and has nothing to delete.
    /// </summary>
    static IEnumerable<PdfDictionary> MasksOf(PdfDictionary image)
    {
        foreach (var key in new[] { "/SMask", "/Mask" })
        {
            if (ImageXObjectCollector.ResolveDictionary(image.Elements[key]) is PdfDictionary mask
                && mask.Elements.GetName("/Subtype") == "/Image")
            {
                yield return mask;
            }
        }
    }

    /// <summary>
    /// Every object id named by an indirect reference anywhere in the document.
    /// References are collected, never followed: whatever an indirect reference
    /// points at is enumerated in its own right, so following would only risk
    /// looping. Direct dictionaries and arrays nested inside an object ARE
    /// walked — that is where a resource dictionary usually lives.
    /// </summary>
    static HashSet<PdfObjectID> ReferencedObjectIds(PdfDocument doc)
    {
        var referenced = new HashSet<PdfObjectID>();
        foreach (var obj in doc.Internals.GetAllObjects())
        {
            CollectReferences(obj, referenced);
        }
        return referenced;
    }

    static void CollectReferences(PdfItem? item, HashSet<PdfObjectID> sink)
    {
        switch (item)
        {
            case PdfReference reference:
                sink.Add(reference.ObjectID);
                break;
            case PdfDictionary dictionary:
                foreach (var value in dictionary.Elements.Values) CollectReferences(value, sink);
                break;
            case PdfArray array:
                foreach (var value in array.Elements) CollectReferences(value, sink);
                break;
        }
    }

    /// <summary>
    /// Delete an indirect object from the document. Best-effort: a file whose
    /// cross-reference table does not admit the removal must still save, with
    /// the object merely unreferenced, rather than fail the whole clean.
    /// </summary>
    static void TryRemoveObject(PdfDocument doc, PdfDictionary obj)
    {
        try { doc.Internals.RemoveObject(obj); }
        catch { /* left unreferenced; the page no longer points at it */ }
    }

    static HashSet<string> ResolveNamesForHashes(
        PdfResources? resources,
        HashSet<string> targetHashes,
        HashSet<string> hashesRemoved,
        Dictionary<PdfObjectID, PdfDictionary> doomed)
    {
        // Every resource-name on this page whose Image XObject carries one of
        // the selected streams. Hashing each entry is the cost of getting the
        // identity right; it is the same work the verifier does per page.
        //
        // The dictionaries behind those names are collected on the way past, so
        // pruning them afterwards does not mean hashing the whole document a
        // second time. Keyed by object id, so an image drawn on five pages is
        // deleted once rather than five times.
        var result = new HashSet<string>(StringComparer.Ordinal);
        if (targetHashes.Count == 0) return result;

        CollectMatchingNames(
            ImageXObjectCollector.EnumerateImageEntries(resources)
                .Select(e => (e.ResourceName, e.Dictionary)),
            targetHashes, hashesRemoved, doomed, result);
        return result;
    }

    /// <summary>
    /// Match a page's resource entries against the selected stream hashes,
    /// collecting the names to drop and the objects to consider deleting.
    ///
    /// Images and forms are enumerated apart — a form that merely CONTAINS a
    /// selected image must not be dropped as a whole — but they are matched the
    /// same way, and that sameness lives here. Getting object identity wrong on
    /// one side and not the other is what forced a released build to be
    /// withdrawn, so the rule is written once.
    /// </summary>
    static void CollectMatchingNames(
        IEnumerable<(string ResourceName, PdfDictionary Dictionary)> entries,
        HashSet<string> targetHashes,
        HashSet<string> hashesRemoved,
        Dictionary<PdfObjectID, PdfDictionary> doomed,
        HashSet<string> result)
    {
        foreach (var entry in entries)
        {
            var hash = ImageXObjectCollector.ComputeStreamHash(entry.Dictionary);
            if (!targetHashes.Contains(hash)) continue;
            result.Add(entry.ResourceName);
            hashesRemoved.Add(hash);
            doomed[entry.Dictionary.Internals.ObjectID] = entry.Dictionary;
        }
    }

    /// <summary>
    /// The same resolution as <see cref="ResolveNamesForHashes"/>, for the Form
    /// XObjects behind drawings. The enumeration is separate because a form
    /// that merely CONTAINS a selected image must not be dropped by this route
    /// — its image is handled as an image, and the form may draw much else
    /// besides. The matching itself is shared.
    /// </summary>
    static HashSet<string> ResolveFormNamesForHashes(
        PdfResources? resources,
        HashSet<string> targetHashes,
        HashSet<string> hashesRemoved,
        Dictionary<PdfObjectID, PdfDictionary> doomed)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        if (targetHashes.Count == 0) return result;

        CollectMatchingNames(
            ImageXObjectCollector.EnumerateFormEntries(resources)
                .Select(e => (e.ResourceName, e.Dictionary)),
            targetHashes, hashesRemoved, doomed, result);
        return result;
    }

    static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch { /* best-effort cleanup */ }
    }
}
