namespace PdfImageRemoverForRag.App.Localization;

/// <summary>Korean (ko) UI text. Selected for any ko-* display language.</summary>
internal sealed class KoreanStrings : IStrings
{
    public string AppTitle => "PDF Image Remover for RAG";

    public string MenuFile => "파일(&F)";
    public string MenuOpen => "열기(&O)…";
    public string MenuSave => "선택 항목 삭제 후 저장(&S)…";
    public string MenuCloseAll => "모두 닫기(&C)";
    public string MenuExit => "끝내기(&X)";
    public string MenuView => "보기(&V)";
    public string MenuTableView => "표(&T)";
    public string MenuTileView => "타일(&I)";
    public string MenuShownTypes => "표시할 종류(&S)";
    public string MenuShowImages => "이미지";
    public string MenuShowShapes => "도형";
    public string MenuShowText => "텍스트";
    public string MenuHelp => "도움말(&H)";
    public string MenuManual => "온라인 설명서(&M)…";
    public string MenuAbout => "정보(&A)…";

    // Only ja/en manual pages exist, so Korean points at the English one.
    public string ManualUrl =>
        "https://github.com/Nakanokappei/pdf-image-remover-for-rag/blob/main/docs/manual.en.md";

    public string LinkOpenFailed =>
        "페이지를 열 수 없습니다. 다음 URL을 브라우저에서 열어 주십시오.\n";

    public string ToolOpen => "PDF 열기";
    public string ToolSave => "삭제 후 저장";
    public string ToolSelectAll => "모두 선택";
    public string ToolClearSelection => "선택 해제";

    public string ColumnThumbnail => "미리 보기";
    public string ColumnImageId => "개체 ID";
    public string ColumnType => "종류";
    public string TypeImage => "이미지";
    public string TypeText => "텍스트";
    public string TypeShape => "도형";
    public string ColumnSize => "크기";
    public string ColumnUsageCount => "사용 횟수";
    public string ColumnCompression => "압축";
    public string ColumnEstimatedSize => "예상 용량";
    public string ColumnWarning => "경고";
    public string AccessibleDeleteColumn => "삭제";
    public string TextSize(int characterCount) => $"{characterCount}자";

    public string StatusOpenPrompt => "PDF를 열어 주십시오";
    public string StatusAnalyzing => "PDF를 분석하는 중…";

    public string Cancel => "취소";
    public string StatusCancelling => "취소하는 중…";
    public string StatusCancelled => "열기를 취소했습니다";

    public string ProgressReadingPages(string fileName, int page, int pageCount) =>
        $"{fileName} — {pageCount}페이지 중 {page}페이지 분석 중";

    public string ProgressThumbnails(string fileName, int page, int pageCount) =>
        $"{fileName} — 미리 보기 만드는 중, {pageCount}페이지 중 {page}페이지";

    public string ProgressGrouping(string fileName) =>
        $"{fileName} — 개체를 집계하는 중";

    public string ThumbnailPending => "미리 보기 생성 중…";

    public string StatusAnalyzed => "분석이 완료되었습니다";
    public string StatusOpenFailed => "PDF를 열 수 없습니다";
    public string StatusSaving => "저장하는 중…";
    public string StatusSaveFailed => "저장하지 못했습니다";

    public string StatusSaved(int fileCount, int drawCallsRemoved) =>
        $"{fileCount}개 파일을 저장했습니다 — {drawCallsRemoved}개 그리기 명령 삭제, 검증 정상";

    public string StatusSelection(int selectedCount) =>
        $"삭제 대상 {selectedCount}개 선택됨";

    public string WarningNotRemovable => "삭제 불가";

    public string WarningFullPage =>
        "스캔한 페이지로 보입니다. 삭제하면 해당 페이지가 빈 페이지가 됩니다";

    public string TooltipUnsafe =>
        "PDF 구조가 복잡하여 이 이미지는 안전하게 삭제할 수 없습니다.";

    public string TooltipFullPage =>
        "이 이미지가 페이지 전체를 구성하고 있을 수 있습니다.\n" +
        "삭제하면 본문을 포함하여 해당 페이지에 보이는 내용이 모두 사라질 수 있습니다.";

    public string OpenDialogTitle => "PDF 열기";
    public string PdfFileFilter => "PDF 파일 (*.pdf)|*.pdf";
    public string SaveDialogTitle => "선택 항목 삭제 후 저장";

    public string OutputFolderDescription =>
        "정리된 PDF를 저장할 폴더를 선택하십시오. 각 파일은 \"<이름>_cleaned.pdf\"로 저장됩니다.";

    public string SameAsSourceMessage =>
        "정리된 PDF는 원본 파일에 덮어쓸 수 없습니다. 다른 이름을 지정하십시오.";

