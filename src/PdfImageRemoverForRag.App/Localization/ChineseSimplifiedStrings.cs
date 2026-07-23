namespace PdfImageRemoverForRag.App.Localization;

/// <summary>Simplified Chinese (zh-Hans) UI text. Selected for any zh-Hans display language.</summary>
internal sealed class ChineseSimplifiedStrings : IStrings
{
    // Marketing name; the Microsoft Store lists the product in English.
    public string AppTitle => "PDF Image Remover for RAG";

    public string MenuFile => "文件(&F)";
    public string MenuOpen => "打开(&O)…";
    public string MenuSave => "删除所选对象并保存(&S)…";
    public string MenuCloseAll => "全部关闭(&C)";
    public string MenuExit => "退出(&X)";
    public string MenuView => "视图(&V)";
    public string MenuTableView => "表格(&T)";
    public string MenuTileView => "平铺(&I)";
    public string MenuShownTypes => "显示的类型(&S)";
    public string MenuShowImages => "图像";
    public string MenuShowShapes => "形状";
    public string MenuShowText => "文本";
    public string MenuHelp => "帮助(&H)";
    public string MenuManual => "在线手册(&M)…";
    public string MenuAbout => "关于(&A)…";

    // Only Japanese and English manual pages exist, so point at the English one.
    public string ManualUrl =>
        "https://github.com/Nakanokappei/pdf-image-remover-for-rag/blob/main/docs/manual.en.md";

    public string LinkOpenFailed =>
        "无法打开该页面。请在浏览器中打开以下网址：\n";

    public string ToolOpen => "打开 PDF";
    public string ToolSave => "删除并保存";
    public string ToolSelectAll => "全选";
    public string ToolClearSelection => "清除选择";

    public string ColumnThumbnail => "缩略图";
    public string ColumnImageId => "对象 ID";
    public string ColumnType => "类型";
    public string TypeImage => "图像";
    public string TypeText => "文本";
    public string TypeShape => "形状";
    public string ColumnSize => "尺寸";
    public string ColumnUsageCount => "使用次数";
    public string ColumnCompression => "压缩";
    public string ColumnEstimatedSize => "估计容量";
    public string ColumnWarning => "警告";
    public string AccessibleDeleteColumn => "删除";
    public string TextSize(int characterCount) => $"{characterCount} 个字符";

    public string StatusOpenPrompt => "请打开 PDF 以开始";
    public string StatusAnalyzing => "正在分析 PDF…";

    public string Cancel => "取消";
    public string StatusCancelling => "正在取消…";
    public string StatusCancelled => "已取消打开";

    public string ProgressReadingPages(string fileName, int page, int pageCount) =>
        $"{fileName} — 正在分析第 {page}/{pageCount} 页";

    public string ProgressThumbnails(string fileName, int page, int pageCount) =>
        $"{fileName} — 正在生成缩略图，第 {page}/{pageCount} 页";

    public string ProgressGrouping(string fileName) =>
        $"{fileName} — 正在归类对象";

    public string ThumbnailPending => "正在生成缩略图…";

    public string StatusAnalyzed => "分析完成";
    public string StatusOpenFailed => "无法打开该 PDF";
    public string StatusSaving => "正在保存…";
    public string StatusSaveFailed => "保存失败";

    public string StatusSaved(int fileCount, int drawCallsRemoved) =>
        $"已保存 {fileCount} 个文件（删除了 {drawCallsRemoved} 处绘制调用，保存后验证正常）";

    public string StatusSelection(int selectedCount) =>
        $"已选择 {selectedCount} 组图像待删除";

    public string WarningNotRemovable => "无法删除";

    public string WarningFullPage =>
        "可能是扫描页面的图像 - 删除后整页将变为空白";

    public string TooltipUnsafe =>
        "由于 PDF 结构复杂，无法安全删除该图像。";

    public string TooltipFullPage =>
        "该图像可能构成了整个页面。\n" +
        "删除后，该页面上的所有可见内容（包括正文）都可能被清除。";

