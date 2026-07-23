namespace PdfImageRemoverForRag.Core.Validation;

/// <summary>
/// Decides whether a file is worth handing to a PDF parser at all.
///
/// Until now the only test was the file extension, which is a name and not a
/// fact: anything renamed to .pdf went straight into PDFsharp and PdfPig. Those
/// are large parsers written for well-formed input, and the files reaching them
/// come from wherever the user got them. Checking the header first means a file
/// that is not a PDF is refused by us, with a message that says so, instead of
/// becoming a parser's problem.
///
/// This is a gate, not a validator — a real PDF is still free to be corrupt
/// further in, and the error kinds cover that. It only removes the easy case.
/// </summary>
public static class PdfFileSignature
{
    /// <summary>
    /// Every PDF begins with "%PDF-" followed by its version. The spec allows
    /// leading junk before the header, and readers commonly scan the first
    /// kilobyte for it, so this does the same rather than demanding offset 0.
    /// </summary>
    const int HeaderSearchWindow = 1024;

    /// <summary>
    /// The shortest thing that could conceivably be a PDF: header, one object,
    /// xref and trailer. Anything under this is empty or truncated.
    /// </summary>
    const int MinimumPlausibleLength = 64;

    /// <summary>
    /// True when the file starts with a PDF header and is long enough to hold
    /// a document. Any IO problem answers false — the caller then reports the
    /// file as unreadable, which is what it is.
    /// </summary>
    public static bool LooksLikePdf(string filePath)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            if (stream.Length < MinimumPlausibleLength) return false;

            Span<byte> head = stackalloc byte[HeaderSearchWindow];
            int read = stream.ReadAtLeast(head, HeaderSearchWindow, throwOnEndOfStream: false);
            return head[..read].IndexOf("%PDF-"u8) >= 0;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
