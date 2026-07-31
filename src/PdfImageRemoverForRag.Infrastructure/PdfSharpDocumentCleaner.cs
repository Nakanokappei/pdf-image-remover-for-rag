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

    readonly IPageRasterizer? _rasterizer;

    /// <param name="rasterizer">
    /// Renders the regions a caller asks to flatten. Optional because plain
    /// removal needs no renderer, and because the only real implementation is
    /// Windows-only (see <see cref="IPageRasterizer"/>) while this assembly has
    /// to keep building and testing on macOS.
    /// </param>
    public PdfSharpDocumentCleaner(IPageRasterizer? rasterizer = null)
    {
        _rasterizer = rasterizer;
    }

    public async Task<CleaningResult> CleanAsync(
        string sourcePath,
        string destinationPath,
        IReadOnlyList<ImageRemovalSelection> selections,
        IReadOnlyList<OverlapRegion>? regionsToFlatten = null,
        CancellationToken ct = default)
    {
        var regions = regionsToFlatten ?? Array.Empty<OverlapRegion>();

        // Hard rule from the spec: never overwrite the source, even if the
        // App accidentally supplies the same path.
        if (CleanedFileNamer.WouldOverwriteSource(sourcePath, destinationPath))
        {
            throw new PdfCleanerException(PdfCleanerErrorKind.DestinationNotWritable,
                "元 PDF と同じパスへの保存はできません。別名を指定してください。");
        }
        if (selections.Count == 0 && regions.Count == 0)
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

        return await Task
            .Run(() => CleanSync(sourcePath, destinationPath, selections, flattenImages, ct), ct)
            .ConfigureAwait(false);
    }

    /// <summary>One region and the pixels that will replace it.</summary>
    sealed record FlattenImage(OverlapRegion Region, byte[] Png);

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
            var png = await _rasterizer!
                .RenderRegionAsync(sourcePath, region.PageNumber, PageRegion.Of(region), FlattenDpi, ct)
                .ConfigureAwait(false);
            if (png is null || png.Length == 0) continue;
            images.Add(new FlattenImage(region, png));
        }
        return images;
    }

    static CleaningResult CleanSync(string sourcePath, string destinationPath,
        IReadOnlyList<ImageRemovalSelection> selections,
        IReadOnlyList<FlattenImage> flattenImages, CancellationToken ct)
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
        var selectedImageHashes = new HashSet<string>(
            selections
                .Where(s => s.Kind == RemovableKind.Image && s.Hash is not null)
                .Select(s => s.Hash!),
            StringComparer.Ordinal);

        // Text selections are matched by their shown string, not an object id.
        var selectedTextValues = new HashSet<string>(
            selections
                .Where(s => s.Kind == RemovableKind.Text && s.TextValue is not null)
                .Select(s => s.TextValue!),
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
                foreach (var flatten in pageFlatten)
                {
                    int removedForRegion = RemoveRegionMembers(page, sequence, flatten.Region);
                    // Nothing matched: the objects are no longer where analysis
                    // saw them, so there is nothing to replace and drawing the
                    // rendering would just lay a second copy over the original.
                    if (removedForRegion == 0) continue;
                    removed += removedForRegion;
                    flattenedHere.Add(flatten);
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

                page.Contents.ReplaceContent(sequence);
                // Only now may the replacements be drawn: ReplaceContent
                // collapses the page into a single content stream, so anything
                // appended before it would be thrown away.
                if (flattenedHere.Count > 0)
                {
                    DrawFlattenedRegions(page, flattenedHere, placedImages);
                    regionsFlattened += flattenedHere.Count;
                }
                pagesModified++;
                totalRemovedOps += removed;
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
                ImagesKeptForOtherReferences: imagesKeptBack);
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
    static int RemoveRegionMembers(PdfPage page, CSequence sequence, OverlapRegion region)
    {
        // The region names its image members by stream hash, while the page
        // draws them through resource names, so the same resolution plain
        // removal does is needed here. What is deliberately NOT done is
        // recording those hashes as removed, or those objects as doomed: the
        // image bytes are still in the document — inside the rendering, and
        // usually still drawn elsewhere — so both collectors are given
        // throwaways rather than the real ones. Deleting a flattened image
        // would tear it out of every other page that draws it.
        var imageHashes = new HashSet<string>(
            region.Members.Where(m => m.Kind == RemovableKind.Image).Select(m => m.Identity),
            StringComparer.Ordinal);
        var namesInRegion = ResolveNamesForHashes(
            page.Resources, imageHashes,
            new HashSet<string>(StringComparer.Ordinal),
            new Dictionary<PdfObjectID, PdfDictionary>());

        return ContentStreamWalker.RemoveInRegion(
            sequence, region, namesInRegion,
            new PdfTextDecoder(page.Resources), new PdfFontMetrics(page.Resources));
    }

    /// <summary>
    /// Draw each region's rendering where its objects used to be. The images are
    /// added to <paramref name="keepAlive"/> rather than disposed here because
    /// PDFsharp reads their streams when the document is saved.
    /// </summary>
    static void DrawFlattenedRegions(
        PdfPage page, List<FlattenImage> flattened, List<IDisposable> keepAlive)
    {
        // Appending gives a content stream of our own, on top of what is left of
        // the page. XGraphics measures from the top-left with y growing down,
        // while a region is in PDF space with the origin at the bottom-left, so
        // the top edge is the page height less the region's upper edge. (Pages
        // with a /Rotate entry are not handled specially — analysis reads
        // content-stream coordinates too, so both sides are consistent.)
        using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append, XGraphicsUnit.Point);
        foreach (var flatten in flattened)
        {
            var region = flatten.Region;
            var image = XImage.FromStream(new MemoryStream(flatten.Png));
            keepAlive.Add(image);
            double top = page.Height.Point - (region.Y + region.Height);
            gfx.DrawImage(image, new XRect(region.X, top, region.Width, region.Height));
        }
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
    /// or both. A <c>/Mask</c> may instead be an array of colour-key ranges,
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

        foreach (var entry in ImageXObjectCollector.EnumerateImageEntries(resources))
        {
            var hash = ImageXObjectCollector.ComputeStreamHash(entry.Dictionary);
            if (!targetHashes.Contains(hash)) continue;
            result.Add(entry.ResourceName);
            hashesRemoved.Add(hash);
            doomed[entry.Dictionary.Internals.ObjectID] = entry.Dictionary;
        }
        return result;
    }

    static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch { /* best-effort cleanup */ }
    }
}
