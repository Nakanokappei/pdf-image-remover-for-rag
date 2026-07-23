namespace PdfImageRemoverForRag.App.Localization;

/// <summary>Japanese (ja) UI text. Selected for any ja-* display language.</summary>
internal sealed class JapaneseStrings : IStrings
{
    public string AppTitle => "RAG 用 PDF 画像除去ツール";

    public string MenuFile => "ファイル(&F)";
    public string MenuOpen => "開く(&O)…";
    public string MenuSave => "選択画像を削除して保存(&S)…";
    public string MenuCloseAll => "すべて閉じる(&C)";
    public string MenuExit => "終了(&X)";
    public string MenuView => "表示(&V)";
    public string MenuTableView => "表形式(&T)";
    public string MenuTileView => "タイル形式(&I)";
    public string MenuShownTypes => "表示列(&D)";
    public string MenuShowImages => "画像";
    public string MenuShowShapes => "図形";
    public string MenuShowText => "テキスト";
    public string MenuHelp => "ヘルプ(&H)";
    public string MenuManual => "オンラインマニュアル(&M)…";
    public string MenuAbout => "このアプリについて(&A)…";

    public string ManualUrl =>
        "https://github.com/Nakanokappei/pdf-image-remover-for-rag/blob/main/docs/manual.ja.md";

    public string LinkOpenFailed =>
        "ページを開けませんでした。次の URL をブラウザーで開いてください。\n";

    public string ToolOpen => "PDFを開く";
    public string ToolSave => "削除して保存";
    public string ToolSelectAll => "すべてを選択";
    public string ToolClearSelection => "選択解除";

    public string ColumnThumbnail => "サムネイル";
    public string ColumnImageId => "オブジェクトID";
    public string ColumnType => "タイプ";
    public string TypeImage => "画像";
    public string TypeText => "テキスト";
    public string TypeShape => "図形";
    public string ColumnSize => "サイズ";
    public string ColumnUsageCount => "使用回数";
    public string ColumnCompression => "圧縮";
    public string ColumnEstimatedSize => "推定容量";
    public string ColumnWarning => "警告";
    public string AccessibleDeleteColumn => "削除";
    public string TextSize(int characterCount) => $"{characterCount} 文字";

    public string StatusOpenPrompt => "PDFを開いてください";
    public string StatusAnalyzing => "PDFを解析しています…";

    public string Cancel => "キャンセル";
    public string StatusCancelling => "キャンセルしています…";
    public string StatusCancelled => "読み込みをキャンセルしました";

    public string ProgressReadingPages(string fileName, int page, int pageCount) =>
        $"{fileName} — {page}/{pageCount} ページを解析中";

    public string ProgressThumbnails(string fileName, int page, int pageCount) =>
        $"{fileName} — サムネイルを作成中 {page}/{pageCount} ページ";

    public string ProgressGrouping(string fileName) =>
        $"{fileName} — オブジェクトを集計中";

    public string ThumbnailPending => "サムネイルを生成中…";

    public string StatusAnalyzed => "PDFの解析が完了しました";
    public string StatusOpenFailed => "PDFを開けませんでした";
    public string StatusSaving => "保存しています…";
    public string StatusSaveFailed => "保存に失敗しました";

    public string StatusSaved(int fileCount, int drawCallsRemoved) =>
        $"{fileCount} ファイルを保存しました（{drawCallsRemoved} 箇所を削除、保存後検証 OK）";

    public string StatusSelection(int selectedCount) =>
        $"削除対象：{selectedCount} 件を選択中";

    public string WarningNotRemovable => "削除不可";

    public string WarningFullPage =>
        "恐らくスキャン文書の画像。削除するとページ全体が空白になります";

    public string TooltipUnsafe =>
        "複雑なPDF構造のため、この画像は安全に削除できません。";

    public string TooltipFullPage =>
        "ページ全体を構成している画像の可能性があります。\n" +
        "この画像を削除すると、ページの本文を含むすべての表示内容が失われる可能性があります。";

    public string OpenDialogTitle => "PDFを開く";
    public string PdfFileFilter => "PDF ファイル (*.pdf)|*.pdf";
    public string SaveDialogTitle => "選択画像を削除して保存";

