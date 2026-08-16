using PdfImageRemoverForRag.Core.Models;

namespace PdfImageRemoverForRag.App.Localization;

/// <summary>
/// Indonesian (id) and Malay (ms) UI text. ONE implementation registered under
/// both culture keys, so wording is chosen to read naturally in both standards.
/// Where they diverge the Indonesian form wins (larger market) as long as the
/// Malay reader still understands it: File (ms: Fail), Tampilan (ms: Paparan),
/// Ukuran (ms: Saiz), Karakter (ms: Aksara), Kompresi (ms: Mampatan),
/// Peringatan (ms: Amaran), Verifikasi (ms: Pengesahan), Konfirmasi
/// (ms: Pengesahan), Kesalahan (ms: Ralat), Lisensi (ms: Lesen), Detail
/// (ms: Butiran), dihapus (ms: dipadam), karena (ms: kerana), Silakan (ms: Sila).
/// Column headers shortened on purpose to fit narrow columns: "Miniatur"
/// (not "Gambar Kecil") and "Perk. Ukuran" (short for "Perkiraan Ukuran").
/// </summary>
internal sealed class IndonesianMalayStrings : IStrings
{
    public string AppTitle => "PDF Image Remover for RAG";

    public string MenuFile => "&File";
    public string MenuOpen => "&Buka…";
    public string MenuSave => "Hapus Pilihan dan &Simpan…";
    public string MenuCloseAll => "&Tutup Semua";
    public string MenuExit => "&Keluar";
    public string MenuView => "&Tampilan";
    public string MenuTableView => "&Tabel";
    public string MenuTileView => "&Petak";
    public string MenuShownTypes => "&Jenis Ditampilkan";
    public string MenuShowImages => "Gambar";
    public string MenuShowShapes => "Bentuk";
    public string MenuShowDrawings => "Grafik";
    public string MenuShowShadows => "Bayangan";
    public string MenuShowText => "Teks";
    public string MenuTools => "&Alat";
    public string MenuSettings => "&Pengaturan…";
    public string MenuHelp => "&Bantuan";
    public string MenuManual => "&Manual Online…";
    public string MenuAbout => "&Tentang…";

    public string ManualUrl =>
        "https://github.com/Nakanokappei/pdf-image-remover-for-rag/blob/main/docs/manual.en.md";

    public string LinkOpenFailed =>
        "Tidak dapat membuka halaman. Silakan buka URL ini di browser Anda:\n";

    public string ToolOpen => "Buka PDF";
    public string ToolSave => "Hapus dan Simpan";
    public string ToolSelectAll => "Pilih Semua";
    public string ToolClearSelection => "Bersihkan Pilihan";

    public string ColumnThumbnail => "Miniatur";
    public string ColumnObjectId => "ID Objek";
    public string ColumnType => "Jenis";
    public string TypeImage => "Gambar";
    public string TypeText => "Teks";
    public string TypeShape => "Bentuk";
    public string TypeDrawing => "Grafik";
    public string TypeShadow => "Bayangan";
    public string ColumnSize => "Ukuran";
    public string ColumnUsageCount => "Penggunaan";
    public string ColumnCompression => "Kompresi";
    public string ColumnEstimatedSize => "Perk. Ukuran";
    public string ColumnWarning => "Peringatan";
    public string ColumnDelete => "Hapus";
    public string TextSize(int characterCount) => $"{characterCount} karakter";
    public string RowNumber(int number) => $"Baris {number}";

    public string StatusOpenPrompt => "Buka PDF untuk memulai";
    public string StatusAnalyzing => "Menganalisis PDF…";

    public string Cancel => "Batal";
    public string StatusCanceling => "Membatalkan…";
    public string StatusCanceled => "Pembukaan dibatalkan";

    public string ProgressReadingPages(string fileName, int page, int pageCount) =>
        $"{fileName} — menganalisis halaman {page} dari {pageCount}";

    public string ProgressThumbnails(string fileName, int page, int pageCount) =>
        $"{fileName} — membuat miniatur, halaman {page} dari {pageCount}";

    public string ProgressGrouping(string fileName) =>
        $"{fileName} — mengelompokkan objek";

    public string ThumbnailPending => "Membuat miniatur…";

    public string StatusAnalyzed => "Analisis selesai";
    public string StatusOpenFailed => "Tidak dapat membuka PDF";
    public string StatusSaving => "Menyimpan…";
    public string StatusSaveFailed => "Gagal menyimpan";

    public string StatusSaved(
        int fileCount, int drawCallsRemoved, int regionsFlattened, int imagesResized) =>
        $"{fileCount} file disimpan — {drawCallsRemoved} panggilan gambar dihapus, {regionsFlattened} area dijadikan gambar, {imagesResized} gambar diperkecil, verifikasi OK";

