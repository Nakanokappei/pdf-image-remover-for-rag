namespace PdfImageRemoverForRag.App.Localization;

/// <summary>
/// Turkish (tr) UI text. Menu access keys follow the Western convention: the
/// "&amp;" marks a letter inside the word.
///
/// Counted strings are phrased so one wording fits every number. Turkish does
/// not pluralize a noun after a numeral, so "1 dosya" and "1306 dosya" are both
/// correct; where a case suffix would be needed it is attached to a following
/// fixed word ("{n} dosya kaydedildi", "{n} tane daha") rather than to the
/// number itself, which keeps the sentence correct for any value.
/// </summary>
internal sealed class TurkishStrings : IStrings
{
    public string AppTitle => "PDF Image Remover for RAG";

    public string MenuFile => "&Dosya";
    public string MenuOpen => "&Aç…";
    public string MenuSave => "Seçilenleri Kaldır ve &Kaydet…";
    public string MenuCloseAll => "&Tümünü Kapat";
    public string MenuExit => "&Çıkış";
    public string MenuView => "&Görünüm";
    public string MenuTableView => "&Tablo";
    public string MenuTileView => "&Döşeme";
    public string MenuShownTypes => "G&österilen Türler";
    public string MenuShowImages => "Görüntüler";
    public string MenuShowShapes => "Şekiller";
    public string MenuShowText => "Metin";
    public string MenuHelp => "&Yardım";
    public string MenuManual => "Çevrimiçi &Kılavuz…";
    public string MenuAbout => "&Hakkında…";

    public string ManualUrl =>
        "https://github.com/Nakanokappei/pdf-image-remover-for-rag/blob/main/docs/manual.en.md";

    public string LinkOpenFailed =>
        "Sayfa açılamadı. Lütfen bu adresi tarayıcınızda açın:\n";

    public string ToolOpen => "PDF Aç";
    public string ToolSave => "Kaldır ve Kaydet";
    public string ToolSelectAll => "Tümünü Seçin";
    public string ToolClearSelection => "Seçimi Temizleyin";

    // Column captions are kept short on purpose: they sit in narrow table
    // columns. Shortened from their natural full forms are "Nesne ID"
    // (Nesne kimliği), "Kullanım" (Kullanım sayısı) and "Tah. boyut"
    // (Tahmini boyut).
    public string ColumnThumbnail => "Küçük resim";
    public string ColumnImageId => "Nesne ID";
    public string ColumnType => "Tür";
    public string TypeImage => "Görüntü";
    public string TypeText => "Metin";
    public string TypeShape => "Şekil";
    public string ColumnSize => "Boyut";
    public string ColumnUsageCount => "Kullanım";
    public string ColumnCompression => "Sıkıştırma";
    public string ColumnEstimatedSize => "Tah. boyut";
    public string ColumnWarning => "Uyarı";
    public string AccessibleDeleteColumn => "Kaldır";
    public string TextSize(int characterCount) => $"{characterCount} karakter";

    public string StatusOpenPrompt => "Başlamak için bir PDF açın";
    public string StatusAnalyzing => "PDF çözümleniyor…";

    public string Cancel => "İptal";
    public string StatusCancelling => "İptal ediliyor…";
    public string StatusCancelled => "Açma işlemi iptal edildi";

    public string ProgressReadingPages(string fileName, int page, int pageCount) =>
        $"{fileName} — sayfa {page}/{pageCount} çözümleniyor";

    public string ProgressThumbnails(string fileName, int page, int pageCount) =>
        $"{fileName} — küçük resimler oluşturuluyor, sayfa {page}/{pageCount}";

    public string ProgressGrouping(string fileName) =>
        $"{fileName} — nesneler gruplanıyor";

    public string ThumbnailPending => "Küçük resim oluşturuluyor…";

    public string StatusAnalyzed => "Çözümleme tamamlandı";
    public string StatusOpenFailed => "PDF açılamadı";
    public string StatusSaving => "Kaydediliyor…";
    public string StatusSaveFailed => "Kaydetme başarısız";

    public string StatusSaved(int fileCount, int drawCallsRemoved) =>
        $"{fileCount} dosya kaydedildi — {drawCallsRemoved} çizim çağrısı kaldırıldı, doğrulama başarılı";

    public string StatusSelection(int selectedCount) =>
        $"{selectedCount} nesne grubu kaldırılmak üzere seçildi";

    public string WarningNotRemovable => "Kaldırılamaz";

    public string WarningFullPage =>
        "Büyük olasılıkla taranmış bir sayfa - kaldırılırsa sayfa boş kalır";

    public string TooltipUnsafe =>
        "PDF'in karmaşık yapısı nedeniyle bu görüntü güvenli biçimde kaldırılamaz.";

    public string TooltipFullPage =>
        "Bu görüntü sayfanın tamamını kaplıyor olabilir.\n" +
        "Kaldırılması, gövde metni dahil o sayfada görünen her şeyi silebilir.";

    public string OpenDialogTitle => "PDF Aç";
    public string PdfFileFilter => "PDF dosyaları (*.pdf)|*.pdf";
    public string SaveDialogTitle => "Seçilenleri Kaldır ve Kaydet";

