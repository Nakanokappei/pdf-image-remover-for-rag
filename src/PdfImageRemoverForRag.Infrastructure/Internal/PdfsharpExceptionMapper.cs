using PdfImageRemoverForRag.Core.Errors;
using PdfSharp.Pdf.IO;

namespace PdfImageRemoverForRag.Infrastructure.Internal;

/// <summary>
/// Translates PDFsharp / IO exceptions into the domain
/// <see cref="PdfCleanerException"/> the App knows how to handle. Extracted
/// so every Infrastructure entry-point can wrap its body with a single
/// try/catch — see <c>PdfSharpDocumentAnalyzer</c> for the pattern.
/// </summary>
internal static class PdfsharpExceptionMapper
{
    /// <summary>
    /// Convert a raw exception into a <see cref="PdfCleanerException"/> the
    /// App can present. Preserves the inner exception so telemetry keeps the
    /// original stack trace.
    /// </summary>
    public static PdfCleanerException Map(Exception ex, string context)
    {
        // Order matters: PdfReaderException is a subclass of IOException in
        // PDFsharp 6.x so it must be checked before IOException. Everything
        // that does not match falls into the "unexpected" bucket which the
        // App treats as a bug-report trigger (spec §17).
        return ex switch
        {
            PdfReaderException prex when prex.Message.Contains("password", StringComparison.OrdinalIgnoreCase)
                => new PdfCleanerException(PdfCleanerErrorKind.PdfEncrypted,
                    $"{context}: the PDF is encrypted and needs a password.", ex),

            PdfReaderException prex when prex.Message.Contains("not a PDF", StringComparison.OrdinalIgnoreCase)
                                          || prex.Message.Contains("header", StringComparison.OrdinalIgnoreCase)
                => new PdfCleanerException(PdfCleanerErrorKind.NotAPdf,
                    $"{context}: the file is not a PDF.", ex),

            PdfReaderException prex
                => new PdfCleanerException(PdfCleanerErrorKind.PdfCorrupted,
                    $"{context}: error while reading the PDF: {prex.Message}", ex),

            FileNotFoundException fnf
                => new PdfCleanerException(PdfCleanerErrorKind.PdfCorrupted,
                    $"{context}: file not found: {fnf.FileName}", ex),

            UnauthorizedAccessException uae
                => new PdfCleanerException(PdfCleanerErrorKind.DestinationNotWritable,
                    $"{context}: the destination is not writable: {uae.Message}", ex),

            IOException ioe when ioe.Message.Contains("space", StringComparison.OrdinalIgnoreCase)
                                 || ioe.Message.Contains("disk", StringComparison.OrdinalIgnoreCase)
                => new PdfCleanerException(PdfCleanerErrorKind.DiskFull,
                    $"{context}: not enough disk space.", ex),

            IOException ioe when ioe.Message.Contains("used by another", StringComparison.OrdinalIgnoreCase)
                                 || ioe.Message.Contains("being used", StringComparison.OrdinalIgnoreCase)
                => new PdfCleanerException(PdfCleanerErrorKind.FileInUse,
                    $"{context}: the file is in use by another process.", ex),

            IOException ioe
                => new PdfCleanerException(PdfCleanerErrorKind.DestinationNotWritable,
                    $"{context}: I/O error: {ioe.Message}", ex),

            OperationCanceledException
                => new PdfCleanerException(PdfCleanerErrorKind.UserCanceled,
                    $"{context}: the operation was canceled.", ex),

            _ => new PdfCleanerException(PdfCleanerErrorKind.Unexpected,
                $"{context}: unexpected error: {ex.Message}", ex),
        };
    }
}
