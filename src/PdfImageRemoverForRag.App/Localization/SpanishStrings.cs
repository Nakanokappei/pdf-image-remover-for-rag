namespace PdfImageRemoverForRag.App.Localization;

/// <summary>
/// Spanish (es) UI text. Neutral international Spanish, using the standard
/// Windows terminology (Archivo / Abrir / Guardar / Cancelar / Carpeta).
/// </summary>
internal sealed class SpanishStrings : IStrings
{
    // The product is listed under its English name on the Microsoft Store.
    public string AppTitle => "PDF Image Remover for RAG";

    // Access keys are distinct within each menu level: file A/B/G/C/S,
    // view V/T/S/M, help Y/M/A.
    public string MenuFile => "&Archivo";
    public string MenuOpen => "A&brir…";
    public string MenuSave => "Quitar seleccionados y &Guardar…";
    public string MenuCloseAll => "&Cerrar todo";
    public string MenuExit => "&Salir";
    public string MenuView => "&Ver";
    public string MenuTableView => "&Tabla";
    public string MenuTileView => "Mo&saico";
    public string MenuShownTypes => "Tipos &mostrados";
    public string MenuShowImages => "Imágenes";
    public string MenuShowShapes => "Formas";
    public string MenuShowText => "Texto";
    public string MenuHelp => "A&yuda";
    public string MenuManual => "&Manual en línea…";
    public string MenuAbout => "&Acerca de…";

    // Only Japanese and English manual pages exist, so Spanish points at English.
    public string ManualUrl =>
        "https://github.com/Nakanokappei/pdf-image-remover-for-rag/blob/main/docs/manual.en.md";

    public string LinkOpenFailed =>
        "No se pudo abrir la página. Abra esta dirección en su navegador:\n";

    public string ToolOpen => "Abrir PDF";
    public string ToolSave => "Quitar y guardar";
    public string ToolSelectAll => "Seleccionar todo";
    public string ToolClearSelection => "Borrar selección";

    // Column captions are deliberately shortened to fit narrow columns:
    // ColumnUsageCount ("Usos" instead of "Número de usos") and
    // ColumnEstimatedSize ("Tam. est." instead of "Tamaño estimado").
    public string ColumnThumbnail => "Miniatura";
    public string ColumnImageId => "ID de objeto";
    public string ColumnType => "Tipo";
    public string TypeImage => "Imagen";
    public string TypeText => "Texto";
    public string TypeShape => "Forma";
    public string ColumnSize => "Tamaño";
    public string ColumnUsageCount => "Usos";
    public string ColumnCompression => "Compresión";
    public string ColumnEstimatedSize => "Tam. est.";
    public string ColumnWarning => "Aviso";
    public string AccessibleDeleteColumn => "Quitar";
    public string TextSize(int characterCount) => $"{characterCount} caracteres";

    public string StatusOpenPrompt => "Abra un PDF para comenzar";
    public string StatusAnalyzing => "Analizando el PDF…";

    public string Cancel => "Cancelar";
    public string StatusCancelling => "Cancelando…";
    public string StatusCancelled => "Apertura cancelada";

    public string ProgressReadingPages(string fileName, int page, int pageCount) =>
        $"{fileName} — analizando la página {page} de {pageCount}";

    public string ProgressThumbnails(string fileName, int page, int pageCount) =>
        $"{fileName} — generando miniaturas, página {page} de {pageCount}";

    public string ProgressGrouping(string fileName) =>
        $"{fileName} — agrupando objetos";

    public string ThumbnailPending => "Generando miniatura…";

    public string StatusAnalyzed => "Análisis completado";
    public string StatusOpenFailed => "No se pudo abrir el PDF";
    public string StatusSaving => "Guardando…";
    public string StatusSaveFailed => "Error al guardar";

    public string StatusSaved(int fileCount, int drawCallsRemoved) =>
        $"Se guardaron {fileCount} archivo(s) — se quitaron {drawCallsRemoved} llamada(s) de dibujo, verificación correcta";

    public string StatusSelection(int selectedCount) =>
        $"{selectedCount} grupo(s) de imágenes seleccionado(s) para quitar";

    public string WarningNotRemovable => "No se puede quitar";

    public string WarningFullPage =>
        "Probablemente es una página escaneada: al quitarla, la página queda en blanco";

    public string TooltipUnsafe =>
        "Esta imagen no se puede quitar de forma segura debido a la estructura compleja del PDF.";

    public string TooltipFullPage =>
        "Es posible que esta imagen ocupe la página entera.\n" +
        "Al quitarla puede borrarse todo lo visible en esa página, incluido el contenido principal.";

