using PdfImageRemoverForRag.Core.Abstractions;
using PdfImageRemoverForRag.Core.Models;
using PdfImageRemoverForRag.Infrastructure.Internal;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.Content;
using PdfSharp.Pdf.IO;

namespace PdfImageRemoverForRag.Infrastructure;

/// <summary>
/// PDFsharp-backed implementation of <see cref="IPdfDocumentVerifier"/>.
/// Runs the six-point simple verification from spec §16 against the just-
/// saved PDF. Any exception is captured as
/// <see cref="VerificationReport.NoRuntimeExceptions"/> = false so the App
/// can decide whether to abort the save (spec §15 step 5–6).
/// </summary>
public sealed class PdfSharpDocumentVerifier : IPdfDocumentVerifier
{
    public async Task<VerificationReport> VerifyAsync(
        string originalPath,
        string cleanedPath,
        IReadOnlyList<string> removedGroupHashes,
        IReadOnlyList<string> retainedGroupHashes,
        CancellationToken ct = default)
    {
        return await Task.Run(
            () => VerifySync(originalPath, cleanedPath, removedGroupHashes, retainedGroupHashes, ct),
            ct).ConfigureAwait(false);
    }

    static VerificationReport VerifySync(string originalPath, string cleanedPath,
        IReadOnlyList<string> removedGroupHashes, IReadOnlyList<string> retainedGroupHashes,
        CancellationToken ct)
    {
        var warnings = new List<string>();
        bool cleanedOpens = false;
        bool pageCountOk = false;
        bool nonEmpty = false;
        bool noDoForRemoved = true;
        bool retainedPresent = true;
        bool noExceptions = true;

        try
        {
            // Non-empty size — cheap check first so we fail fast on truncation.
            var cleanedSize = new FileInfo(cleanedPath).Length;
            nonEmpty = cleanedSize > 0;
            if (!nonEmpty) warnings.Add("cleaned PDF file size is zero");

            // Both PDFs open and page counts agree.
            int origPageCount, cleanedPageCount;
            using (var orig = PdfReader.Open(originalPath, PdfDocumentOpenMode.Import))
            using (var cleaned = PdfReader.Open(cleanedPath, PdfDocumentOpenMode.Import))
            {
                cleanedOpens = true;
                origPageCount = orig.PageCount;
                cleanedPageCount = cleaned.PageCount;
            }
            pageCountOk = origPageCount == cleanedPageCount;
            if (!pageCountOk) warnings.Add($"page count differs: {origPageCount} vs {cleanedPageCount}");
            ct.ThrowIfCancellationRequested();

            // Removed groups: no page may still contain a Do operator for a
            // resource name that resolves to one of the removed hashes.
            var removedHashSet = new HashSet<string>(removedGroupHashes, StringComparer.Ordinal);
            var retainedHashSet = new HashSet<string>(retainedGroupHashes, StringComparer.Ordinal);
            bool retainedFoundAny = retainedHashSet.Count == 0;

            using (var cleaned = PdfReader.Open(cleanedPath, PdfDocumentOpenMode.Import))
            {
                for (int i = 0; i < cleaned.PageCount; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var page = cleaned.Pages[i];
                    var (removedNames, retainedNames) = CategoriseNames(
                        page.Resources, removedHashSet, retainedHashSet);

                    if (retainedNames.Count > 0) retainedFoundAny = true;

                    if (removedNames.Count == 0) continue;
                    var content = PageContentAccessor.ReadMergedBytes(page);
                    var sequence = ContentReader.ReadContent(content);
                    foreach (var name in removedNames)
                    {
                        if (ContentStreamWalker.ContainsDoFor(sequence, name))
                        {
                            noDoForRemoved = false;
                            warnings.Add($"page {i + 1} still draws {name}");
                        }
                    }
                }
            }

            retainedPresent = retainedFoundAny;
            if (!retainedPresent)
                warnings.Add("no retained image groups were located in the cleaned PDF");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            noExceptions = false;
            warnings.Add($"verification threw {ex.GetType().Name}: {ex.Message}");
        }

        return new VerificationReport(
            CleanedPdfOpens: cleanedOpens,
            PageCountMatches: pageCountOk,
            NonEmptyFileSize: nonEmpty,
            NoDoOperatorsForRemovedImages: noDoForRemoved,
            NonRemovedImageGroupsRetained: retainedPresent,
            NoRuntimeExceptions: noExceptions,
            Warnings: warnings);
    }

    static (HashSet<string> Removed, HashSet<string> Retained) CategoriseNames(
        PdfResources? resources, HashSet<string> removed, HashSet<string> retained)
    {
        // Walk /XObject once per page, classifying each Image entry against
        // the removed/retained hash sets so the caller can search the content
        // stream in a single pass.
        var removedNames = new HashSet<string>(StringComparer.Ordinal);
        var retainedNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in ImageXObjectCollector.EnumerateImageEntries(resources))
        {
            var hash = ImageXObjectCollector.ComputeStreamHash(entry.Dictionary);
            if (removed.Contains(hash)) removedNames.Add(entry.ResourceName);
            else if (retained.Contains(hash)) retainedNames.Add(entry.ResourceName);
        }
        return (removedNames, retainedNames);
    }
}
