namespace PdfImageRemoverForRag.App.Localization;

/// <summary>
/// Description + suggested remedy for one error kind, as shown in the error
/// dialog. Kept as a pair because the dialog always renders both together.
/// </summary>
internal readonly record struct ErrorText(string Description, string Remedy);

/// <summary>
/// Every user-visible string in the app, in one language. One class per
/// language implements this; <see cref="L10n"/> picks the implementation once
/// at startup from the OS display language.
///
/// The interface is the contract that keeps translations complete: adding a
/// member here fails the build for every language that has not translated it.
/// That matters because the App layer has no automated tests — the compiler is
/// the only guard.
///
/// Strings that read the same in every language (a copyright line, "N/A", a
/// "12×34 pt" measurement) deliberately do NOT live here; they stay as
/// constants on <see cref="L10n"/> so translators never see them.
/// </summary>
internal interface IStrings
{
    // --- window / menus ----------------------------------------------------

    /// <summary>
    /// Marketing name shown in the title bar. Translate it only where the
    /// product is actually marketed under a translated name — otherwise keep
    /// the English name, which is how the Microsoft Store lists it.
    /// </summary>
    string AppTitle { get; }

    // Menu captions carry their access key as "&X". Japanese, Chinese and
    // Korean convention appends it in parentheses after the caption
    // ("ファイル(&F)"); Western languages mark a letter inside the word
    // ("&File"). Each language writes whichever its platform convention uses.
    string MenuFile { get; }
    string MenuOpen { get; }
    string MenuSave { get; }
    string MenuCloseAll { get; }
    string MenuExit { get; }
    string MenuView { get; }
    string MenuTableView { get; }
    string MenuTileView { get; }
    string MenuShownTypes { get; }
    string MenuShowImages { get; }
    string MenuShowShapes { get; }
    string MenuShowText { get; }
    string MenuHelp { get; }
    string MenuManual { get; }
    string MenuAbout { get; }

    /// <summary>
    /// The manual is hosted in the GitHub repository as Markdown. Only
    /// Japanese and English pages exist, so every other language points at the
    /// English one.
    /// </summary>
    string ManualUrl { get; }

    string LinkOpenFailed { get; }

    // --- toolbar -----------------------------------------------------------

    string ToolOpen { get; }
    string ToolSave { get; }
    string ToolSelectAll { get; }
    string ToolClearSelection { get; }

    // --- object list columns (spec §11.3) ----------------------------------

    string ColumnThumbnail { get; }
    string ColumnImageId { get; }
    string ColumnType { get; }
    string TypeImage { get; }
    string TypeText { get; }
    string TypeShape { get; }
    string ColumnSize { get; }
    string ColumnUsageCount { get; }
    string ColumnCompression { get; }
    string ColumnEstimatedSize { get; }
    string ColumnWarning { get; }

    /// <summary>
    /// Screen-reader name for the delete column, whose visible header is only a
    /// ☑ glyph (which a screen reader would read as "ballot box"). Use the same
    /// remove/delete verb the language uses elsewhere in the app.
    /// </summary>
    string AccessibleDeleteColumn { get; }

    /// <summary>Size cell for a text object: character count.</summary>
    string TextSize(int characterCount);

    // --- status bar --------------------------------------------------------

    string StatusOpenPrompt { get; }
    string StatusAnalyzing { get; }

    // --- analysis progress -------------------------------------------------
    // A 30 MB PDF can take minutes; without a running count the user cannot
    // tell a slow file from a hung app.

    string Cancel { get; }
    string StatusCancelling { get; }
    string StatusCancelled { get; }

    /// <summary>Reading pages, e.g. "report.pdf — analyzing page 12 of 48".</summary>
    string ProgressReadingPages(string fileName, int page, int pageCount);

    /// <summary>Thumbnail decoding, counted in pages for the same reason.</summary>
    string ProgressThumbnails(string fileName, int page, int pageCount);

    string ProgressGrouping(string fileName);

    /// <summary>
    /// Drawn inside a tile whose bitmap is not built yet. Without it the tile
    /// is an empty frame, which reads as a broken image rather than as work in
    /// progress. Keep it short — it wraps inside a 236x188 tile.
    /// Use the same word for "thumbnail" as <see cref="ColumnThumbnail"/>.
    /// </summary>
    string ThumbnailPending { get; }

    string StatusAnalyzed { get; }
    string StatusOpenFailed { get; }
    string StatusSaving { get; }
    string StatusSaveFailed { get; }
    string StatusSaved(int fileCount, int drawCallsRemoved);
    string StatusSelection(int selectedCount);

    // --- warnings (spec §7 / §14.3) ----------------------------------------

    string WarningNotRemovable { get; }

    /// <summary>
    /// Spelled out rather than abbreviated: this is the one warning where
    /// acting on it destroys a page, so it must read as a sentence.
    /// </summary>
    string WarningFullPage { get; }

    string TooltipUnsafe { get; }
    string TooltipFullPage { get; }

    // --- dialogs -----------------------------------------------------------

    string OpenDialogTitle { get; }

    /// <summary>
    /// Windows file-dialog filter. Keep the "label|pattern" structure and the
    /// "*.pdf" pattern exactly — only the label is translated.
    /// </summary>
    string PdfFileFilter { get; }

    string SaveDialogTitle { get; }
    string OutputFolderDescription { get; }
    string SameAsSourceMessage { get; }
    string SameAsSourceTitle { get; }
    string ConfirmTitle { get; }
    string ConfirmSaveBeforeOpen { get; }
    string ConfirmDiscardBeforeOpen { get; }
    string ErrorDialogTitle { get; }
    string CopyDetails { get; }
    string AboutTitle { get; }

    /// <summary>
    /// One paragraph, not a manual. What it does, plus the two reassurances
    /// that matter most (originals untouched, nothing leaves the PC).
    /// </summary>
    string AboutDescription { get; }

    string AboutAppLicense { get; }
    string AboutThirdPartyLicense { get; }
    string AboutLicenseLink { get; }

    // --- messages raised by the workflow (spec §17) ------------------------

    string ErrorSameAsSource { get; }
    string ErrorNoSelection { get; }

    /// <summary>
    /// What the cleaner reported, shown before the verifier's complaints: it
    /// distinguishes "nothing matched" from "matched but did not stick".
    /// </summary>
    string VerificationCleanerSummary(int pagesModified, int drawCallsRemoved);

    string VerificationMoreWarnings(int remaining);
    string ErrorVerificationFailedPrefix { get; }

    // --- error catalog (spec §17) ------------------------------------------
    // One member per PdfCleanerErrorKind. They are properties rather than a
    // switch so that a language missing one is a compile error, the same as
    // every other string here. ErrorMessageCatalog owns the enum-to-member
    // mapping, which is language-neutral and therefore written once.

    ErrorText NotAPdf { get; }
    ErrorText PdfCorrupted { get; }
    ErrorText PdfEncrypted { get; }
    ErrorText PdfPasswordRequired { get; }
    ErrorText UnsupportedEncryption { get; }
    ErrorText ImageExtractionFailed { get; }
    ErrorText ImageRemovalUnsafe { get; }
    ErrorText DestinationNotWritable { get; }
    ErrorText FileInUse { get; }
    ErrorText DiskFull { get; }
    ErrorText PostSaveVerificationFailed { get; }
    ErrorText UserCancelled { get; }
    ErrorText Unexpected { get; }
}
