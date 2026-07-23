namespace PdfImageRemoverForRag.Core.Errors;

/// <summary>
/// The 13 error categories the UI must be able to distinguish (spec §17).
/// Each entry maps 1:1 to a translated message and a suggested action in
/// the App layer. Keep this enum synced with the App resource file — never
/// add a new value without adding the localized text at the same time.
/// </summary>
public enum PdfCleanerErrorKind
{
    /// <summary>File exists but is not a PDF (missing %PDF- header, wrong extension).</summary>
    NotAPdf,

    /// <summary>The file is a PDF but internally corrupted and cannot be parsed.</summary>
    PdfCorrupted,

    /// <summary>PDF is encrypted; no password was provided.</summary>
    PdfEncrypted,

    /// <summary>PDF is encrypted; the supplied password is wrong.</summary>
    PdfPasswordRequired,

    /// <summary>PDF uses an encryption scheme the library cannot open.</summary>
    UnsupportedEncryption,

    /// <summary>Image XObject could not be enumerated / decoded.</summary>
    ImageExtractionFailed,

    /// <summary>Target images cannot be removed safely (shared Form XObject etc.).</summary>
    ImageRemovalUnsafe,

    /// <summary>Destination path is not writable.</summary>
    DestinationNotWritable,

    /// <summary>Destination file is locked by another process.</summary>
    FileInUse,

    /// <summary>Disk full or quota exceeded during save.</summary>
    DiskFull,

    /// <summary>Cleaned PDF could not be reopened after saving.</summary>
    PostSaveVerificationFailed,

    /// <summary>User cancelled the operation.</summary>
    UserCancelled,

    /// <summary>Anything else — a wrapped exception the UI should treat as a bug report.</summary>
    Unexpected,
}
