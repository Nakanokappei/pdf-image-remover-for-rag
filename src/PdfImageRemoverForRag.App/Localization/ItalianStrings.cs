namespace PdfImageRemoverForRag.App.Localization;

/// <summary>
/// Italian (it) UI text. Access keys follow the Western convention — the "&amp;"
/// marks a letter inside the caption.
/// </summary>
internal sealed class ItalianStrings : IStrings
{
    public string AppTitle => "PDF Image Remover for RAG";

    public string MenuFile => "&File";
    public string MenuOpen => "&Apri…";
    public string MenuSave => "Rimuovi selezionati e &Salva…";
    public string MenuCloseAll => "&Chiudi tutto";
    public string MenuExit => "&Esci";
    public string MenuView => "&Visualizza";
    public string MenuTableView => "&Tabella";
    public string MenuTileView => "&Riquadri";
    public string MenuShownTypes => "Tipi &mostrati";
    public string MenuShowImages => "Immagini";
    public string MenuShowShapes => "Forme";
    public string MenuShowText => "Testo";
    public string MenuHelp => "&Guida";
    public string MenuManual => "&Manuale online…";
    public string MenuAbout => "&Informazioni su…";

    // Only Japanese and English manual pages exist, so Italian points at English.
    public string ManualUrl =>
        "https://github.com/Nakanokappei/pdf-image-remover-for-rag/blob/main/docs/manual.en.md";

    public string LinkOpenFailed =>
        "Impossibile aprire la pagina. Aprire questo URL nel browser:\n";

    public string ToolOpen => "Apri PDF";
    public string ToolSave => "Rimuovi e salva";
    public string ToolSelectAll => "Seleziona tutto";
    public string ToolClearSelection => "Annulla selezione";

    // Column captions sit in narrow columns: "Dim.", "Compress." and
    // "Dim. stim." are deliberately abbreviated.
    public string ColumnThumbnail => "Anteprima";
    public string ColumnImageId => "ID oggetto";
    public string ColumnType => "Tipo";
    public string TypeImage => "Immagine";
    public string TypeText => "Testo";
    public string TypeShape => "Forma";
    public string ColumnSize => "Dim.";
    public string ColumnUsageCount => "Utilizzi";
    public string ColumnCompression => "Compress.";
    public string ColumnEstimatedSize => "Dim. stim.";
    public string ColumnWarning => "Avviso";
    public string AccessibleDeleteColumn => "Rimuovi";
    public string TextSize(int characterCount) => $"{characterCount} caratteri";

    public string StatusOpenPrompt => "Aprire un PDF per iniziare";
    public string StatusAnalyzing => "Analisi del PDF in corso…";

    public string Cancel => "Annulla";
    public string StatusCancelling => "Annullamento…";
    public string StatusCancelled => "Apertura annullata";

    public string ProgressReadingPages(string fileName, int page, int pageCount) =>
        $"{fileName} — analisi della pagina {page} di {pageCount}";

    public string ProgressThumbnails(string fileName, int page, int pageCount) =>
        $"{fileName} — creazione delle anteprime, pagina {page} di {pageCount}";

    public string ProgressGrouping(string fileName) =>
        $"{fileName} — raggruppamento degli oggetti";

    public string ThumbnailPending => "Creazione anteprima…";

    public string StatusAnalyzed => "Analisi completata";
    public string StatusOpenFailed => "Impossibile aprire il PDF";
    public string StatusSaving => "Salvataggio…";
    public string StatusSaveFailed => "Salvataggio non riuscito";

    public string StatusSaved(int fileCount, int drawCallsRemoved) =>
        $"Salvati {fileCount} file — {drawCallsRemoved} chiamate di disegno rimosse, verifica OK";

    public string StatusSelection(int selectedCount) =>
        $"{selectedCount} gruppi di immagini selezionati per la rimozione";

    public string WarningNotRemovable => "Non rimovibile";

    public string WarningFullPage =>
        "Probabilmente una pagina scansionata - rimuovendola la pagina resta vuota";

    public string TooltipUnsafe =>
        "Questa immagine non può essere rimossa in modo sicuro a causa della struttura complessa del PDF.";

    public string TooltipFullPage =>
        "Questa immagine potrebbe occupare l'intera pagina.\n" +
        "Rimuovendola si rischia di cancellare tutto il contenuto visibile della pagina, testo compreso.";

    public string OpenDialogTitle => "Apri PDF";
    public string PdfFileFilter => "File PDF (*.pdf)|*.pdf";
    public string SaveDialogTitle => "Rimuovi selezionati e salva";

