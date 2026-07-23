namespace PdfImageRemoverForRag.App.Localization;

/// <summary>
/// German (de) UI text. Translated from <see cref="EnglishStrings"/>; the
/// English implementation stays the fallback for every untranslated language.
/// </summary>
internal sealed class GermanStrings : IStrings
{
    public string AppTitle => "PDF Image Remover for RAG";

    public string MenuFile => "&Datei";
    public string MenuOpen => "&Öffnen…";
    public string MenuSave => "Auswahl entfernen und &speichern…";
    public string MenuCloseAll => "Alle schl&ießen";
    public string MenuExit => "&Beenden";
    public string MenuView => "&Ansicht";
    public string MenuTableView => "&Tabelle";
    public string MenuTileView => "&Kacheln";
    public string MenuShownTypes => "Angezeigte T&ypen";
    public string MenuShowImages => "Bilder";
    public string MenuShowShapes => "Formen";
    public string MenuShowText => "Text";
    public string MenuHelp => "&Hilfe";
    public string MenuManual => "Online-Han&dbuch…";
    public string MenuAbout => "&Info…";

    // Only Japanese and English manual pages exist, so German points at English.
    public string ManualUrl =>
        "https://github.com/Nakanokappei/pdf-image-remover-for-rag/blob/main/docs/manual.en.md";

    public string LinkOpenFailed =>
        "Die Seite konnte nicht geöffnet werden. Bitte öffnen Sie diese URL in Ihrem Browser:\n";

    public string ToolOpen => "PDF öffnen";
    public string ToolSave => "Entfernen und speichern";
    public string ToolSelectAll => "Alle auswählen";
    public string ToolClearSelection => "Auswahl aufheben";

    // Column headers are deliberately short/abbreviated: the German compounds
    // ("Verwendungsanzahl", "Komprimierungsverfahren", "Geschätzte Größe") do
    // not fit the table. Shortened: Anzahl, Kompr., Gesch. Größe.
    public string ColumnThumbnail => "Vorschau";
    public string ColumnImageId => "Objekt-ID";
    public string ColumnType => "Typ";
    public string TypeImage => "Bild";
    public string TypeText => "Text";
    public string TypeShape => "Form";
    public string ColumnSize => "Größe";
    public string ColumnUsageCount => "Anzahl";
    public string ColumnCompression => "Kompr.";
    public string ColumnEstimatedSize => "Gesch. Größe";
    public string ColumnWarning => "Warnung";
    public string AccessibleDeleteColumn => "Entfernen";
    public string TextSize(int characterCount) => $"{characterCount} Zeichen";

    public string StatusOpenPrompt => "Öffnen Sie eine PDF-Datei, um zu beginnen";
    public string StatusAnalyzing => "PDF wird analysiert…";

    public string Cancel => "Abbrechen";
    public string StatusCancelling => "Wird abgebrochen…";
    public string StatusCancelled => "Öffnen abgebrochen";

    public string ProgressReadingPages(string fileName, int page, int pageCount) =>
        $"{fileName} — Seite {page} von {pageCount} wird analysiert";

    public string ProgressThumbnails(string fileName, int page, int pageCount) =>
        $"{fileName} — Vorschaubilder werden erstellt, Seite {page} von {pageCount}";

    public string ProgressGrouping(string fileName) =>
        $"{fileName} — Objekte werden gruppiert";

    public string ThumbnailPending => "Vorschau wird erstellt…";

    public string StatusAnalyzed => "Analyse abgeschlossen";
    public string StatusOpenFailed => "Die PDF-Datei konnte nicht geöffnet werden";
    public string StatusSaving => "Wird gespeichert…";
    public string StatusSaveFailed => "Speichern fehlgeschlagen";

    public string StatusSaved(int fileCount, int drawCallsRemoved) =>
        $"{fileCount} Datei(en) gespeichert — {drawCallsRemoved} Zeichenbefehl(e) entfernt, Prüfung OK";

    public string StatusSelection(int selectedCount) =>
        $"{selectedCount} Bildgruppe(n) zum Entfernen ausgewählt";

    public string WarningNotRemovable => "Nicht entfernbar";

    public string WarningFullPage =>
        "Vermutlich eine gescannte Seite - nach dem Entfernen bleibt die Seite leer";

    public string TooltipUnsafe =>
        "Dieses Bild kann wegen der komplexen Struktur der PDF-Datei nicht sicher entfernt werden.";

    public string TooltipFullPage =>
        "Dieses Bild füllt möglicherweise die gesamte Seite aus.\n" +
        "Beim Entfernen kann alles Sichtbare auf dieser Seite verloren gehen, auch der Fließtext.";

    public string OpenDialogTitle => "PDF öffnen";
    public string PdfFileFilter => "PDF-Dateien (*.pdf)|*.pdf";
    public string SaveDialogTitle => "Auswahl entfernen und speichern";

    public string OutputFolderDescription =>
        "Wählen Sie den Ordner für die bereinigten PDF-Dateien. Jede Datei wird als \"<Name>_cleaned.pdf\" gespeichert.";