    public string OpenDialogTitle => "打开 PDF";
    public string PdfFileFilter => "PDF 文件 (*.pdf)|*.pdf";
    public string SaveDialogTitle => "删除所选对象并保存";

    public string OutputFolderDescription =>
        "请选择保存处理后 PDF 的文件夹。每个文件将保存为“<文件名>_cleaned.pdf”。";

    public string SameAsSourceMessage =>
        "处理后的 PDF 不能覆盖源文件。请指定其他文件名。";

    public string SameAsSourceTitle => "保存位置";
    public string ConfirmTitle => "确认";

    public string ConfirmSaveBeforeOpen =>
        "您已选择了待删除的对象。是否在打开新文件前保存？\n" +
        "选择“否”将放弃当前的选择。";

    public string ConfirmDiscardBeforeOpen =>
        "是否关闭当前打开的文件并打开新文件？";

    public string ErrorDialogTitle => "错误";
    public string CopyDetails => "复制详细信息";
    public string AboutTitle => "关于 PDF Image Remover for RAG";

    public string AboutDescription =>
        "在 PDF 进入 RAG 流程之前，删除其中妨碍检索的对象——徽标图像、" +
        "重复出现的页眉页脚文本、表格线等。原始文件不会被修改，" +
        "所有处理都在本机上完成。";

    public string AboutAppLicense => "本应用基于 MIT 许可证发布。";
    public string AboutThirdPartyLicense => "使用的库：PDFsharp (MIT)、PdfPig (Apache-2.0)";
    public string AboutLicenseLink => "许可证信息";

    public string ErrorSameAsSource =>
        "无法覆盖源 PDF 进行保存。请指定其他文件名。";

    public string ErrorNoSelection => "未选择任何待删除的图像。";

    public string VerificationCleanerSummary(int pagesModified, int drawCallsRemoved) =>
        $"（删除处理：{pagesModified} 页、{drawCallsRemoved} 处绘制调用）";

    public string VerificationMoreWarnings(int remaining) => $" 等另外 {remaining} 项";

    public string ErrorVerificationFailedPrefix => "保存后验证失败：";

    public ErrorText NotAPdf => new(
        "所选文件不是 PDF。",
        "请选择扩展名为 .pdf 的有效文件。");

    public ErrorText PdfCorrupted => new(
        "该 PDF 文件已损坏，或格式无法读取。",
        "请确认其他 PDF 查看器是否能够打开该文件。");

    public ErrorText PdfEncrypted => new(
        "该 PDF 已加密。",
        "本版本不支持带密码的 PDF。请先解除保护，然后重试。");

    public ErrorText PdfPasswordRequired => new(
        "打开该 PDF 需要密码。",
        "本版本不支持输入密码。请先解除保护，然后重试。");

    public ErrorText UnsupportedEncryption => new(
        "该 PDF 使用了不受支持的加密方式。",
        "请向文档的制作方确认所使用的加密方式。");

    public ErrorText ImageExtractionFailed => new(
        "无法从该 PDF 中提取图像。",
        "请确认换用其他 PDF 是否也会出现该问题。若会出现，请复制详细信息并反馈。");

    public ErrorText ImageRemovalUnsafe => new(
        "由于 PDF 结构复杂，无法安全删除该图像。",
        "请取消勾选相关图像后再保存。");

    public ErrorText DestinationNotWritable => new(
        "保存位置不可写入。",
        "请指定其他文件夹，或检查写入权限。源 PDF 不能被覆盖。");

    public ErrorText FileInUse => new(
        "该文件正在其他应用程序中打开。",
        "请关闭正在使用该文件的应用程序，然后重试。");

    public ErrorText DiskFull => new(
        "磁盘可用空间不足。",
        "请释放磁盘空间，然后重试。");

    public ErrorText PostSaveVerificationFailed => new(
        "保存后验证失败，因此未保存该 PDF。",
        "源 PDF 未被更改。请复制详细信息并反馈该问题。");

    public ErrorText UserCancelled => new("操作已取消。", "");

    public ErrorText Unexpected => new(
        "发生了意外错误。",
        "请复制详细信息并反馈给开发者。");
}