    public string StatusFlattening => "Menggabungkan…";

    public string StatusFlattened(int places) =>
        $"{places} tempat digabungkan — simpan untuk menulisnya";

    public string StatusFlattenUndone => "Penggabungan dibatalkan";

    public string StatusSelection(int selectedCount) =>
        $"{selectedCount} objek dipilih untuk dihapus";

    public string WarningNotRemovable => "Tidak dapat dihapus";

    public string WarningFullPage =>
        "Kemungkinan halaman hasil pindaian - menghapusnya membuat halaman menjadi kosong";

    public string TooltipUnsafe =>
        "Gambar ini tidak dapat dihapus dengan aman karena struktur PDF yang kompleks.";

    public string TooltipFullPage =>
        "Gambar ini mungkin mengisi seluruh halaman.\n" +
        "Menghapusnya dapat menghilangkan semua yang terlihat di halaman itu, termasuk isi utamanya.";

    public string OpenDialogTitle => "Buka PDF";
    public string PdfFileFilter => "File PDF (*.pdf)|*.pdf";
    public string SaveDialogTitle => "Hapus Pilihan dan Simpan";

    public string OutputFolderDescription =>
        "Pilih folder untuk PDF yang sudah dibersihkan. Setiap file disimpan sebagai \"<nama>_cleaned.pdf\".";

    public string SameAsSourceMessage =>
        "PDF hasil pembersihan tidak dapat menimpa file sumber. Pilih nama lain.";

    public string SameAsSourceTitle => "Lokasi Penyimpanan";
    public string ConfirmTitle => "Konfirmasi";

    public string ConfirmSaveBeforeOpen =>
        "Ada objek yang dipilih untuk dihapus. Simpan sebelum membuka file baru?\n" +
        "Memilih Tidak akan membuang pilihan saat ini.";

    public string ConfirmDiscardBeforeOpen =>
        "Tutup file yang sedang terbuka dan buka file baru?";

    public string ErrorDialogTitle => "Kesalahan";
    public string CopyDetails => "Salin Detail";
    public string AboutTitle => "Tentang PDF Image Remover for RAG";

    public string AboutDescription =>
        "Menghapus objek yang mengganggu proses pencarian — gambar logo, teks header dan " +
        "footer yang berulang, garis tabel — dari PDF sebelum masuk ke pipeline RAG Anda. " +
        "File asli Anda tidak pernah diubah, dan semua proses berjalan secara lokal di " +
        "komputer ini.";

    public string AboutAppLicense => "Dirilis di bawah Lisensi MIT.";
    public string AboutThirdPartyLicense => "Pustaka: PDFsharp (MIT), PdfPig (Apache-2.0)";
    public string AboutLicenseLink => "Informasi lisensi";
    public string ContextMenuUsageLocations => "Tampilkan &Lokasi Penggunaan…";

    public string GraphicsObjectsTitle => "Objek grafis";
    public string FlattenUnitMenu => "Perintah untuk unit ini";
    public string FlattenVisible => "Jadikan objek terlihat sebagai gambar";
    public string FlattenSelected => "Jadikan objek terpilih sebagai gambar";
    public string FlattenUndo => "Batalkan gambar";
    public string FlattenMerge => "Gabungkan unit";
    public string FlattenSplit => "Pisahkan objek terpilih";

    public string FlattenDescription =>
        "Menggabungkan objek grafis yang bertumpang tindih menjadi satu gambar. Halaman tetap "
        + "sama, tetapi teks di dalamnya tidak lagi berupa teks.";

    public string FlattenObjectNotOverlapping => "Objek grafis ini tidak bertumpang tindih dengan apa pun.";
    public string FlattenNoOverlaps => "Tidak ada objek grafis yang bertumpang tindih.";
    public string FlattenWholePageWarning =>
        "Objek yang dipilih menutupi hampir seluruh halaman. Setelah digabung, halaman ini menjadi satu gambar dan teksnya tidak lagi berupa teks.";
    public string StatusFlattenSelection(int objectCount) =>
        $"{objectCount} objek grafis dipilih";
    public string AccessibleFlattenPreview => "Pratinjau";

    public string Ok => "OK";
    public string SettingsTitle => "Pengaturan";
    public string SettingsImagesGroup => "Gambar dalam PDF yang disimpan";
    public string SettingsShrinkImages => "&Perkecil gambar";
    public string SettingsResolution => "&Resolusi:";
    public string SettingsJpegQuality => "&Kualitas:";
    public string SettingsSampleFigure => "Diagram";
    public string SettingsSamplePhoto => "Foto";
    public string SettingsSampleText => "Laporan uji TW3";
    public string SettingsZoom => "&Perbesaran:";

