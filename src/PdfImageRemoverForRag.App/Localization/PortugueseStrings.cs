namespace PdfImageRemoverForRag.App.Localization;

/// <summary>
/// Portuguese (pt) UI text, written in Brazilian Portuguese. Registered under
/// the neutral "pt" key, so it also serves European Portuguese; wording avoids
/// terms that only work in one variant except where the two diverge sharply,
/// in which case the Brazilian form wins (arquivo, salvar, senha).
/// </summary>
internal sealed class PortugueseStrings : IStrings
{
    public string AppTitle => "PDF Image Remover for RAG";

    public string MenuFile => "&Arquivo";
    public string MenuOpen => "&Abrir…";
    public string MenuSave => "Remover Selecionados e &Salvar…";
    public string MenuCloseAll => "&Fechar Tudo";
    public string MenuExit => "Sai&r";
    public string MenuView => "E&xibir";
    public string MenuTableView => "&Tabela";
    public string MenuTileView => "&Blocos";
    public string MenuShownTypes => "Tipos &Exibidos";
    public string MenuShowImages => "Imagens";
    public string MenuShowShapes => "Formas";
    public string MenuShowText => "Texto";
    public string MenuHelp => "A&juda";
    public string MenuManual => "&Manual Online…";
    public string MenuAbout => "&Sobre…";

    public string ManualUrl =>
        "https://github.com/Nakanokappei/pdf-image-remover-for-rag/blob/main/docs/manual.en.md";

    public string LinkOpenFailed =>
        "Não foi possível abrir a página. Abra este endereço no seu navegador:\n";

    public string ToolOpen => "Abrir PDF";
    public string ToolSave => "Remover e Salvar";
    public string ToolSelectAll => "Selecionar Tudo";
    public string ToolClearSelection => "Limpar Seleção";

    // Column captions are deliberately short: "Miniatura", "Usos",
    // "Tam. Est." are abbreviated so the narrow table columns do not ellipsize.
    public string ColumnThumbnail => "Miniatura";
    public string ColumnImageId => "ID do Objeto";
    public string ColumnType => "Tipo";
    public string TypeImage => "Imagem";
    public string TypeText => "Texto";
    public string TypeShape => "Forma";
    public string ColumnSize => "Tamanho";
    public string ColumnUsageCount => "Usos";
    public string ColumnCompression => "Compressão";
    public string ColumnEstimatedSize => "Tam. Est.";
    public string ColumnWarning => "Aviso";
    public string AccessibleDeleteColumn => "Remover";
    public string TextSize(int characterCount) => $"{characterCount} caracteres";

    public string StatusOpenPrompt => "Abra um PDF para começar";
    public string StatusAnalyzing => "Analisando o PDF…";

    public string Cancel => "Cancelar";
    public string StatusCancelling => "Cancelando…";
    public string StatusCancelled => "Abertura cancelada";

    public string ProgressReadingPages(string fileName, int page, int pageCount) =>
        $"{fileName} — analisando a página {page} de {pageCount}";

    public string ProgressThumbnails(string fileName, int page, int pageCount) =>
        $"{fileName} — gerando miniaturas, página {page} de {pageCount}";

    public string ProgressGrouping(string fileName) =>
        $"{fileName} — agrupando objetos";

    public string ThumbnailPending => "Gerando miniatura…";

    public string StatusAnalyzed => "Análise concluída";
    public string StatusOpenFailed => "Não foi possível abrir o PDF";
    public string StatusSaving => "Salvando…";
    public string StatusSaveFailed => "Falha ao salvar";

    public string StatusSaved(int fileCount, int drawCallsRemoved) =>
        $"{fileCount} arquivo(s) salvo(s) — {drawCallsRemoved} chamada(s) de desenho removida(s), verificação OK";

    public string StatusSelection(int selectedCount) =>
        $"{selectedCount} grupo(s) de imagens selecionado(s) para remoção";

    public string WarningNotRemovable => "Não removível";

    public string WarningFullPage =>
        "Provavelmente uma página digitalizada - removê-la deixa a página em branco";

    public string TooltipUnsafe =>
        "Esta imagem não pode ser removida com segurança devido à estrutura complexa do PDF.";

    public string TooltipFullPage =>
        "Esta imagem pode ocupar a página inteira.\n" +
        "Removê-la pode apagar tudo o que aparece nessa página, inclusive o conteúdo do texto.";

    public string OpenDialogTitle => "Abrir PDF";
    public string PdfFileFilter => "Arquivos PDF (*.pdf)|*.pdf";
    public string SaveDialogTitle => "Remover Selecionados e Salvar";