    public string SameAsSourceTitle => "저장 위치";
    public string ConfirmTitle => "확인";

    public string ConfirmSaveBeforeOpen =>
        "삭제 대상으로 선택된 개체가 있습니다. 새 파일을 열기 전에 저장하시겠습니까?\n" +
        "아니요를 선택하면 현재 선택이 취소됩니다.";

    public string ConfirmDiscardBeforeOpen =>
        "현재 열려 있는 파일을 닫고 새 파일을 여시겠습니까?";

    public string ErrorDialogTitle => "오류";
    public string CopyDetails => "자세한 내용 복사";
    public string AboutTitle => "PDF Image Remover for RAG 정보";

    public string AboutDescription =>
        "RAG 파이프라인에 넣기 전의 PDF에서 검색에 방해가 되는 개체(로고 이미지, 반복되는 " +
        "머리글과 바닥글 텍스트, 괘선 등)를 제거합니다. 원본 파일은 변경되지 않으며, 모든 " +
        "처리는 이 PC 안에서 이루어집니다.";

    public string AboutAppLicense => "MIT 라이선스로 배포됩니다.";
    public string AboutThirdPartyLicense => "사용 라이브러리: PDFsharp (MIT), PdfPig (Apache-2.0)";
    public string AboutLicenseLink => "라이선스 정보";

    public string ErrorSameAsSource =>
        "원본 PDF에 덮어쓸 수 없습니다. 다른 이름을 지정하십시오.";

    public string ErrorNoSelection => "삭제할 이미지가 선택되지 않았습니다.";

    public string VerificationCleanerSummary(int pagesModified, int drawCallsRemoved) =>
        $"(삭제 처리: {pagesModified}페이지, 그리기 명령 {drawCallsRemoved}개) ";

    public string VerificationMoreWarnings(int remaining) => $" 외 {remaining}건";

    public string ErrorVerificationFailedPrefix => "저장 후 검증에 실패했습니다: ";

    public ErrorText NotAPdf => new(
        "선택한 파일은 PDF가 아닙니다.",
        "확장명이 .pdf인 올바른 파일을 선택하십시오.");

    public ErrorText PdfCorrupted => new(
        "PDF 파일이 손상되었거나 읽을 수 없는 형식입니다.",
        "다른 PDF 뷰어에서 열리는지 확인하십시오.");

    public ErrorText PdfEncrypted => new(
        "이 PDF는 암호화되어 있습니다.",
        "이 버전은 암호로 보호된 PDF를 지원하지 않습니다. 보호를 해제한 후 다시 시도하십시오.");

    public ErrorText PdfPasswordRequired => new(
        "이 PDF를 열려면 암호가 필요합니다.",
        "이 버전은 암호 입력을 지원하지 않습니다. 보호를 해제한 후 다시 시도하십시오.");

    public ErrorText UnsupportedEncryption => new(
        "지원하지 않는 암호화 방식을 사용하는 PDF입니다.",
        "문서를 만든 곳에 사용된 암호화 방식을 문의하십시오.");

    public ErrorText ImageExtractionFailed => new(
        "PDF에서 이미지를 추출할 수 없습니다.",
        "다른 PDF에서도 같은 문제가 발생하는지 확인하십시오. 발생한다면 자세한 내용을 복사하여 보고해 주십시오.");

    public ErrorText ImageRemovalUnsafe => new(
        "PDF 구조가 복잡하여 이 이미지는 안전하게 삭제할 수 없습니다.",
        "해당 이미지의 선택을 해제한 후 다시 저장하십시오.");

    public ErrorText DestinationNotWritable => new(
        "저장 위치에 쓸 수 없습니다.",
        "다른 폴더를 선택하거나 쓰기 권한을 확인하십시오. 원본 PDF에는 덮어쓸 수 없습니다.");

    public ErrorText FileInUse => new(
        "파일이 다른 응용 프로그램에서 열려 있습니다.",
        "해당 파일을 사용 중인 응용 프로그램을 닫은 후 다시 시도하십시오.");

    public ErrorText DiskFull => new(
        "디스크 여유 공간이 부족합니다.",
        "디스크 공간을 확보한 후 다시 시도하십시오.");

    public ErrorText PostSaveVerificationFailed => new(
        "저장 후 검증에 실패하여 PDF가 저장되지 않았습니다.",
        "원본 PDF는 변경되지 않았습니다. 자세한 내용을 복사하여 문제를 보고해 주십시오.");

    public ErrorText UserCancelled => new("작업이 취소되었습니다.", "");

    public ErrorText Unexpected => new(
        "예기치 않은 오류가 발생했습니다.",
        "자세한 내용을 복사하여 개발자에게 보고해 주십시오.");
}
