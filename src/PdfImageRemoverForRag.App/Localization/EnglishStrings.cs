namespace PdfImageRemoverForRag.App.Localization;

/// <summary>
/// English (en) UI text. Also the fallback for any display language the app
/// does not translate, so this implementation must never be removed.
/// </summary>
internal sealed class EnglishStrings : IStrings
{
    public string AppTitle => "PDF Image Remover for RAG";

    public string MenuFile => "&File";
    public string MenuOpen => "&Open…";
    public string MenuSave => "Remove Selected && &Save…";
    public string MenuCloseAll => "&Close All";
    public string MenuExit => "E&xit";
    public string MenuView => "&View";
    public string MenuTableView => "&Table";
    public string MenuTileView => "T&iles";
    public string MenuShownTypes => "&Shown Types";
    public string MenuShowImages => "Images";
    public string MenuShowShapes => "Shapes";
    public string MenuShowDrawings => "Drawings";
    public string MenuShowShadows => "Shadows";
    public string MenuShowText => "Text";
    public string MenuHelp => "&Help";
    public string MenuManual => "Online &Manual…";
    public string MenuAbout => "&About…";

    public string ManualUrl =>
        "https://github.com/Nakanokappei/pdf-image-remover-for-rag/blob/main/docs/manual.en.md";

    public string LinkOpenFailed =>
        "Could not open the page. Please open this URL in your browser:\n";

    public string ToolOpen => "Open PDF";
    public string ToolSave => "Remove && Save";
    public string ToolSelectAll => "Select All";
    public string ToolClearSelection => "Clear Selection";

    public string ColumnThumbnail => "Thumbnail";
    public string ColumnObjectId => "Object ID";
    public string ColumnType => "Type";
    public string TypeImage => "Image";
    public string TypeText => "Text";
    public string TypeShape => "Shape";
    public string TypeDrawing => "Drawing";
    public string TypeShadow => "Shadow";
    public string ColumnSize => "Size";
    public string ColumnUsageCount => "Usage";
    public string ColumnCompression => "Compression";
    public string ColumnEstimatedSize => "Est. Size";
    public string ColumnWarning => "Warning";
    public string ColumnDelete => "Remove";
    public string TextSize(int characterCount) => $"{characterCount} chars";
    public string RowNumber(int number) => $"Row {number}";

    public string StatusOpenPrompt => "Open a PDF to begin";
    public string StatusAnalyzing => "Analyzing PDF…";

    public string Cancel => "Cancel";
    public string StatusCancelling => "Cancelling…";
    public string StatusCancelled => "Opening cancelled";

    public string ProgressReadingPages(string fileName, int page, int pageCount) =>
        $"{fileName} — analyzing page {page} of {pageCount}";

    public string ProgressThumbnails(string fileName, int page, int pageCount) =>
        $"{fileName} — building thumbnails, page {page} of {pageCount}";

    public string ProgressGrouping(string fileName) =>
        $"{fileName} — grouping objects";

    public string ThumbnailPending => "Building thumbnail…";

    public string StatusAnalyzed => "Analysis complete";
    public string StatusOpenFailed => "Could not open the PDF";
    public string StatusSaving => "Saving…";
    public string StatusSaveFailed => "Save failed";

    public string StatusSaved(int fileCount, int drawCallsRemoved, int regionsFlattened) =>
        $"Saved {fileCount} file(s) — {drawCallsRemoved} draw call(s) removed, {regionsFlattened} region(s) flattened, verification OK";

    public string StatusFlattening => "Flattening…";

    public string StatusFlattened(int places) =>
        $"{places} place(s) flattened — save to write them out";

    public string StatusFlattenUndone => "Flatten undone";

    public string StatusSelection(int selectedCount) =>
        $"{selectedCount} object(s) selected for removal";

    public string WarningNotRemovable => "Not removable";

    public string WarningFullPage =>
        "Probably a scanned page - removing it leaves the page blank";

    public string TooltipUnsafe =>
        "This image cannot be removed safely because of the PDF's complex structure.";

    public string TooltipFullPage =>
        "This image may make up the entire page.\n" +
        "Removing it can erase everything visible on that page, including the body content.";

    public string OpenDialogTitle => "Open PDF";
    public string PdfFileFilter => "PDF files (*.pdf)|*.pdf";
    public string SaveDialogTitle => "Remove Selected & Save";

    public string OutputFolderDescription =>
        "Choose the folder for the cleaned PDFs. Each file is saved as \"<name>_cleaned.pdf\".";

    public string SameAsSourceMessage =>
        "The cleaned PDF cannot overwrite the source file. Choose a different name.";