    public string OutputFolderDescription =>
        "Temizlenmiş PDF'ler için klasörü seçin. Her dosya \"<ad>_cleaned.pdf\" olarak kaydedilir.";

    public string SameAsSourceMessage =>
        "Temizlenmiş PDF kaynak dosyanın üzerine yazamaz. Farklı bir ad seçin.";

    public string SameAsSourceTitle => "Kayıt Konumu";
    public string ConfirmTitle => "Onay";

    public string ConfirmSaveBeforeOpen =>
        "Kaldırılmak üzere seçilmiş nesneler var. Yeni dosyaları açmadan önce kaydedilsin mi?\n" +
        "Hayır'ı seçerseniz geçerli seçim atılır.";

    public string ConfirmDiscardBeforeOpen =>
        "Şu anda açık olan dosyalar kapatılıp yenileri açılsın mı?";

    public string ErrorDialogTitle => "Hata";
    public string CopyDetails => "Ayrıntıları Kopyalayın";
    public string AboutTitle => "PDF Image Remover for RAG Hakkında";

    public string AboutDescription =>
        "PDF'ler RAG işlem hattınıza girmeden önce, bilgi getirimini zorlaştıran nesneleri — " +
        "logo görüntülerini, yinelenen üstbilgi ve altbilgi metinlerini, cetvel çizgilerini — " +
        "kaldırır. Özgün dosyalarınız hiçbir zaman değiştirilmez ve tüm işlemler bu " +
        "bilgisayarda yerel olarak çalışır.";

    public string AboutAppLicense => "MIT Lisansı ile yayımlanmıştır.";
    public string AboutThirdPartyLicense => "Kitaplıklar: PDFsharp (MIT), PdfPig (Apache-2.0)";
    public string AboutLicenseLink => "Lisans bilgileri";

    public string ErrorSameAsSource =>
        "Kaynak PDF'in üzerine kaydedilemez. Farklı bir ad seçin.";

    public string ErrorNoSelection => "Kaldırılmak üzere seçilmiş görüntü yok.";

    public string VerificationCleanerSummary(int pagesModified, int drawCallsRemoved) =>
        $"(temizleyici: {pagesModified} sayfa, {drawCallsRemoved} çizim çağrısı) ";

    public string VerificationMoreWarnings(int remaining) => $" ve {remaining} tane daha";

    public string ErrorVerificationFailedPrefix => "Kayıt sonrası doğrulama başarısız: ";

    public ErrorText NotAPdf => new(
        "Seçilen dosya bir PDF değil.",
        ".pdf uzantılı geçerli bir dosya seçin.");

    public ErrorText PdfCorrupted => new(
        "PDF dosyası bozuk veya okunamayan bir biçimde.",
        "Başka bir PDF görüntüleyicinin dosyayı açıp açamadığını denetleyin.");

    public ErrorText PdfEncrypted => new(
        "Bu PDF şifrelenmiş.",
        "Bu sürüm parola korumalı PDF'leri desteklemiyor. Korumayı kaldırıp yeniden deneyin.");

    public ErrorText PdfPasswordRequired => new(
        "Bu PDF'i açmak için parola gerekiyor.",
        "Bu sürüm parola girişini desteklemiyor. Korumayı kaldırıp yeniden deneyin.");

    public ErrorText UnsupportedEncryption => new(
        "PDF, desteklenmeyen bir şifreleme yöntemi kullanıyor.",
        "Kullanılan şifrelemeyi belgeyi oluşturan tarafa sorun.");

    public ErrorText ImageExtractionFailed => new(
        "Görüntüler PDF'ten çıkarılamadı.",
        "Sorunun başka bir PDF'te de yinelenip yinelenmediğini denetleyin. Yineleniyorsa ayrıntıları kopyalayıp bildirin.");

    public ErrorText ImageRemovalUnsafe => new(
        "PDF'in karmaşık yapısı nedeniyle bu görüntü güvenli biçimde kaldırılamaz.",
        "İlgili görüntünün işaretini kaldırıp yeniden kaydedin.");

    public ErrorText DestinationNotWritable => new(
        "Hedef konuma yazılamıyor.",
        "Başka bir klasör seçin veya yazma iznini denetleyin. Kaynak PDF'in üzerine yazılamaz.");

    public ErrorText FileInUse => new(
        "Dosya başka bir uygulamada açık.",
        "Dosyayı kullanan uygulamayı kapatın, sonra yeniden deneyin.");

    public ErrorText DiskFull => new(
        "Diskte yeterli boş alan yok.",
        "Diskte yer açın, sonra yeniden deneyin.");

    public ErrorText PostSaveVerificationFailed => new(
        "Kayıt sonrası doğrulama başarısız olduğundan PDF kaydedilmedi.",
        "Kaynak PDF değişmedi. Ayrıntıları kopyalayıp sorunu bildirin.");

    public ErrorText UserCancelled => new("İşlem iptal edildi.", "");

    public ErrorText Unexpected => new(
        "Beklenmeyen bir hata oluştu.",
        "Ayrıntıları kopyalayıp geliştiriciye bildirin.");
}
