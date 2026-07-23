namespace PdfImageRemoverForRag.App.Localization;

/// <summary>
/// Vietnamese (vi) UI text. Menu access keys follow the Western convention:
/// the "&amp;" marks a letter inside the caption, always a letter without a
/// diacritic so it can be typed on a plain keyboard.
/// </summary>
internal sealed class VietnameseStrings : IStrings
{
    public string AppTitle => "PDF Image Remover for RAG";

    public string MenuFile => "&Tệp";
    public string MenuOpen => "&Mở…";
    public string MenuSave => "Xóa mục đã chọn và &Lưu…";
    public string MenuCloseAll => "Đóng tất &cả";
    public string MenuExit => "T&hoát";
    public string MenuView => "&Xem";
    public string MenuTableView => "&Bảng";
    public string MenuTileView => "&Thẻ";
    public string MenuShownTypes => "Loại &hiển thị";
    public string MenuShowImages => "Hình ảnh";
    public string MenuShowShapes => "Hình vẽ";
    public string MenuShowText => "Văn bản";
    public string MenuHelp => "Trợ &giúp";
    public string MenuManual => "&Hướng dẫn trực tuyến…";
    public string MenuAbout => "&Giới thiệu…";

    // Only Japanese and English manual pages exist, so Vietnamese points at English.
    public string ManualUrl =>
        "https://github.com/Nakanokappei/pdf-image-remover-for-rag/blob/main/docs/manual.en.md";

    public string LinkOpenFailed =>
        "Không thể mở trang. Vui lòng mở địa chỉ này trong trình duyệt:\n";

    public string ToolOpen => "Mở tệp PDF";
    public string ToolSave => "Xóa và lưu";
    public string ToolSelectAll => "Chọn tất cả";
    public string ToolClearSelection => "Bỏ chọn tất cả";

    // Column captions are deliberately shortened to fit narrow table columns:
    // ColumnThumbnail ("Ảnh nhỏ", not "Ảnh thu nhỏ"), ColumnUsageCount
    // ("Lần dùng", not "Số lần sử dụng"), ColumnCompression ("Nén", not
    // "Kiểu nén"), ColumnEstimatedSize ("Ước tính", not "Dung lượng ước tính").
    public string ColumnThumbnail => "Ảnh nhỏ";
    public string ColumnImageId => "Mã đối tượng";
    public string ColumnType => "Loại";
    public string TypeImage => "Hình ảnh";
    public string TypeText => "Văn bản";
    public string TypeShape => "Hình vẽ";
    public string ColumnSize => "Kích thước";
    public string ColumnUsageCount => "Lần dùng";
    public string ColumnCompression => "Nén";
    public string ColumnEstimatedSize => "Ước tính";
    public string ColumnWarning => "Cảnh báo";
    public string AccessibleDeleteColumn => "Xóa";
    public string TextSize(int characterCount) => $"{characterCount} ký tự";

    public string StatusOpenPrompt => "Mở một tệp PDF để bắt đầu";
    public string StatusAnalyzing => "Đang phân tích tệp PDF…";

    public string Cancel => "Hủy";
    public string StatusCancelling => "Đang hủy…";
    public string StatusCancelled => "Đã hủy việc mở tệp";

    public string ProgressReadingPages(string fileName, int page, int pageCount) =>
        $"{fileName} — đang phân tích trang {page}/{pageCount}";

    public string ProgressThumbnails(string fileName, int page, int pageCount) =>
        $"{fileName} — đang tạo ảnh nhỏ, trang {page}/{pageCount}";

    public string ProgressGrouping(string fileName) =>
        $"{fileName} — đang nhóm các đối tượng";

    public string ThumbnailPending => "Đang tạo ảnh nhỏ…";

    public string StatusAnalyzed => "Đã phân tích xong";
    public string StatusOpenFailed => "Không thể mở tệp PDF";
    public string StatusSaving => "Đang lưu…";
    public string StatusSaveFailed => "Lưu không thành công";

    public string StatusSaved(int fileCount, int drawCallsRemoved) =>
        $"Đã lưu {fileCount} tệp — đã xóa {drawCallsRemoved} lệnh vẽ, kiểm tra hợp lệ";

    public string StatusSelection(int selectedCount) =>
        $"Đã chọn {selectedCount} nhóm ảnh để xóa";

    public string WarningNotRemovable => "Không thể xóa";

    public string WarningFullPage =>
        "Có thể là ảnh quét của cả trang - xóa sẽ làm trang trống hoàn toàn";

    public string TooltipUnsafe =>
        "Không thể xóa an toàn hình ảnh này vì cấu trúc phức tạp của tệp PDF.";

    public string TooltipFullPage =>
        "Hình ảnh này có thể chiếm trọn cả trang.\n" +
        "Xóa nó có thể làm mất mọi nội dung hiển thị trên trang đó, kể cả phần nội dung chính.";

    public string OpenDialogTitle => "Mở tệp PDF";
    public string PdfFileFilter => "Tệp PDF (*.pdf)|*.pdf";
    public string SaveDialogTitle => "Xóa mục đã chọn và lưu";