    public string SameAsSourceTitle => "Save Location";
    public string ConfirmTitle => "Confirm";

    public string ConfirmSaveBeforeOpen =>
        "You have objects selected for removal. Save before opening new files?\n" +
        "Choosing No discards the current selection.";

    public string ConfirmDiscardBeforeOpen =>
        "Close the currently open files and open new ones?";

    public string ErrorDialogTitle => "Error";
    public string CopyDetails => "Copy Details";
    public string AboutTitle => "About PDF Image Remover for RAG";

    public string AboutDescription =>
        "Removes the objects that get in the way of retrieval — logo images, repeated " +
        "header and footer text, ruling lines — from PDFs before they enter your RAG " +
        "pipeline. Your original files are never modified, and everything runs locally " +
        "on this PC.";

    public string AboutAppLicense => "Released under the MIT License.";
    public string AboutThirdPartyLicense => "Libraries: PDFsharp (MIT), PdfPig (Apache-2.0)";
    public string AboutLicenseLink => "License information";
    public string ContextMenuUsageLocations => "Show &Usage Locations…";

    public string FlattenPanelTitle => "Graphics objects";
    public string FlattenMenu => "Graphics object commands";
    public string FlattenUnitMenu => "Commands for this unit";
    public string FlattenVisible => "Turn the visible objects into a picture";
    public string FlattenSelected => "Turn the selected objects into a picture";
    public string FlattenUndo => "Undo the picture";
    public string FlattenMerge => "Merge the selected units";
    public string FlattenSplit => "Split the selected objects off";

    public string FlattenDescription =>
        "Bakes overlapping graphics objects into a single image. The page looks the same, "
        + "but the text inside is no longer text.";

    public string FlattenObjectNotOverlapping => "This graphics object does not overlap anything.";
    public string FlattenNoOverlaps => "No overlapping graphics objects were found.";
    public string FlattenWholePageWarning =>
        "The ticked objects cover almost the whole page. Flattening them turns the page into a single image, and none of its text stays text.";
    public string StatusFlattenSelection(int objectCount) =>
        $"{objectCount} graphics object(s) selected";
    public string AccessibleFlattenPreview => "Preview";

    public string ErrorSameAsSource =>
        "Cannot save over the source PDF. Choose a different name.";

    public string ErrorNoSelection =>
        "Nothing is selected to remove or flatten.";

    public string VerificationCleanerSummary(int pagesModified, int drawCallsRemoved) =>
        $"(cleaner: {pagesModified} pages, {drawCallsRemoved} draw calls) ";

    public string VerificationMoreWarnings(int remaining) => $" and {remaining} more";

    public string ErrorVerificationFailedPrefix => "Post-save verification failed: ";

    public ErrorText NotAPdf => new(
        "The selected file is not a PDF.",
        "Choose a valid file with a .pdf extension.");

    public ErrorText PdfCorrupted => new(
        "The PDF file is corrupted or in an unreadable format.",
        "Check whether another PDF viewer can open it.");

    public ErrorText PdfEncrypted => new(
        "This PDF is encrypted.",
        "This version does not support password-protected PDFs. Remove the protection and try again.");

    public ErrorText PdfPasswordRequired => new(
        "A password is required to open this PDF.",
        "This version does not support entering passwords. Remove the protection and try again.");

    public ErrorText UnsupportedEncryption => new(
        "The PDF uses an unsupported encryption scheme.",
        "Ask the document's producer about the encryption used.");

    public ErrorText ImageExtractionFailed => new(
        "Could not extract the images from the PDF.",
        "Check whether the problem reproduces with another PDF. If it does, copy the details and report it.");

    public ErrorText ImageRemovalUnsafe => new(
        "This image cannot be removed safely because of the PDF's complex structure.",
        "Uncheck the affected image and save again.");

    public ErrorText DestinationNotWritable => new(
        "The destination is not writable.",
        "Choose another folder or check the write permission. The source PDF cannot be overwritten.");

    public ErrorText FileInUse => new(
        "The file is open in another application.",
        "Close the application using the file, then try again.");

    public ErrorText DiskFull => new(
        "There is not enough free disk space.",
        "Free up disk space, then try again.");

    public ErrorText PostSaveVerificationFailed => new(
        "Post-save verification failed, so the PDF was not saved.",
        "The source PDF is unchanged. Copy the details and report the problem.");

    public ErrorText UserCancelled => new("The operation was cancelled.", "");

    public ErrorText Unexpected => new(
        "An unexpected error occurred.",
        "Copy the details and report them to the developer.");
}
