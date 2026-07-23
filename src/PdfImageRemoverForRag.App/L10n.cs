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
    /// The delete column shows only a checkbox, so its header is a compact
    /// check glyph rather than a word that never fits the narrow column.
    /// </summary>
    public static string ColumnDelete => "☑";

    /// <summary>Compression cell for non-image objects (they have none).</summary>
    public static string CompressionNotApplicable => "N/A";

    public static string AboutCopyright => "Copyright © 2026 Nakano Kappei";

    public static string AboutLicenseUrl =>
        "https://github.com/Nakanokappei/pdf-image-remover-for-rag/blob/main/docs/license-notices.md";

    /// <summary>Size cell for a shape: bounding box in points.</summary>
    public static string ShapeSize(int width, int height) => $"{width}×{height} pt";

    /// <summary>Prefix shown when more than one file is being opened.</summary>
    public static string ProgressFileCounter(int index, int count) => $"[{index}/{count}] ";

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
    public static string ColumnImageId => S.ColumnImageId;
    public static string ColumnType => S.ColumnType;
    public static string TypeImage => S.TypeImage;
    public static string TypeText => S.TypeText;
    public static string TypeShape => S.TypeShape;
    public static string ColumnSize => S.ColumnSize;
    public static string ColumnUsageCount => S.ColumnUsageCount;
    public static string ColumnCompression => S.ColumnCompression;
    public static string ColumnEstimatedSize => S.ColumnEstimatedSize;
    public static string ColumnWarning => S.ColumnWarning;
    public static string AccessibleDeleteColumn => S.AccessibleDeleteColumn;
    public static string TextSize(int characterCount) => S.TextSize(characterCount);

    // --- status bar / progress ---------------------------------------------

    public static string StatusOpenPrompt => S.StatusOpenPrompt;
    public static string StatusAnalyzing => S.StatusAnalyzing;
    public static string Cancel => S.Cancel;
    public static string StatusCancelling => S.StatusCancelling;
    public static string StatusCancelled => S.StatusCancelled;

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

    public static string StatusSaved(int fileCount, int drawCallsRemoved) =>
        S.StatusSaved(fileCount, drawCallsRemoved);

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

    // --- workflow messages -------------------------------------------------

    public static string ErrorSameAsSource => S.ErrorSameAsSource;
    public static string ErrorNoSelection => S.ErrorNoSelection;

    public static string VerificationCleanerSummary(int pagesModified, int drawCallsRemoved) =>
        S.VerificationCleanerSummary(pagesModified, drawCallsRemoved);

    public static string VerificationMoreWarnings(int remaining) =>
        S.VerificationMoreWarnings(remaining);

    public static string ErrorVerificationFailedPrefix => S.ErrorVerificationFailedPrefix;
}