    public string OutputFolderDescription =>
        "Escolha a pasta para os PDFs limpos. Cada arquivo é salvo como \"<nome>_cleaned.pdf\".";

    public string SameAsSourceMessage =>
        "O PDF limpo não pode sobrescrever o arquivo de origem. Escolha outro nome.";

    public string SameAsSourceTitle => "Local de Salvamento";
    public string ConfirmTitle => "Confirmar";

    public string ConfirmSaveBeforeOpen =>
        "Há objetos selecionados para remoção. Salvar antes de abrir novos arquivos?\n" +
        "Escolher Não descarta a seleção atual.";

    public string ConfirmDiscardBeforeOpen =>
        "Fechar os arquivos abertos e abrir novos?";

    public string ErrorDialogTitle => "Erro";
    public string CopyDetails => "Copiar Detalhes";
    public string AboutTitle => "Sobre o PDF Image Remover for RAG";

    public string AboutDescription =>
        "Remove dos PDFs os objetos que atrapalham a recuperação — imagens de logotipo, " +
        "textos repetidos de cabeçalho e rodapé, linhas de separação — antes que eles entrem " +
        "no seu pipeline de RAG. Seus arquivos originais nunca são modificados, e tudo é " +
        "executado localmente neste computador.";

    public string AboutAppLicense => "Distribuído sob a Licença MIT.";
    public string AboutThirdPartyLicense => "Bibliotecas: PDFsharp (MIT), PdfPig (Apache-2.0)";
    public string AboutLicenseLink => "Informações de licença";

    public string ErrorSameAsSource =>
        "Não é possível salvar sobre o PDF de origem. Escolha outro nome.";

    public string ErrorNoSelection => "Nenhuma imagem está selecionada para remoção.";

    public string VerificationCleanerSummary(int pagesModified, int drawCallsRemoved) =>
        $"(limpeza: {pagesModified} páginas, {drawCallsRemoved} chamadas de desenho) ";

    public string VerificationMoreWarnings(int remaining) => $" e mais {remaining}";

    public string ErrorVerificationFailedPrefix => "Falha na verificação após o salvamento: ";

    public ErrorText NotAPdf => new(
        "O arquivo selecionado não é um PDF.",
        "Escolha um arquivo válido com a extensão .pdf.");

    public ErrorText PdfCorrupted => new(
        "O arquivo PDF está corrompido ou em um formato ilegível.",
        "Verifique se outro leitor de PDF consegue abri-lo.");

    public ErrorText PdfEncrypted => new(
        "Este PDF está criptografado.",
        "Esta versão não oferece suporte a PDFs protegidos por senha. Remova a proteção e tente novamente.");

    public ErrorText PdfPasswordRequired => new(
        "É necessária uma senha para abrir este PDF.",
        "Esta versão não permite digitar senhas. Remova a proteção e tente novamente.");

    public ErrorText UnsupportedEncryption => new(
        "O PDF usa um esquema de criptografia sem suporte.",
        "Consulte quem produziu o documento sobre a criptografia utilizada.");

    public ErrorText ImageExtractionFailed => new(
        "Não foi possível extrair as imagens do PDF.",
        "Verifique se o problema se repete com outro PDF. Se sim, copie os detalhes e relate o ocorrido.");

    public ErrorText ImageRemovalUnsafe => new(
        "Esta imagem não pode ser removida com segurança devido à estrutura complexa do PDF.",
        "Desmarque a imagem afetada e salve novamente.");

    public ErrorText DestinationNotWritable => new(
        "O destino não permite gravação.",
        "Escolha outra pasta ou verifique a permissão de gravação. O PDF de origem não pode ser sobrescrito.");

    public ErrorText FileInUse => new(
        "O arquivo está aberto em outro aplicativo.",
        "Feche o aplicativo que está usando o arquivo e tente novamente.");

    public ErrorText DiskFull => new(
        "Não há espaço livre suficiente em disco.",
        "Libere espaço em disco e tente novamente.");

    public ErrorText PostSaveVerificationFailed => new(
        "A verificação após o salvamento falhou, portanto o PDF não foi salvo.",
        "O PDF de origem está inalterado. Copie os detalhes e relate o problema.");

    public ErrorText UserCancelled => new("A operação foi cancelada.", "");

    public ErrorText Unexpected => new(
        "Ocorreu um erro inesperado.",
        "Copie os detalhes e relate-os ao desenvolvedor.");
}
