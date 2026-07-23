namespace PdfImageRemoverForRag.App.Localization;

/// <summary>
/// Traditional Chinese (zh-Hant, Taiwan wording) UI text. Selected for any
/// zh-Hant / zh-TW display language.
/// </summary>
internal sealed class ChineseTraditionalStrings : IStrings
{
    public string AppTitle => "PDF Image Remover for RAG";

    public string MenuFile => "檔案(&F)";
    public string MenuOpen => "開啟(&O)…";
    public string MenuSave => "移除選取項目並儲存(&S)…";
    public string MenuCloseAll => "全部關閉(&C)";
    public string MenuExit => "結束(&X)";
    public string MenuView => "檢視(&V)";
    public string MenuTableView => "表格(&T)";
    public string MenuTileView => "並排圖標(&I)";
    public string MenuShownTypes => "顯示的類型(&S)";
    public string MenuShowImages => "圖片";
    public string MenuShowShapes => "圖形";
    public string MenuShowText => "文字";
    public string MenuHelp => "說明(&H)";
    public string MenuManual => "線上手冊(&M)…";
    public string MenuAbout => "關於(&A)…";

    // Only Japanese and English manual pages exist, so point at the English one.
    public string ManualUrl =>
        "https://github.com/Nakanokappei/pdf-image-remover-for-rag/blob/main/docs/manual.en.md";

    public string LinkOpenFailed =>
        "無法開啟該頁面。請在瀏覽器中開啟下列網址：\n";

    public string ToolOpen => "開啟 PDF";
    public string ToolSave => "移除並儲存";
    public string ToolSelectAll => "全選";
    public string ToolClearSelection => "清除選取";

    public string ColumnThumbnail => "縮圖";
    public string ColumnImageId => "物件 ID";
    public string ColumnType => "類型";
    public string TypeImage => "圖片";
    public string TypeText => "文字";
    public string TypeShape => "圖形";
    public string ColumnSize => "大小";
    public string ColumnUsageCount => "使用次數";
    public string ColumnCompression => "壓縮";
    public string ColumnEstimatedSize => "預估容量";
    public string ColumnWarning => "警告";
    public string AccessibleDeleteColumn => "移除";
    public string TextSize(int characterCount) => $"{characterCount} 個字元";

    public string StatusOpenPrompt => "請先開啟 PDF";
    public string StatusAnalyzing => "正在分析 PDF…";

    public string Cancel => "取消";
    public string StatusCancelling => "正在取消…";
    public string StatusCancelled => "已取消開啟";

    public string ProgressReadingPages(string fileName, int page, int pageCount) =>
        $"{fileName} — 正在分析第 {page} 頁，共 {pageCount} 頁";

    public string ProgressThumbnails(string fileName, int page, int pageCount) =>
        $"{fileName} — 正在建立縮圖，第 {page} 頁，共 {pageCount} 頁";

    public string ProgressGrouping(string fileName) =>
        $"{fileName} — 正在彙整物件";

    public string ThumbnailPending => "正在產生縮圖…";

    public string StatusAnalyzed => "分析完成";
    public string StatusOpenFailed => "無法開啟該 PDF";
    public string StatusSaving => "正在儲存…";
    public string StatusSaveFailed => "儲存失敗";

    public string StatusSaved(int fileCount, int drawCallsRemoved) =>
        $"已儲存 {fileCount} 個檔案 — 移除了 {drawCallsRemoved} 處繪製指令，儲存後驗證正常";

    public string StatusSelection(int selectedCount) =>
        $"已選取 {selectedCount} 個圖片群組準備移除";

    public string WarningNotRemovable => "無法移除";

    public string WarningFullPage =>
        "可能是掃描的頁面 - 移除後整頁會變成空白";

    public string TooltipUnsafe =>
        "由於此 PDF 的結構複雜，無法安全移除這張圖片。";

    public string TooltipFullPage =>
        "這張圖片可能構成整個頁面。\n" +
        "移除後，該頁面上所有可見的內容（包含內文）都可能消失。";

    public string OpenDialogTitle => "開啟 PDF";
    public string PdfFileFilter => "PDF 檔案 (*.pdf)|*.pdf";
    public string SaveDialogTitle => "移除選取項目並儲存";