    public string OutputFolderDescription =>
        "Scegliere la cartella per i PDF ripuliti. Ogni file viene salvato come \"<nome>_cleaned.pdf\".";

    public string SameAsSourceMessage =>
        "Il PDF ripulito non può sovrascrivere il file di origine. Scegliere un nome diverso.";

    public string SameAsSourceTitle => "Percorso di salvataggio";
    public string ConfirmTitle => "Conferma";

    public string ConfirmSaveBeforeOpen =>
        "Sono presenti oggetti selezionati per la rimozione. Salvare prima di aprire nuovi file?\n" +
        "Scegliendo No la selezione corrente viene annullata.";

    public string ConfirmDiscardBeforeOpen =>
        "Chiudere i file attualmente aperti e aprirne di nuovi?";

    public string ErrorDialogTitle => "Errore";
    public string CopyDetails => "Copia dettagli";
    public string AboutTitle => "Informazioni su PDF Image Remover for RAG";

    public string AboutDescription =>
        "Rimuove dai PDF gli oggetti che ostacolano il recupero delle informazioni — immagini " +
        "di logo, testo ripetuto di intestazioni e piè di pagina, righe di separazione — prima " +
        "che entrino nella pipeline RAG. I file originali non vengono mai modificati e tutte le " +
        "elaborazioni avvengono in locale su questo PC.";

    public string AboutAppLicense => "Distribuito con licenza MIT.";
    public string AboutThirdPartyLicense => "Librerie: PDFsharp (MIT), PdfPig (Apache-2.0)";
    public string AboutLicenseLink => "Informazioni sulle licenze";

    public string ErrorSameAsSource =>
        "Impossibile sovrascrivere il PDF di origine. Scegliere un nome diverso.";

    public string ErrorNoSelection => "Nessuna immagine selezionata per la rimozione.";

    public string VerificationCleanerSummary(int pagesModified, int drawCallsRemoved) =>
        $"(pulizia: {pagesModified} pagine, {drawCallsRemoved} chiamate di disegno) ";

    public string VerificationMoreWarnings(int remaining) => $" e altri {remaining}";

    public string ErrorVerificationFailedPrefix => "Verifica successiva al salvataggio non riuscita: ";

    public ErrorText NotAPdf => new(
        "Il file selezionato non è un PDF.",
        "Scegliere un file valido con estensione .pdf.");

    public ErrorText PdfCorrupted => new(
        "Il file PDF è danneggiato o in un formato illeggibile.",
        "Verificare se un altro visualizzatore di PDF riesce ad aprirlo.");

    public ErrorText PdfEncrypted => new(
        "Questo PDF è crittografato.",
        "Questa versione non supporta i PDF protetti da password. Rimuovere la protezione e riprovare.");

    public ErrorText PdfPasswordRequired => new(
        "Per aprire questo PDF è necessaria una password.",
        "Questa versione non consente di immettere password. Rimuovere la protezione e riprovare.");

    public ErrorText UnsupportedEncryption => new(
        "Il PDF utilizza uno schema di crittografia non supportato.",
        "Rivolgersi a chi ha prodotto il documento per informazioni sulla crittografia utilizzata.");

    public ErrorText ImageExtractionFailed => new(
        "Impossibile estrarre le immagini dal PDF.",
        "Verificare se il problema si ripete con un altro PDF. In tal caso, copiare i dettagli e segnalarlo.");

    public ErrorText ImageRemovalUnsafe => new(
        "Questa immagine non può essere rimossa in modo sicuro a causa della struttura complessa del PDF.",
        "Deselezionare l'immagine interessata e salvare di nuovo.");

    public ErrorText DestinationNotWritable => new(
        "La destinazione non è scrivibile.",
        "Scegliere un'altra cartella o verificare le autorizzazioni di scrittura. Il PDF di origine non può essere sovrascritto.");

    public ErrorText FileInUse => new(
        "Il file è aperto in un'altra applicazione.",
        "Chiudere l'applicazione che utilizza il file e riprovare.");

    public ErrorText DiskFull => new(
        "Spazio su disco insufficiente.",
        "Liberare spazio su disco e riprovare.");

    public ErrorText PostSaveVerificationFailed => new(
        "La verifica successiva al salvataggio non è riuscita, quindi il PDF non è stato salvato.",
        "Il PDF di origine è invariato. Copiare i dettagli e segnalare il problema.");

    public ErrorText UserCancelled => new("L'operazione è stata annullata.", "");

    public ErrorText Unexpected => new(
        "Si è verificato un errore imprevisto.",
        "Copiare i dettagli e inviarli allo sviluppatore.");
}