    public string SameAsSourceMessage =>
        "Die bereinigte PDF-Datei kann die Quelldatei nicht überschreiben. Wählen Sie einen anderen Namen.";

    public string SameAsSourceTitle => "Speicherort";
    public string ConfirmTitle => "Bestätigen";

    public string ConfirmSaveBeforeOpen =>
        "Es sind Objekte zum Entfernen ausgewählt. Vor dem Öffnen neuer Dateien speichern?\n" +
        "Mit Nein wird die aktuelle Auswahl verworfen.";

    public string ConfirmDiscardBeforeOpen =>
        "Die aktuell geöffneten Dateien schließen und neue öffnen?";

    public string ErrorDialogTitle => "Fehler";
    public string CopyDetails => "Details kopieren";
    public string AboutTitle => "Info über PDF Image Remover for RAG";

    public string AboutDescription =>
        "Entfernt die Objekte, die beim Retrieval stören — Logobilder, wiederholte " +
        "Kopf- und Fußzeilentexte, Trennlinien — aus PDF-Dateien, bevor diese in Ihre " +
        "RAG-Pipeline gelangen. Ihre Originaldateien werden nie verändert, und alles " +
        "läuft lokal auf diesem PC.";

    public string AboutAppLicense => "Veröffentlicht unter der MIT-Lizenz.";
    public string AboutThirdPartyLicense => "Bibliotheken: PDFsharp (MIT), PdfPig (Apache-2.0)";
    public string AboutLicenseLink => "Lizenzinformationen";

    public string ErrorSameAsSource =>
        "Die Quell-PDF kann nicht überschrieben werden. Wählen Sie einen anderen Namen.";

    public string ErrorNoSelection => "Es sind keine Bilder zum Entfernen ausgewählt.";

    public string VerificationCleanerSummary(int pagesModified, int drawCallsRemoved) =>
        $"(Bereinigung: {pagesModified} Seiten, {drawCallsRemoved} Zeichenbefehle) ";

    public string VerificationMoreWarnings(int remaining) => $" und {remaining} weitere";

    public string ErrorVerificationFailedPrefix => "Prüfung nach dem Speichern fehlgeschlagen: ";

    public ErrorText NotAPdf => new(
        "Die ausgewählte Datei ist keine PDF-Datei.",
        "Wählen Sie eine gültige Datei mit der Erweiterung .pdf.");

    public ErrorText PdfCorrupted => new(
        "Die PDF-Datei ist beschädigt oder liegt in einem unlesbaren Format vor.",
        "Prüfen Sie, ob ein anderer PDF-Betrachter sie öffnen kann.");

    public ErrorText PdfEncrypted => new(
        "Diese PDF-Datei ist verschlüsselt.",
        "Diese Version unterstützt keine kennwortgeschützten PDF-Dateien. Heben Sie den Schutz auf und versuchen Sie es erneut.");

    public ErrorText PdfPasswordRequired => new(
        "Zum Öffnen dieser PDF-Datei ist ein Kennwort erforderlich.",
        "Diese Version unterstützt keine Kennworteingabe. Heben Sie den Schutz auf und versuchen Sie es erneut.");

    public ErrorText UnsupportedEncryption => new(
        "Die PDF-Datei verwendet ein nicht unterstütztes Verschlüsselungsverfahren.",
        "Fragen Sie beim Ersteller des Dokuments nach der verwendeten Verschlüsselung.");

    public ErrorText ImageExtractionFailed => new(
        "Die Bilder konnten nicht aus der PDF-Datei extrahiert werden.",
        "Prüfen Sie, ob das Problem auch bei einer anderen PDF-Datei auftritt. Wenn ja, kopieren Sie die Details und melden Sie es.");

    public ErrorText ImageRemovalUnsafe => new(
        "Dieses Bild kann wegen der komplexen Struktur der PDF-Datei nicht sicher entfernt werden.",
        "Heben Sie die Auswahl des betroffenen Bildes auf und speichern Sie erneut.");

    public ErrorText DestinationNotWritable => new(
        "Das Ziel ist nicht beschreibbar.",
        "Wählen Sie einen anderen Ordner oder prüfen Sie die Schreibberechtigung. Die Quell-PDF kann nicht überschrieben werden.");

    public ErrorText FileInUse => new(
        "Die Datei ist in einer anderen Anwendung geöffnet.",
        "Schließen Sie die Anwendung, die die Datei verwendet, und versuchen Sie es erneut.");

    public ErrorText DiskFull => new(
        "Es ist nicht genügend freier Speicherplatz vorhanden.",
        "Geben Sie Speicherplatz frei und versuchen Sie es erneut.");

    public ErrorText PostSaveVerificationFailed => new(
        "Die Prüfung nach dem Speichern ist fehlgeschlagen, daher wurde die PDF-Datei nicht gespeichert.",
        "Die Quell-PDF ist unverändert. Kopieren Sie die Details und melden Sie das Problem.");

    public ErrorText UserCancelled => new("Der Vorgang wurde abgebrochen.", "");

    public ErrorText Unexpected => new(
        "Ein unerwarteter Fehler ist aufgetreten.",
        "Kopieren Sie die Details und melden Sie sie dem Entwickler.");
}