    public string OutputFolderDescription =>
        "削除後の PDF を保存するフォルダーを選択してください。各ファイルは「元ファイル名_cleaned.pdf」で保存されます。";

    public string SameAsSourceMessage =>
        "元の PDF と同じパスには保存できません。別のファイル名を指定してください。";

    public string SameAsSourceTitle => "保存先の確認";
    public string ConfirmTitle => "確認";

    public string ConfirmSaveBeforeOpen =>
        "削除対象に選択中のオブジェクトがあります。新しいファイルを開く前に保存しますか？\n" +
        "「いいえ」を選ぶと選択内容は破棄されます。";

    public string ConfirmDiscardBeforeOpen =>
        "現在開いているファイルを閉じて、新しいファイルを開きますか？";

    public string ErrorDialogTitle => "エラー";
    public string CopyDetails => "詳細をコピー";
    public string AboutTitle => "このアプリについて";

    public string AboutDescription =>
        "RAG に登録する前の PDF から、検索の邪魔になるオブジェクト（ロゴなどの画像、" +
        "繰り返し現れるヘッダーやフッターのテキスト、罫線などの図形）を取り除きます。" +
        "元の PDF は変更せず、処理はすべてこの PC の中で完結します。";

    public string AboutAppLicense => "本アプリは MIT ライセンスで公開されています。";
    public string AboutThirdPartyLicense => "利用ライブラリ: PDFsharp (MIT) / PdfPig (Apache-2.0)";
    public string AboutLicenseLink => "ライセンス情報";

    public string ErrorSameAsSource =>
        "元 PDF と同じパスへの保存はできません。別名を指定してください。";

    public string ErrorNoSelection => "削除対象の画像が指定されていません。";

    public string VerificationCleanerSummary(int pagesModified, int drawCallsRemoved) =>
        $"（削除処理: {pagesModified} ページ / {drawCallsRemoved} 箇所）";

    public string VerificationMoreWarnings(int remaining) => $" ほか {remaining} 件";

    public string ErrorVerificationFailedPrefix => "保存後の検証に失敗しました: ";

    public ErrorText NotAPdf => new(
        "選択されたファイルは PDF ではありません。",
        "拡張子が .pdf の正しいファイルを選択してください。");

    public ErrorText PdfCorrupted => new(
        "PDF ファイルが壊れているか、読み取れない形式です。",
        "別の PDF ビューアーで開けるかどうか確認してください。");

    public ErrorText PdfEncrypted => new(
        "この PDF は暗号化されています。",
        "本バージョンはパスワード付き PDF に対応していません。保護を解除してから再度開いてください。");

    public ErrorText PdfPasswordRequired => new(
        "この PDF を開くにはパスワードが必要です。",
        "本バージョンはパスワード入力に対応していません。保護を解除してから再度開いてください。");

    public ErrorText UnsupportedEncryption => new(
        "対応していない暗号化方式の PDF です。",
        "PDF の作成元に暗号化方式を確認してください。");

    public ErrorText ImageExtractionFailed => new(
        "PDF 内の画像を抽出できませんでした。",
        "別の PDF で問題が再現するか確認してください。再現する場合は詳細をコピーして報告してください。");

    public ErrorText ImageRemovalUnsafe => new(
        "複雑なPDF構造のため、この画像は安全に削除できません。",
        "対象画像のチェックを外してから保存してください。");

    public ErrorText DestinationNotWritable => new(
        "保存先に書き込めません。",
        "別のフォルダーを指定するか、書き込み権限を確認してください。元の PDF と同じパスには保存できません。");

    public ErrorText FileInUse => new(
        "ファイルが他のアプリケーションで開かれています。",
        "対象ファイルを開いているアプリケーションを閉じてから、もう一度実行してください。");

    public ErrorText DiskFull => new(
        "ディスクの空き容量が不足しています。",
        "不要なファイルを削除して空き容量を確保してから、もう一度実行してください。");

    public ErrorText PostSaveVerificationFailed => new(
        "保存後の検証に失敗したため、PDF は保存されませんでした。",
        "元の PDF は変更されていません。詳細をコピーして報告してください。");

    public ErrorText UserCancelled => new("処理をキャンセルしました。", "");

    public ErrorText Unexpected => new(
        "予期しないエラーが発生しました。",
        "詳細をコピーして開発者に報告してください。");
}