    public string OpenDialogTitle => "Abrir PDF";
    public string PdfFileFilter => "Archivos PDF (*.pdf)|*.pdf";
    public string SaveDialogTitle => "Quitar seleccionados y guardar";

    public string OutputFolderDescription =>
        "Elija la carpeta para los PDF depurados. Cada archivo se guarda como \"<nombre>_cleaned.pdf\".";

    public string SameAsSourceMessage =>
        "El PDF depurado no puede sobrescribir el archivo de origen. Elija otro nombre.";

    public string SameAsSourceTitle => "Ubicación de guardado";
    public string ConfirmTitle => "Confirmar";

    public string ConfirmSaveBeforeOpen =>
        "Hay objetos seleccionados para quitar. ¿Desea guardar antes de abrir archivos nuevos?\n" +
        "Si elige No, se descarta la selección actual.";

    public string ConfirmDiscardBeforeOpen =>
        "¿Desea cerrar los archivos abiertos y abrir otros nuevos?";

    public string ErrorDialogTitle => "Error";
    public string CopyDetails => "Copiar detalles";
    public string AboutTitle => "Acerca de PDF Image Remover for RAG";

    public string AboutDescription =>
        "Quita de los PDF los objetos que estorban a la recuperación de información " +
        "—imágenes de logotipos, textos repetidos de encabezado y pie de página, líneas— " +
        "antes de que entren en su canalización RAG. Sus archivos originales nunca se " +
        "modifican y todo el proceso se ejecuta localmente en este equipo.";

    public string AboutAppLicense => "Publicado bajo la licencia MIT.";
    public string AboutThirdPartyLicense => "Bibliotecas: PDFsharp (MIT), PdfPig (Apache-2.0)";
    public string AboutLicenseLink => "Información de licencias";

    public string ErrorSameAsSource =>
        "No se puede guardar sobre el PDF de origen. Elija otro nombre.";

    public string ErrorNoSelection => "No hay imágenes seleccionadas para quitar.";

    public string VerificationCleanerSummary(int pagesModified, int drawCallsRemoved) =>
        $"(depurador: {pagesModified} páginas, {drawCallsRemoved} llamadas de dibujo) ";

    public string VerificationMoreWarnings(int remaining) => $" y {remaining} más";

    public string ErrorVerificationFailedPrefix => "Falló la verificación posterior al guardado: ";

    public ErrorText NotAPdf => new(
        "El archivo seleccionado no es un PDF.",
        "Elija un archivo válido con la extensión .pdf.");

    public ErrorText PdfCorrupted => new(
        "El archivo PDF está dañado o tiene un formato ilegible.",
        "Compruebe si otro visor de PDF puede abrirlo.");

    public ErrorText PdfEncrypted => new(
        "Este PDF está cifrado.",
        "Esta versión no admite PDF protegidos con contraseña. Quite la protección e inténtelo de nuevo.");

    public ErrorText PdfPasswordRequired => new(
        "Se necesita una contraseña para abrir este PDF.",
        "Esta versión no admite la escritura de contraseñas. Quite la protección e inténtelo de nuevo.");

    public ErrorText UnsupportedEncryption => new(
        "El PDF usa un esquema de cifrado no admitido.",
        "Consulte con quien generó el documento qué cifrado se utilizó.");

    public ErrorText ImageExtractionFailed => new(
        "No se pudieron extraer las imágenes del PDF.",
        "Compruebe si el problema se repite con otro PDF. Si es así, copie los detalles y notifíquelo.");

    public ErrorText ImageRemovalUnsafe => new(
        "Esta imagen no se puede quitar de forma segura debido a la estructura compleja del PDF.",
        "Desmarque la imagen afectada y guarde de nuevo.");

    public ErrorText DestinationNotWritable => new(
        "No se puede escribir en el destino.",
        "Elija otra carpeta o compruebe los permisos de escritura. El PDF de origen no se puede sobrescribir.");

    public ErrorText FileInUse => new(
        "El archivo está abierto en otra aplicación.",
        "Cierre la aplicación que usa el archivo e inténtelo de nuevo.");

    public ErrorText DiskFull => new(
        "No hay suficiente espacio libre en disco.",
        "Libere espacio en disco e inténtelo de nuevo.");

    public ErrorText PostSaveVerificationFailed => new(
        "Falló la verificación posterior al guardado, por lo que el PDF no se guardó.",
        "El PDF de origen no se modificó. Copie los detalles y notifique el problema.");

    public ErrorText UserCancelled => new("Se canceló la operación.", "");

    public ErrorText Unexpected => new(
        "Se produjo un error inesperado.",
        "Copie los detalles y envíelos al desarrollador.");
}