    public string OutputFolderDescription =>
        "請選擇存放處理後 PDF 的資料夾。每個檔案會以「<原檔名>_cleaned.pdf」儲存。";

    public string SameAsSourceMessage =>
        "處理後的 PDF 不能覆寫來源檔案。請指定其他檔案名稱。";

    public string SameAsSourceTitle => "儲存位置";
    public string ConfirmTitle => "確認";

    public string ConfirmSaveBeforeOpen =>
        "目前有選取準備移除的物件。要在開啟新檔案前先儲存嗎？\n" +
        "選擇「否」會捨棄目前的選取內容。";

    public string ConfirmDiscardBeforeOpen =>
        "要關閉目前開啟的檔案並開啟新的檔案嗎？";

    public string ErrorDialogTitle => "錯誤";
    public string CopyDetails => "複製詳細資料";
    public string AboutTitle => "關於 PDF Image Remover for RAG";

    public string AboutDescription =>
        "在 PDF 進入 RAG 流程之前，先移除會干擾檢索的物件 — 例如標誌圖片、" +
        "重複出現的頁首頁尾文字、格線等。原始檔案完全不會被修改，" +
        "所有處理都在這台電腦上完成。";

    public string AboutAppLicense => "本應用程式以 MIT 授權條款發行。";
    public string AboutThirdPartyLicense => "使用的程式庫：PDFsharp (MIT)、PdfPig (Apache-2.0)";
    public string AboutLicenseLink => "授權資訊";

    public string ErrorSameAsSource =>
        "無法覆寫來源 PDF。請指定其他檔案名稱。";

    public string ErrorNoSelection => "尚未選取要移除的圖片。";

    public string VerificationCleanerSummary(int pagesModified, int drawCallsRemoved) =>
        $"（移除處理：{pagesModified} 頁、{drawCallsRemoved} 處繪製指令）";

    public string VerificationMoreWarnings(int remaining) => $" 以及其他 {remaining} 項";

    public string ErrorVerificationFailedPrefix => "儲存後驗證失敗：";

    public ErrorText NotAPdf => new(
        "選取的檔案不是 PDF。",
        "請選擇副檔名為 .pdf 的正確檔案。");

    public ErrorText PdfCorrupted => new(
        "此 PDF 檔案已損毀，或格式無法讀取。",
        "請確認其他 PDF 檢視程式是否能開啟該檔案。");

    public ErrorText PdfEncrypted => new(
        "此 PDF 已加密。",
        "本版本不支援有密碼保護的 PDF。請先解除保護後再試一次。");

    public ErrorText PdfPasswordRequired => new(
        "開啟此 PDF 需要密碼。",
        "本版本不支援輸入密碼。請先解除保護後再試一次。");

    public ErrorText UnsupportedEncryption => new(
        "此 PDF 使用不支援的加密方式。",
        "請向文件的製作者確認所使用的加密方式。");

    public ErrorText ImageExtractionFailed => new(
        "無法從此 PDF 擷取圖片。",
        "請確認其他 PDF 是否也會發生相同問題。若會，請複製詳細資料並回報。");

    public ErrorText ImageRemovalUnsafe => new(
        "由於此 PDF 的結構複雜，無法安全移除這張圖片。",
        "請取消勾選該圖片後再儲存。");

    public ErrorText DestinationNotWritable => new(
        "無法寫入指定的儲存位置。",
        "請選擇其他資料夾，或確認寫入權限。來源 PDF 無法被覆寫。");

    public ErrorText FileInUse => new(
        "該檔案正由其他應用程式開啟中。",
        "請先關閉使用該檔案的應用程式，然後再試一次。");

    public ErrorText DiskFull => new(
        "磁碟可用空間不足。",
        "請釋出磁碟空間後再試一次。");

    public ErrorText PostSaveVerificationFailed => new(
        "儲存後驗證失敗，因此並未儲存該 PDF。",
        "來源 PDF 未被變更。請複製詳細資料並回報此問題。");

    public ErrorText UserCancelled => new("作業已取消。", "");

    public ErrorText Unexpected => new(
        "發生非預期的錯誤。",
        "請複製詳細資料並回報給開發者。");
}
