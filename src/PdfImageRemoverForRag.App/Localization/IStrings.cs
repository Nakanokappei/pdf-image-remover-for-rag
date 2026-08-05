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
    /// <summary>
    /// Submenu that hides and shows whole KINDS of object — images, shapes,
    /// text. It filters rows, never columns; the Japanese caption said 表示列
    /// ("shown columns") for a long time and described the wrong axis.
    /// </summary>
    string MenuShownTypes { get; }

    string MenuShowImages { get; }
    string MenuShowShapes { get; }
    string MenuShowDrawings { get; }
    string MenuShowShadows { get; }
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
    string ColumnObjectId { get; }
    string ColumnType { get; }
    string TypeImage { get; }
    string TypeText { get; }
    string TypeShape { get; }

    /// <summary>
    /// The artwork a Form XObject paints, listed as one object however many
    /// paths it holds. Distinct from <see cref="TypeShape"/>, which is a single
    /// path on the page — the two sit next to each other in the type column, so
    /// a translation has to keep them tellable apart.
    /// </summary>
    string TypeDrawing { get; }

    /// <summary>
    /// A shadow layer: the flat-coloured picture a drop shadow becomes when it
    /// is exported to PDF. Translate it as the drawing effect a reader knows
    /// from a word processor or presentation tool ("drop shadow"), not as the
    /// dark area an object casts in a photograph.
    /// </summary>
    string TypeShadow { get; }
    string ColumnSize { get; }
    string ColumnUsageCount { get; }
    string ColumnCompression { get; }
    string ColumnEstimatedSize { get; }
    string ColumnWarning { get; }

    /// <summary>
    /// The delete column's header, and what a screen reader calls it. Use the
    /// same remove/delete verb the language uses elsewhere in the app: the
    /// column's ticks are what a save removes, and the panel on the other side
    /// of the window ticks things to flatten, so each side has to say which it
    /// is. The ☑ glyph in front of it is added in code, not translated.
    /// </summary>
    string ColumnDelete { get; }

    /// <summary>Size cell for a text object: character count.</summary>
    string TextSize(int characterCount);

    /// <summary>
    /// What a screen reader calls one row of the object list. The number is the
    /// one printed in the row header, counting from 1 — WinForms names a row
    /// from its zero-based index, so the spoken number was one behind the
    /// visible one.
    /// </summary>
    string RowNumber(int number);

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
    /// <summary>
    /// What one save run did. Both counts are always shown, including a zero:
    /// one run can delete, flatten, or both, and "0 removed" is what tells a
    /// user who only flattened that nothing was thrown away.
    /// </summary>
    string StatusSaved(int fileCount, int drawCallsRemoved, int regionsFlattened);

    /// <summary>
    /// How many rows of the object list are ticked for removal. Say "objects",
    /// not "images" — the list holds images, repeated text and shapes alike.
    /// </summary>
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

    /// <summary>
    /// Right-click menu item on a row/tile, and the title of the window it
    /// opens, which shows every file + page where the object is used with a
    /// full-page thumbnail. Carries an access key like the menu captions.
    /// </summary>
    string ContextMenuUsageLocations { get; }

    // --- the flatten panel -------------------------------------------------
    // Flattening and deleting are opposite operations — one keeps the page's
    // appearance and drops the text layer, the other drops the appearance — but
    // they act on the same objects, so they sit side by side: the object list
    // fills the window and the flatten tree docks to its right. NO access key
    // in the title: it labels a panel, not a menu, and "&" would show literally.

    /// <summary>
    /// Title over the panel where overlapping objects are baked into one image.
    /// Use the term the language's image editors use for Photoshop's "Flatten
    /// Image", if it has one — that is the operation, and users have met it
    /// there.
    /// </summary>
    string FlattenPanelTitle { get; }

    /// <summary>
    /// Buttons that correct a unit the detector got wrong: merge gathers the
    /// ticked objects into one unit, split takes them out of the one they are
    /// in. They act on what is ticked, not on whole units, so word them that
    /// way — and keep them short, since three buttons share the panel's title
    /// bar and the panel can be dragged narrow.
    /// </summary>
    string FlattenMerge { get; }
    string FlattenSplit { get; }

    /// <summary>
    /// One line under the title, explaining what the panel is for. It has to
    /// answer "why would I want this": the page looks unchanged afterwards, and
    /// what changes is invisible — the text stops being text.
    /// </summary>
    string FlattenDescription { get; }

    /// <summary>
    /// A place where objects of different kinds overlap, numbered within its
    /// page: one such place is baked into one image. Keep it short — it is a
    /// tree node label, followed by the kinds it contains in parentheses.
    /// </summary>
    /// <summary>
    /// A unit's heading, numbered document-page-unit ("Unit 1-12-3"). Units are
    /// counted within a page and pages within a document, so the bare count
    /// repeats itself several times over in a panel that shows more than one
    /// page — which it does whenever an object appears more than once. Keep the
    /// three numbers joined by hyphens; only the word is translated.
    /// </summary>
    string FlattenUnitLabel(int document, int page, int number);

    /// <summary>
    /// Shown in the panel when the object selected in the list overlaps nothing
    /// anywhere, so there is no unit to list. States the fact — it is not an
    /// error, and most objects in a document are like this.
    /// </summary>
    string FlattenObjectNotOverlapping { get; }

    /// <summary>
    /// Shown under the description when files are open but nothing in them can
    /// be flattened, so the tree is a plain listing. Answers the question the
    /// user is about to ask: why is there nothing to tick?
    /// </summary>
    string FlattenNoOverlaps { get; }

    /// <summary>
    /// Shown when what the user has ticked would cover essentially the whole
    /// page. Flattening that leaves none of the page's text as text, which is a
    /// reasonable thing to want and a ruinous thing to do by accident — the
    /// preview outline spanning the paper was the only hint before this.
    /// </summary>
    string FlattenWholePageWarning { get; }

    /// <summary>
    /// Status bar while objects are checked for flattening. Counts objects, not
    /// units, because that is what the user ticks.
    /// </summary>
    string StatusFlattenSelection(int objectCount);

    /// <summary>
    /// Screen-reader name for the preview pane beside the tree. It is drawn, so
    /// it has no text of its own for UI Automation to read.
    /// </summary>
    string AccessibleFlattenPreview { get; }

    // --- messages raised by the workflow (spec §17) ------------------------

    string ErrorSameAsSource { get; }

    /// <summary>
    /// Raised when a save run is asked for with neither side ticked. It has to
    /// name both sides: the run deletes AND flattens, so "no images selected"
    /// would send a user who had ticked a flatten unit looking in the wrong
    /// half of the window.
    /// </summary>
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
