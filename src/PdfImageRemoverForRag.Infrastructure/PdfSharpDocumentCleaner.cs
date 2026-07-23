using System.Diagnostics;
using PdfImageRemoverForRag.Core.Abstractions;
using PdfImageRemoverForRag.Core.Errors;
using PdfImageRemoverForRag.Core.Validation;
using PdfImageRemoverForRag.Core.Formatting;
using PdfImageRemoverForRag.Core.Models;
using PdfImageRemoverForRag.Infrastructure.Internal;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.Content;
using PdfSharp.Pdf.IO;

namespace PdfImageRemoverForRag.Infrastructure;

/// <summary>
/// PDFsharp-backed implementation of <see cref="IPdfDocumentCleaner"/>.
/// Given a list of removal selections, rewrites each affected page's
/// content stream to drop the target <c>Do</c> operators, then saves the
/// document through a temp file so a partial write never corrupts the
/// destination (spec §15).
/// </summary>
public sealed class PdfSharpDocumentCleaner : IPdfDocumentCleaner
{
    public async Task<CleaningResult> CleanAsync(
        string sourcePath,
        string destinationPath,
        IReadOnlyList<ImageRemovalSelection> selections,
        CancellationToken ct = default)
    {
        // Hard rule from the spec: never overwrite the source, even if the
        // App accidentally supplies the same path.
        if (CleanedFileNamer.WouldOverwriteSource(sourcePath, destinationPath))
        {
            throw new PdfCleanerException(PdfCleanerErrorKind.DestinationNotWritable,
                "元 PDF と同じパスへの保存はできません。別名を指定してください。");
        }
        if (selections.Count == 0)
        {
            throw new PdfCleanerException(PdfCleanerErrorKind.Unexpected,
                "削除対象の画像が指定されていません。");
        }
        // Re-checked rather than trusted from the open: the file has been
        // sitting on disk since it was analyzed and may have been replaced.
        if (!PdfFileSignature.LooksLikePdf(sourcePath))
        {
            throw new PdfCleanerException(PdfCleanerErrorKind.NotAPdf,
                "選択されたファイルは PDF ではありません。");
        }

        return await Task.Run(() => CleanSync(sourcePath, destinationPath, selections, ct), ct)
            .ConfigureAwait(false);
    }

    static CleaningResult CleanSync(string sourcePath, string destinationPath,
        IReadOnlyList<ImageRemovalSelection> selections, CancellationToken ct)
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
        var removedHashes = new HashSet<string>(StringComparer.Ordinal);

        try
        {
            using var doc = PdfReader.Open(sourcePath, PdfDocumentOpenMode.Modify);

            for (int i = 0; i < doc.PageCount; i++)
            {
                ct.ThrowIfCancellationRequested();
                var page = doc.Pages[i];
                var namesToDrop = ResolveNamesForHashes(page.Resources, selectedImageHashes, removedHashes);
                if (namesToDrop.Count == 0
                    && selectedTextValues.Count == 0
                    && selectedShapeSignatures.Count == 0) continue;

                var contentBytes = PageContentAccessor.ReadMergedBytes(page);
                var sequence = ContentReader.ReadContent(contentBytes);
                int removed = 0;
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
                pagesModified++;
                totalRemovedOps += removed;
            }

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
                Elapsed: sw.Elapsed);
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
    }

    static HashSet<string> ResolveNamesForHashes(
        PdfResources? resources,
        HashSet<string> targetHashes,
        HashSet<string> hashesRemoved)
    {
        // Every resource-name on this page whose Image XObject carries one of
        // the selected streams. Hashing each entry is the cost of getting the
        // identity right; it is the same work the verifier does per page.
        var result = new HashSet<string>(StringComparer.Ordinal);
        if (targetHashes.Count == 0) return result;

        foreach (var entry in ImageXObjectCollector.EnumerateImageEntries(resources))
        {
            var hash = ImageXObjectCollector.ComputeStreamHash(entry.Dictionary);
            if (!targetHashes.Contains(hash)) continue;
            result.Add(entry.ResourceName);
            hashesRemoved.Add(hash);
        }
        return result;
    }

    static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch { /* best-effort cleanup */ }
    }
}
