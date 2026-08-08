using System.Globalization;
using PdfImageRemoverForRag.App.Localization;

namespace PdfImageRemoverForRag.App;

/// <summary>
/// The single access point for user-visible strings. UI code reads
/// <c>L10n.Something</c> and never sees which language is active; the
/// language is decided once at startup from the OS display language
/// (<see cref="CultureInfo.CurrentUICulture"/>) and cannot change while the
/// app runs. No UI code may contain a string literal.
///
/// The members below are one-line delegates to <see cref="IStrings"/>. That
/// indirection is what lets a translation be a single self-contained class:
/// the interface makes an untranslated string a build error, and call sites
/// stay unaware of the whole mechanism.
/// </summary>
internal static class L10n
{
    /// <summary>
    /// Every translated language, keyed by the culture name that
    /// <see cref="Resolve"/> matches against. Regional variants resolve
    /// through their parent chain, so "ja-JP" finds "ja", "zh-TW" finds
    /// "zh-Hant" and "pt-BR" finds "pt" without needing their own entries.
    /// </summary>
    static readonly Dictionary<string, IStrings> ByCulture = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ja"] = new JapaneseStrings(),
        ["en"] = new EnglishStrings(),
        // zh-CN resolves through zh-Hans and zh-TW through zh-Hant, so only
        // the bare neutral "zh" needs a decision of its own: it means
        // Simplified far more often than not.
        ["zh-Hans"] = new ChineseSimplifiedStrings(),
        ["zh-Hant"] = new ChineseTraditionalStrings(),
        ["zh"] = new ChineseSimplifiedStrings(),
        ["ko"] = new KoreanStrings(),
        ["de"] = new GermanStrings(),
        ["fr"] = new FrenchStrings(),
        ["es"] = new SpanishStrings(),
        ["it"] = new ItalianStrings(),
        // Brazilian wording, registered under "pt" so European Portuguese
        // reaches it too — closer than falling back to English.
        ["pt"] = new PortugueseStrings(),
        ["ru"] = new RussianStrings(),
        // Indonesian and Malay are close enough to share one translation,
        // written to read naturally in both. Registered twice because they
        // are separate languages, not parent and child.
        ["id"] = new IndonesianMalayStrings(),
        ["ms"] = new IndonesianMalayStrings(),
        ["hi"] = new HindiStrings(),
        ["tr"] = new TurkishStrings(),
        ["vi"] = new VietnameseStrings(),
    };

    static readonly IStrings Fallback = ByCulture["en"];

    /// <summary>The active translation, chosen once at startup.</summary>
    static readonly IStrings S = Resolve(CultureInfo.CurrentUICulture);

    /// <summary>
    /// Walks the culture's parent chain looking for a translation, so any
    /// regional variant lands on its base language. Falls back to English for
    /// languages the app does not translate.
    /// </summary>
    static IStrings Resolve(CultureInfo culture)
    {
        // The chain ends at the invariant culture, whose name is empty.
        for (var c = culture; !string.IsNullOrEmpty(c.Name); c = c.Parent)
        {
            if (ByCulture.TryGetValue(c.Name, out var strings))
            {
                return strings;
            }
        }

        return Fallback;
    }

    /// <summary>The active translation, read by <see cref="ErrorMessageCatalog"/>.</summary>
    internal static IStrings Current => S;

    // --- language-neutral text ---------------------------------------------
    // These read the same in every language, so they are not part of IStrings
    // and never reach a translator.

    /// <summary>
    /// The tick mark in front of the object list's Remove heading. Not
    /// translated — it is a symbol. The panel opposite once carried the same
    /// glyph, to pair the two; it marks its objects with an eye now, and the
    /// word beside this one is what says which is which.
    /// </summary>
    public const string CheckGlyph = "☑";

    /// <summary>
    /// The object list's tick column: what a save REMOVES. The word matters
    /// because the graphics objects panel across the window marks objects too
    /// — with an eye, and it means something else entirely; a bare glyph on
    /// this side left the user to work out which was which.
    /// </summary>
    public static string ColumnDeleteHeader => $"{CheckGlyph} {S.ColumnDelete}";

    /// <summary>The same word without the glyph, for the spoken name.</summary>
    public static string ColumnDelete => S.ColumnDelete;

    /// <summary>
    /// The hamburger, and the only one in the panel: it belongs to a UNIT's own
    /// row, which is the only place with a menu. Not translated — it is a
    /// symbol.
    ///
    /// It sat at the top of the panel for a while, over a menu whose commands
    /// acted on the selection. That menu is gone: what was left in it was one
    /// command, and one command is a button.
    /// </summary>
    public const string RowMenuGlyph = "☰";

    /// <summary>
    /// A unit's heading: which document, which page, which unit on it. The same
    /// in every language, because it is three numbers and three tags — a reader
    /// matching "Doc:01 P.12 Unit 03" against a page needs it to look the same
    /// wherever they are, and there is nothing here to translate.
    /// </summary>
    public static string FlattenUnitLabel(int document, int page, int number) =>
        $"Doc:{document:00} P.{page:00} Unit {number:00}";

    /// <summary>Compression cell for non-image objects (they have none).</summary>
    public static string CompressionNotApplicable => "N/A";

    public static string AboutCopyright => "Copyright © 2026 Nakano Kappei";

    public static string AboutLicenseUrl =>
        "https://github.com/Nakanokappei/pdf-image-remover-for-rag/blob/main/docs/license-notices.md";

    /// <summary>Size cell for a shape: bounding box in points.</summary>
    public static string ShapeSize(int width, int height) => $"{width}×{height} pt";

    /// <summary>Prefix shown when more than one file is being opened.</summary>
    public static string ProgressFileCounter(int index, int count) => $"[{index}/{count}] ";

    /// <summary>
    /// Page label in the usage-locations window ("p.5"). The "p." abbreviation
    /// is conventional across languages, so it stays off IStrings.
    /// </summary>
    public static string UsagePageLabel(int pageNumber) => $"p.{pageNumber}";

    // --- window / menus ----------------------------------------------------

    public static string AppTitle => S.AppTitle;
    public static string MenuFile => S.MenuFile;
    public static string MenuOpen => S.MenuOpen;
    public static string MenuSave => S.MenuSave;
    public static string MenuCloseAll => S.MenuCloseAll;
    public static string MenuExit => S.MenuExit;
    public static string MenuView => S.MenuView;
    public static string MenuTableView => S.MenuTableView;
    public static string MenuTileView => S.MenuTileView;
    public static string MenuShownTypes => S.MenuShownTypes;
    public static string MenuShowImages => S.MenuShowImages;
    public static string MenuShowShapes => S.MenuShowShapes;
    public static string MenuShowDrawings => S.MenuShowDrawings;
    public static string MenuShowShadows => S.MenuShowShadows;
    public static string MenuShowText => S.MenuShowText;
    public static string MenuHelp => S.MenuHelp;
    public static string MenuManual => S.MenuManual;
    public static string MenuAbout => S.MenuAbout;
    public static string ManualUrl => S.ManualUrl;
    public static string LinkOpenFailed => S.LinkOpenFailed;

    // --- toolbar -----------------------------------------------------------

    public static string ToolOpen => S.ToolOpen;
    public static string ToolSave => S.ToolSave;
    public static string ToolSelectAll => S.ToolSelectAll;
    public static string ToolClearSelection => S.ToolClearSelection;

    // --- object list columns -----------------------------------------------

    public static string ColumnThumbnail => S.ColumnThumbnail;
    public static string ColumnObjectId => S.ColumnObjectId;
    public static string ColumnType => S.ColumnType;
    public static string TypeImage => S.TypeImage;
    public static string TypeText => S.TypeText;
    public static string TypeShape => S.TypeShape;
    public static string TypeDrawing => S.TypeDrawing;
    public static string TypeShadow => S.TypeShadow;
    public static string ColumnSize => S.ColumnSize;
    public static string ColumnUsageCount => S.ColumnUsageCount;
    public static string ColumnCompression => S.ColumnCompression;
    public static string ColumnEstimatedSize => S.ColumnEstimatedSize;
    public static string ColumnWarning => S.ColumnWarning;
    public static string TextSize(int characterCount) => S.TextSize(characterCount);
    public static string RowNumber(int number) => S.RowNumber(number);

    // --- status bar / progress ---------------------------------------------

    public static string StatusOpenPrompt => S.StatusOpenPrompt;
    public static string StatusAnalyzing => S.StatusAnalyzing;
    public static string Cancel => S.Cancel;
    public static string StatusCanceling => S.StatusCanceling;
    public static string StatusCanceled => S.StatusCanceled;

    public static string ProgressReadingPages(string fileName, int page, int pageCount) =>
        S.ProgressReadingPages(fileName, page, pageCount);

    public static string ProgressThumbnails(string fileName, int page, int pageCount) =>
        S.ProgressThumbnails(fileName, page, pageCount);

    public static string ProgressGrouping(string fileName) => S.ProgressGrouping(fileName);

    public static string ThumbnailPending => S.ThumbnailPending;

    public static string StatusAnalyzed => S.StatusAnalyzed;
    public static string StatusOpenFailed => S.StatusOpenFailed;
    public static string StatusSaving => S.StatusSaving;
    public static string StatusSaveFailed => S.StatusSaveFailed;

    public static string StatusSaved(int fileCount, int drawCallsRemoved, int regionsFlattened) =>
        S.StatusSaved(fileCount, drawCallsRemoved, regionsFlattened);

    public static string StatusFlattening => S.StatusFlattening;
    public static string StatusFlattened(int places) => S.StatusFlattened(places);
    public static string StatusFlattenUndone => S.StatusFlattenUndone;

    public static string StatusSelection(int selectedCount) => S.StatusSelection(selectedCount);

    // --- warnings ----------------------------------------------------------

    public static string WarningNotRemovable => S.WarningNotRemovable;
    public static string WarningFullPage => S.WarningFullPage;
    public static string TooltipUnsafe => S.TooltipUnsafe;
    public static string TooltipFullPage => S.TooltipFullPage;

    // --- dialogs -----------------------------------------------------------

    public static string OpenDialogTitle => S.OpenDialogTitle;
    public static string PdfFileFilter => S.PdfFileFilter;
    public static string SaveDialogTitle => S.SaveDialogTitle;
    public static string OutputFolderDescription => S.OutputFolderDescription;
    public static string SameAsSourceMessage => S.SameAsSourceMessage;
    public static string SameAsSourceTitle => S.SameAsSourceTitle;
    public static string ConfirmTitle => S.ConfirmTitle;
    public static string ConfirmSaveBeforeOpen => S.ConfirmSaveBeforeOpen;
    public static string ConfirmDiscardBeforeOpen => S.ConfirmDiscardBeforeOpen;
    public static string ErrorDialogTitle => S.ErrorDialogTitle;
    public static string CopyDetails => S.CopyDetails;
    public static string AboutTitle => S.AboutTitle;
    public static string AboutDescription => S.AboutDescription;
    public static string AboutAppLicense => S.AboutAppLicense;
    public static string AboutThirdPartyLicense => S.AboutThirdPartyLicense;
    public static string AboutLicenseLink => S.AboutLicenseLink;
    public static string ContextMenuUsageLocations => S.ContextMenuUsageLocations;

    // --- the graphics objects panel ----------------------------------------

    public static string GraphicsObjectsTitle => S.GraphicsObjectsTitle;
    public static string FlattenVisible => S.FlattenVisible;
    public static string FlattenSelected => S.FlattenSelected;
    public static string FlattenUnitMenu => S.FlattenUnitMenu;
    public static string FlattenUndo => S.FlattenUndo;
    public static string FlattenMerge => S.FlattenMerge;
    public static string FlattenSplit => S.FlattenSplit;
    public static string FlattenDescription => S.FlattenDescription;
    public static string FlattenObjectNotOverlapping => S.FlattenObjectNotOverlapping;
    public static string FlattenNoOverlaps => S.FlattenNoOverlaps;
    public static string FlattenWholePageWarning => S.FlattenWholePageWarning;
    public static string StatusFlattenSelection(int objectCount) => S.StatusFlattenSelection(objectCount);
    public static string AccessibleFlattenPreview => S.AccessibleFlattenPreview;

    // --- workflow messages -------------------------------------------------

    public static string ErrorSameAsSource => S.ErrorSameAsSource;
    public static string ErrorNoSelection => S.ErrorNoSelection;

    public static string VerificationCleanerSummary(int pagesModified, int drawCallsRemoved) =>
        S.VerificationCleanerSummary(pagesModified, drawCallsRemoved);

    public static string VerificationMoreWarnings(int remaining) =>
        S.VerificationMoreWarnings(remaining);

    public static string ErrorVerificationFailedPrefix => S.ErrorVerificationFailedPrefix;
}
