namespace PdfImageRemoverForRag.App.Localization;

/// <summary>
/// Russian (ru) UI text. Translated from <see cref="EnglishStrings"/>, whose
/// member order and layout it mirrors exactly.
///
/// Counted strings are phrased as "noun in genitive plural: N" (for example
/// "Файлов сохранено: 3") instead of "N noun". Russian picks one of three
/// plural forms from the number, and that pattern reads correctly for every
/// number, so no pluralization helper is needed.
/// </summary>
internal sealed class RussianStrings : IStrings
{
    public string AppTitle => "PDF Image Remover for RAG";

    public string MenuFile => "&Файл";
    public string MenuOpen => "&Открыть…";
    public string MenuSave => "Удалить выбранное и &сохранить…";
    public string MenuCloseAll => "&Закрыть все";
    public string MenuExit => "В&ыход";
    public string MenuView => "&Вид";
    public string MenuTableView => "&Таблица";
    public string MenuTileView => "&Плитки";
    public string MenuShownTypes => "&Отображаемые типы";
    public string MenuShowImages => "Изображения";
    public string MenuShowShapes => "Фигуры";
    public string MenuShowText => "Текст";
    public string MenuHelp => "&Справка";
    public string MenuManual => "Онлайн-&руководство…";
    public string MenuAbout => "&О программе…";

    // Only Japanese and English manual pages exist, so Russian points at English.
    public string ManualUrl =>
        "https://github.com/Nakanokappei/pdf-image-remover-for-rag/blob/main/docs/manual.en.md";

    public string LinkOpenFailed =>
        "Не удалось открыть страницу. Откройте этот адрес в браузере:\n";

    public string ToolOpen => "Открыть PDF";
    public string ToolSave => "Удалить и сохранить";
    public string ToolSelectAll => "Выделить все";
    public string ToolClearSelection => "Снять выделение";

    // Column captions are deliberately abbreviated — Russian words are far longer
    // than the English originals and these sit in narrow table columns.
    // Shortened: ColumnUsageCount ("Использований" -> "Исп."),
    // ColumnEstimatedSize ("Оценочный размер" -> "Прибл. размер"),
    // ColumnWarning ("Предупреждение" -> "Предупр.").
    public string ColumnThumbnail => "Эскиз";
    public string ColumnImageId => "ID объекта";
    public string ColumnType => "Тип";
    public string TypeImage => "Изображение";
    public string TypeText => "Текст";
    public string TypeShape => "Фигура";
    public string ColumnSize => "Размер";
    public string ColumnUsageCount => "Исп.";
    public string ColumnCompression => "Сжатие";
    public string ColumnEstimatedSize => "Прибл. размер";
    public string ColumnWarning => "Предупр.";
    public string AccessibleDeleteColumn => "Удалить";
    public string TextSize(int characterCount) => $"{characterCount} симв.";

    public string StatusOpenPrompt => "Откройте PDF, чтобы начать";
    public string StatusAnalyzing => "Анализ PDF…";

    public string Cancel => "Отмена";
    public string StatusCancelling => "Отмена…";
    public string StatusCancelled => "Открытие отменено";

    public string ProgressReadingPages(string fileName, int page, int pageCount) =>
        $"{fileName} — анализ страницы {page} из {pageCount}";

    public string ProgressThumbnails(string fileName, int page, int pageCount) =>
        $"{fileName} — создание эскизов, страница {page} из {pageCount}";

    public string ProgressGrouping(string fileName) =>
        $"{fileName} — группировка объектов";

    public string ThumbnailPending => "Создание эскиза…";

    public string StatusAnalyzed => "Анализ завершён";
    public string StatusOpenFailed => "Не удалось открыть PDF";
    public string StatusSaving => "Сохранение…";
    public string StatusSaveFailed => "Не удалось сохранить";

    public string StatusSaved(int fileCount, int drawCallsRemoved) =>
        $"Файлов сохранено: {fileCount} — удалено фрагментов отрисовки: {drawCallsRemoved}, проверка пройдена";

    public string StatusSelection(int selectedCount) =>
        $"Выбрано объектов для удаления: {selectedCount}";

    public string WarningNotRemovable => "Нельзя удалить";

    public string WarningFullPage =>
        "Вероятно, отсканированная страница - после удаления страница останется пустой";

    public string TooltipUnsafe =>
        "Это изображение нельзя удалить безопасно из-за сложной структуры PDF.";

    public string TooltipFullPage =>
        "Возможно, это изображение занимает всю страницу.\n" +
        "Его удаление может стереть всё видимое содержимое этой страницы, включая основной текст.";

