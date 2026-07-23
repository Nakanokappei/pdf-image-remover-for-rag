using PdfImageRemoverForRag.App.Localization;
using PdfImageRemoverForRag.Core.Errors;

namespace PdfImageRemoverForRag.App;

/// <summary>
/// Maps every <see cref="PdfCleanerErrorKind"/> (spec §17) to the text shown
/// in the error dialog. The mapping is language-neutral — the wording lives
/// in <see cref="IStrings"/>, one member per kind — so a new enum value is
/// added here once and then translated everywhere the compiler demands.
/// </summary>
internal static class ErrorMessageCatalog
{
    public static ErrorText Resolve(PdfCleanerErrorKind kind)
    {
        var s = L10n.Current;

        // The default arm covers enum values added without a case here; it
        // reads as "unexpected", which is exactly what such a value is.
        return kind switch
        {
            PdfCleanerErrorKind.NotAPdf => s.NotAPdf,
            PdfCleanerErrorKind.PdfCorrupted => s.PdfCorrupted,
            PdfCleanerErrorKind.PdfEncrypted => s.PdfEncrypted,
            PdfCleanerErrorKind.PdfPasswordRequired => s.PdfPasswordRequired,
            PdfCleanerErrorKind.UnsupportedEncryption => s.UnsupportedEncryption,
            PdfCleanerErrorKind.ImageExtractionFailed => s.ImageExtractionFailed,
            PdfCleanerErrorKind.ImageRemovalUnsafe => s.ImageRemovalUnsafe,
            PdfCleanerErrorKind.DestinationNotWritable => s.DestinationNotWritable,
            PdfCleanerErrorKind.FileInUse => s.FileInUse,
            PdfCleanerErrorKind.DiskFull => s.DiskFull,
            PdfCleanerErrorKind.PostSaveVerificationFailed => s.PostSaveVerificationFailed,
            PdfCleanerErrorKind.UserCancelled => s.UserCancelled,
            PdfCleanerErrorKind.Unexpected => s.Unexpected,
            _ => s.Unexpected,
        };
    }
}