    public string SettingsPreviewLossless(string name, string size) =>
        $"{name} - {size}, disimpan tanpa kehilangan data. Kualitas tidak mengubahnya.";

    public string SettingsPreviewLossy(string name, string size) =>
        $"{name} - {size} pada kualitas ini";

    public string SettingsShrinkDescription =>
        "Gambar yang lebih besar dari resolusi yang dipilih digambar ulang lebih kecil. "
        + "Tampilan halaman tetap sama; hanya jumlah piksel di baliknya yang berubah. "
        + "Tidak ada yang diperbesar.";

    public string ResolutionScreen(int dpi) => $"Layar - {dpi} dpi";

    public string ResolutionRagLatin(int dpi) => $"Untuk RAG (Latin dan Kiril) - {dpi} dpi";

    public string ResolutionRagComplexScripts(int dpi) =>
        $"Untuk RAG (CJK dan aksara kompleks lainnya) - {dpi} dpi";

    public string ResolutionRagFinePrint(int dpi) =>
        $"Untuk RAG (dokumen dengan huruf kecil) - {dpi} dpi";

    public string ResolutionPrint(int dpi) => $"Cetak - {dpi} dpi";

    public ImageSizeLimit DefaultImageSizeLimit => ImageSizeLimit.RagLatin;

    public string ErrorSameAsSource =>
        "Tidak dapat menyimpan dengan menimpa PDF sumber. Pilih nama lain.";

    public string ErrorNoSelection =>
        "Tidak ada yang dipilih untuk dihapus atau dijadikan gambar.";

    public string VerificationCleanerSummary(int pagesModified, int drawCallsRemoved) =>
        $"(pembersih: {pagesModified} halaman, {drawCallsRemoved} panggilan gambar) ";

    public string VerificationMoreWarnings(int remaining) => $" dan {remaining} lainnya";

    public string ErrorVerificationFailedPrefix => "Verifikasi setelah penyimpanan gagal: ";

    public ErrorText NotAPdf => new(
        "File yang dipilih bukan PDF.",
        "Pilih file yang valid dengan ekstensi .pdf.");

    public ErrorText PdfCorrupted => new(
        "File PDF rusak atau formatnya tidak dapat dibaca.",
        "Periksa apakah aplikasi pembaca PDF lain dapat membukanya.");

    public ErrorText PdfEncrypted => new(
        "PDF ini terenkripsi.",
        "Versi ini tidak mendukung PDF yang dilindungi kata sandi. Hapus proteksinya lalu coba lagi.");

    public ErrorText PdfPasswordRequired => new(
        "Diperlukan kata sandi untuk membuka PDF ini.",
        "Versi ini tidak mendukung pemasukan kata sandi. Hapus proteksinya lalu coba lagi.");

    public ErrorText UnsupportedEncryption => new(
        "PDF ini memakai skema enkripsi yang tidak didukung.",
        "Tanyakan kepada pembuat dokumen tentang enkripsi yang dipakai.");

    public ErrorText ImageExtractionFailed => new(
        "Tidak dapat mengekstrak gambar dari PDF.",
        "Periksa apakah masalahnya berulang dengan PDF lain. Jika ya, salin detailnya dan laporkan.");

    public ErrorText ImageRemovalUnsafe => new(
        "Gambar ini tidak dapat dihapus dengan aman karena struktur PDF yang kompleks.",
        "Batalkan centang pada gambar tersebut lalu simpan lagi.");

    public ErrorText DestinationNotWritable => new(
        "Lokasi tujuan tidak dapat ditulisi.",
        "Pilih folder lain atau periksa izin tulisnya. PDF sumber tidak dapat ditimpa.");

    public ErrorText FileInUse => new(
        "File sedang dibuka di aplikasi lain.",
        "Tutup aplikasi yang memakai file itu, lalu coba lagi.");

    public ErrorText DiskFull => new(
        "Ruang kosong pada disk tidak mencukupi.",
        "Kosongkan ruang disk, lalu coba lagi.");

    public ErrorText PostSaveVerificationFailed => new(
        "Verifikasi setelah penyimpanan gagal, jadi PDF tidak disimpan.",
        "PDF sumber tidak berubah. Salin detailnya dan laporkan masalah ini.");

    public ErrorText UserCanceled => new("Operasi dibatalkan.", "");

    public ErrorText Unexpected => new(
        "Terjadi kesalahan yang tidak terduga.",
        "Salin detailnya dan laporkan kepada pengembang.");
}
