using PdfSharp.Pdf;

namespace PdfImageRemoverForRag.Infrastructure.Internal;

/// <summary>
/// Helpers around <see cref="PdfPage.Contents"/> — page /Contents can be a
/// single stream or an array of streams, and this centralises the merge so
/// analyzer, cleaner, and verifier all handle it the same way.
/// </summary>
internal static class PageContentAccessor
{
    /// <summary>
    /// Concatenate every content stream on the page with a whitespace
    /// separator (required by PDF §7.8.2 when merging).
    /// </summary>
    public static byte[] ReadMergedBytes(PdfPage page)
    {
        using var ms = new MemoryStream();
        bool first = true;
        foreach (var content in page.Contents)
        {
            if (!first) ms.WriteByte((byte)'\n');
            first = false;
            var bytes = content.Stream.UnfilteredValue;
            ms.Write(bytes, 0, bytes.Length);
        }
        return ms.ToArray();
    }
}