    public string OpenDialogTitle => "Открыть PDF";
    public string PdfFileFilter => "Файлы PDF (*.pdf)|*.pdf";
    public string SaveDialogTitle => "Удалить выбранное и сохранить";

    public string OutputFolderDescription =>
        "Выберите папку для очищенных файлов PDF. Каждый файл сохраняется как \"<имя>_cleaned.pdf\".";

    public string SameAsSourceMessage =>
        "Очищенный PDF не может заменить исходный файл. Выберите другое имя.";

    public string SameAsSourceTitle => "Место сохранения";
    public string ConfirmTitle => "Подтверждение";

    public string ConfirmSaveBeforeOpen =>
        "Есть объекты, выбранные для удаления. Сохранить перед открытием новых файлов?\n" +
        "Если выбрать «Нет», текущее выделение будет потеряно.";

    public string ConfirmDiscardBeforeOpen =>
        "Закрыть открытые файлы и открыть новые?";

    public string ErrorDialogTitle => "Ошибка";
    public string CopyDetails => "Копировать сведения";
    public string AboutTitle => "О программе PDF Image Remover for RAG";

    public string AboutDescription =>
        "Удаляет из файлов PDF объекты, которые мешают поиску, — изображения логотипов, " +
        "повторяющийся текст колонтитулов, линейки — до того, как документы попадут в ваш " +
        "конвейер RAG. Исходные файлы никогда не изменяются, и вся обработка выполняется " +
        "локально на этом компьютере.";

    public string AboutAppLicense => "Распространяется по лицензии MIT.";
    public string AboutThirdPartyLicense => "Библиотеки: PDFsharp (MIT), PdfPig (Apache-2.0)";
    public string AboutLicenseLink => "Сведения о лицензиях";

    public string ErrorSameAsSource =>
        "Нельзя сохранить поверх исходного PDF. Выберите другое имя.";

    public string ErrorNoSelection => "Не выбрано ни одного изображения для удаления.";

    public string VerificationCleanerSummary(int pagesModified, int drawCallsRemoved) =>
        $"(очистка — страниц: {pagesModified}, фрагментов отрисовки: {drawCallsRemoved}) ";

    public string VerificationMoreWarnings(int remaining) => $" и ещё: {remaining}";

    public string ErrorVerificationFailedPrefix => "Проверка после сохранения не пройдена: ";

    public ErrorText NotAPdf => new(
        "Выбранный файл не является файлом PDF.",
        "Выберите корректный файл с расширением .pdf.");

    public ErrorText PdfCorrupted => new(
        "Файл PDF повреждён или имеет нечитаемый формат.",
        "Проверьте, открывается ли он в другой программе для просмотра PDF.");

    public ErrorText PdfEncrypted => new(
        "Этот файл PDF зашифрован.",
        "Эта версия не поддерживает файлы PDF, защищённые паролем. Снимите защиту и повторите попытку.");

    public ErrorText PdfPasswordRequired => new(
        "Для открытия этого файла PDF требуется пароль.",
        "Эта версия не поддерживает ввод паролей. Снимите защиту и повторите попытку.");

    public ErrorText UnsupportedEncryption => new(
        "В файле PDF используется неподдерживаемая схема шифрования.",
        "Уточните у создателя документа, какое шифрование применено.");

    public ErrorText ImageExtractionFailed => new(
        "Не удалось извлечь изображения из файла PDF.",
        "Проверьте, повторяется ли проблема с другим файлом PDF. Если да, скопируйте сведения и сообщите о проблеме.");

    public ErrorText ImageRemovalUnsafe => new(
        "Это изображение нельзя удалить безопасно из-за сложной структуры PDF.",
        "Снимите флажок с этого изображения и сохраните ещё раз.");

    public ErrorText DestinationNotWritable => new(
        "В папку назначения нельзя выполнить запись.",
        "Выберите другую папку или проверьте права на запись. Исходный файл PDF нельзя перезаписать.");

    public ErrorText FileInUse => new(
        "Файл открыт в другом приложении.",
        "Закройте приложение, использующее файл, и повторите попытку.");

    public ErrorText DiskFull => new(
        "Недостаточно свободного места на диске.",
        "Освободите место на диске и повторите попытку.");

    public ErrorText PostSaveVerificationFailed => new(
        "Проверка после сохранения не пройдена, поэтому файл PDF не был сохранён.",
        "Исходный файл PDF не изменён. Скопируйте сведения и сообщите о проблеме.");

    public ErrorText UserCancelled => new("Операция отменена.", "");

    public ErrorText Unexpected => new(
        "Произошла непредвиденная ошибка.",
        "Скопируйте сведения и отправьте их разработчику.");
}
