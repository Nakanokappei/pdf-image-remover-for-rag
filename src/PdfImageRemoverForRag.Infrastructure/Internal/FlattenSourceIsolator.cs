using PdfImageRemoverForRag.Core.Grouping;
using PdfImageRemoverForRag.Core.Models;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Content;
using PdfSharp.Pdf.IO;

namespace PdfImageRemoverForRag.Infrastructure.Internal;

/// <summary>
/// A one-page copy of the document holding, inside the region, only the objects
/// the user ticked.
///
/// Flattening renders a rectangle of the page. Rendering it from the document
/// as it stands photographs whatever else reaches into that rectangle — a
/// neighbouring image the user chose to KEEP ends up in the picture, cropped at
/// its edge, while the original goes on being drawn. On the page the two sit
/// exactly on top of each other and nobody notices; a reader that pulls images
/// out of the file gets the same picture twice. A customer reported precisely
/// that.
///
/// So the picture is rendered from this copy instead: everything else that
/// reaches into the rectangle is taken out first, and the render is asked for a
/// transparent background, which leaves the kept neighbours showing through
/// from the page underneath.
///
/// One page rather than the whole document because it is written to disk for
/// the renderer to read, once per region.
/// </summary>
internal static class FlattenSourceIsolator
{
    /// <summary>
    /// Write the isolated page and return its path, or null when the page
    /// cannot be copied. The caller deletes the file once the render is done.
    /// </summary>
    public static string? Write(string sourcePath, OverlapRegion region, string destinationPath)
    {
        try
        {
            using var source = PdfReader.Open(sourcePath, PdfDocumentOpenMode.Import);
            if (region.PageNumber < 1 || region.PageNumber > source.PageCount) return null;

            using var isolated = new PdfDocument();
            // AddPage copies the page into the new document, resources and all,
            // so the content can be rewritten without touching the source.
            var page = isolated.AddPage(source.Pages[region.PageNumber - 1]);

            var sequence = ContentReader.ReadContent(PageContentAccessor.ReadMergedBytes(page));
            var memberNames = MemberImageNames(page, region);

            ContentStreamWalker.RemoveInRegion(
                sequence, region, memberNames,
                new PdfTextDecoder(page.Resources), new PdfFontMetrics(page.Resources),
                ContentStreamWalker.RegionSide.Others);

            page.Contents.ReplaceContent(sequence);
            isolated.Save(destinationPath);
            return destinationPath;
        }
        catch (Exception)
        {
            // Rendering from the original is the old behaviour, not a failure:
            // the picture then holds a neighbour's edge, which is what this
            // exists to avoid but is far better than no picture at all.
            return null;
        }
    }

    /// <summary>
    /// The resource names on this page that name a member image. The members
    /// carry stream hashes, which is the identity everything else uses too, and
    /// the copied page has resource names of its own.
    /// </summary>
    static IReadOnlySet<string> MemberImageNames(PdfPage page, OverlapRegion region)
    {
        var hashes = region.Members
            .Where(m => m.Kind.IsImageXObject())
            .Select(m => m.Identity)
            .ToHashSet(StringComparer.Ordinal);

        var names = new HashSet<string>(StringComparer.Ordinal);
        if (hashes.Count == 0) return names;

        foreach (var entry in ImageXObjectCollector.EnumerateImageEntries(page.Resources))
        {
            if (hashes.Contains(ImageXObjectCollector.ComputeStreamHash(entry.Dictionary)))
                names.Add(entry.ResourceName);
        }
        return names;
    }
}