    public string OutputFolderDescription =>
        "Chọn thư mục cho các tệp PDF đã xử lý. Mỗi tệp được lưu thành \"<tên>_cleaned.pdf\".";

    public string SameAsSourceMessage =>
        "Tệp PDF đã xử lý không thể ghi đè lên tệp nguồn. Vui lòng chọn tên khác.";

    public string SameAsSourceTitle => "Vị trí lưu";
    public string ConfirmTitle => "Xác nhận";

    public string ConfirmSaveBeforeOpen =>
        "Bạn đang chọn một số đối tượng để xóa. Lưu trước khi mở tệp mới?\n" +
        "Chọn Không sẽ bỏ các lựa chọn hiện tại.";

    public string ConfirmDiscardBeforeOpen =>
        "Đóng các tệp đang mở và mở tệp mới?";

    public string ErrorDialogTitle => "Lỗi";
    public string CopyDetails => "Sao chép chi tiết";
    public string AboutTitle => "Giới thiệu về PDF Image Remover for RAG";

    public string AboutDescription =>
        "Xóa những đối tượng gây nhiễu cho việc truy xuất — hình ảnh logo, văn bản đầu " +
        "trang và chân trang lặp lại, đường kẻ — khỏi tệp PDF trước khi đưa vào quy trình " +
        "RAG. Tệp gốc của bạn không bao giờ bị thay đổi và mọi thao tác đều chạy cục bộ " +
        "trên máy tính này.";

    public string AboutAppLicense => "Phát hành theo Giấy phép MIT.";
    public string AboutThirdPartyLicense => "Thư viện: PDFsharp (MIT), PdfPig (Apache-2.0)";
    public string AboutLicenseLink => "Thông tin giấy phép";

    public string ErrorSameAsSource =>
        "Không thể lưu đè lên tệp PDF nguồn. Vui lòng chọn tên khác.";

    public string ErrorNoSelection => "Chưa chọn hình ảnh nào để xóa.";

    public string VerificationCleanerSummary(int pagesModified, int drawCallsRemoved) =>
        $"(xử lý: {pagesModified} trang, {drawCallsRemoved} lệnh vẽ) ";

    public string VerificationMoreWarnings(int remaining) => $" và {remaining} mục khác";

    public string ErrorVerificationFailedPrefix => "Kiểm tra sau khi lưu không đạt: ";

    public ErrorText NotAPdf => new(
        "Tệp đã chọn không phải là tệp PDF.",
        "Hãy chọn một tệp hợp lệ có phần mở rộng .pdf.");

    public ErrorText PdfCorrupted => new(
        "Tệp PDF bị hỏng hoặc ở định dạng không đọc được.",
        "Hãy kiểm tra xem một trình xem PDF khác có mở được tệp này không.");

    public ErrorText PdfEncrypted => new(
        "Tệp PDF này được mã hóa.",
        "Phiên bản này không hỗ trợ tệp PDF được bảo vệ bằng mật khẩu. Hãy gỡ bỏ bảo vệ rồi thử lại.");

    public ErrorText PdfPasswordRequired => new(
        "Cần có mật khẩu để mở tệp PDF này.",
        "Phiên bản này không hỗ trợ nhập mật khẩu. Hãy gỡ bỏ bảo vệ rồi thử lại.");

    public ErrorText UnsupportedEncryption => new(
        "Tệp PDF dùng phương thức mã hóa không được hỗ trợ.",
        "Hãy hỏi đơn vị tạo tài liệu về phương thức mã hóa được dùng.");

    public ErrorText ImageExtractionFailed => new(
        "Không thể trích xuất hình ảnh từ tệp PDF.",
        "Hãy kiểm tra xem lỗi có lặp lại với tệp PDF khác không. Nếu có, hãy sao chép chi tiết và báo lỗi.");

    public ErrorText ImageRemovalUnsafe => new(
        "Không thể xóa an toàn hình ảnh này vì cấu trúc phức tạp của tệp PDF.",
        "Hãy bỏ chọn hình ảnh liên quan rồi lưu lại.");

    public ErrorText DestinationNotWritable => new(
        "Không thể ghi vào vị trí đích.",
        "Hãy chọn thư mục khác hoặc kiểm tra quyền ghi. Không thể ghi đè lên tệp PDF nguồn.");

    public ErrorText FileInUse => new(
        "Tệp đang được mở trong ứng dụng khác.",
        "Hãy đóng ứng dụng đang dùng tệp này rồi thử lại.");

    public ErrorText DiskFull => new(
        "Không còn đủ dung lượng trống trên đĩa.",
        "Hãy giải phóng dung lượng đĩa rồi thử lại.");

    public ErrorText PostSaveVerificationFailed => new(
        "Kiểm tra sau khi lưu không đạt nên tệp PDF chưa được lưu.",
        "Tệp PDF nguồn không thay đổi. Hãy sao chép chi tiết và báo lỗi.");

    public ErrorText UserCancelled => new("Thao tác đã bị hủy.", "");

    public ErrorText Unexpected => new(
        "Đã xảy ra lỗi không mong muốn.",
        "Hãy sao chép chi tiết và gửi cho nhà phát triển.");
}
