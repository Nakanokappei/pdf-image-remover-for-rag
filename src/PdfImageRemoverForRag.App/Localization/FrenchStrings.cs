namespace PdfImageRemoverForRag.App.Localization;

/// <summary>
/// French (fr) UI text. Western access-key convention: the "&amp;" marks a
/// letter inside the caption. French typography wants a narrow no-break space
/// before ":" "?" "!"; a plain ASCII space is used here instead, to keep every
/// string free of characters that could surprise an encoding.
/// </summary>
internal sealed class FrenchStrings : IStrings
{
    public string AppTitle => "PDF Image Remover for RAG";

    public string MenuFile => "&Fichier";
    public string MenuOpen => "&Ouvrir…";
    public string MenuSave => "Supprimer la sélection et &enregistrer…";
    public string MenuCloseAll => "&Tout fermer";
    public string MenuExit => "&Quitter";
    public string MenuView => "&Affichage";
    public string MenuTableView => "&Tableau";
    public string MenuTileView => "&Vignettes";
    public string MenuShownTypes => "Types &affichés";
    public string MenuShowImages => "Images";
    public string MenuShowShapes => "Formes";
    public string MenuShowText => "Texte";
    public string MenuHelp => "Aid&e";
    public string MenuManual => "&Manuel en ligne…";
    public string MenuAbout => "À &propos…";

    // Only Japanese and English manual pages exist, so French points at English.
    public string ManualUrl =>
        "https://github.com/Nakanokappei/pdf-image-remover-for-rag/blob/main/docs/manual.en.md";

    public string LinkOpenFailed =>
        "Impossible d'ouvrir la page. Ouvrez cette URL dans votre navigateur :\n";

    public string ToolOpen => "Ouvrir un PDF";
    public string ToolSave => "Supprimer et enregistrer";
    public string ToolSelectAll => "Tout sélectionner";
    public string ToolClearSelection => "Effacer la sélection";

    // Column captions sit in narrow columns. Deliberately shortened:
    // ColumnUsageCount ("Occurrences"), ColumnCompression ("Compression"),
    // ColumnEstimatedSize ("Taille estimée").
    public string ColumnThumbnail => "Aperçu";
    public string ColumnImageId => "ID objet";
    public string ColumnType => "Type";
    public string TypeImage => "Image";
    public string TypeText => "Texte";
    public string TypeShape => "Forme";
    public string ColumnSize => "Taille";
    public string ColumnUsageCount => "Occur.";
    public string ColumnCompression => "Compr.";
    public string ColumnEstimatedSize => "Taille est.";
    public string ColumnWarning => "Avertissement";
    public string AccessibleDeleteColumn => "Supprimer";
    public string TextSize(int characterCount) => $"{characterCount} car.";

    public string StatusOpenPrompt => "Ouvrez un PDF pour commencer";
    public string StatusAnalyzing => "Analyse du PDF…";

    public string Cancel => "Annuler";
    public string StatusCancelling => "Annulation…";
    public string StatusCancelled => "Ouverture annulée";

    public string ProgressReadingPages(string fileName, int page, int pageCount) =>
        $"{fileName} — analyse de la page {page} sur {pageCount}";

    public string ProgressThumbnails(string fileName, int page, int pageCount) =>
        $"{fileName} — création des aperçus, page {page} sur {pageCount}";

    public string ProgressGrouping(string fileName) =>
        $"{fileName} — regroupement des objets";

    public string ThumbnailPending => "Création de l'aperçu…";

    public string StatusAnalyzed => "Analyse terminée";
    public string StatusOpenFailed => "Impossible d'ouvrir le PDF";
    public string StatusSaving => "Enregistrement…";
    public string StatusSaveFailed => "Échec de l'enregistrement";

    public string StatusSaved(int fileCount, int drawCallsRemoved) =>
        $"{fileCount} fichier(s) enregistré(s) — {drawCallsRemoved} appel(s) de dessin supprimé(s), vérification OK";

    public string StatusSelection(int selectedCount) =>
        $"{selectedCount} groupe(s) d'images sélectionné(s) pour suppression";

    public string WarningNotRemovable => "Non supprimable";

    public string WarningFullPage =>
        "Probablement une page numérisée - la supprimer laisse la page vide";

    public string TooltipUnsafe =>
        "Cette image ne peut pas être supprimée sans risque en raison de la structure complexe du PDF.";

    public string TooltipFullPage =>
        "Cette image occupe peut-être toute la page.\n" +
        "La supprimer peut effacer tout ce qui est visible sur cette page, y compris le contenu principal.";

    public string OpenDialogTitle => "Ouvrir un PDF";
    public string PdfFileFilter => "Fichiers PDF (*.pdf)|*.pdf";
    public string SaveDialogTitle => "Supprimer la sélection et enregistrer";

    public string OutputFolderDescription =>
        "Choisissez le dossier des PDF nettoyés. Chaque fichier est enregistré sous \"<nom>_cleaned.pdf\".";

    public string SameAsSourceMessage =>
        "Le PDF nettoyé ne peut pas remplacer le fichier source. Choisissez un autre nom.";

    public string SameAsSourceTitle => "Emplacement d'enregistrement";
    public string ConfirmTitle => "Confirmation";

    public string ConfirmSaveBeforeOpen =>
        "Des objets sont sélectionnés pour suppression. Enregistrer avant d'ouvrir de nouveaux fichiers ?\n" +
        "Si vous choisissez Non, la sélection actuelle sera abandonnée.";

    public string ConfirmDiscardBeforeOpen =>
        "Fermer les fichiers actuellement ouverts et en ouvrir de nouveaux ?";

    public string ErrorDialogTitle => "Erreur";
    public string CopyDetails => "Copier les détails";
    public string AboutTitle => "À propos de PDF Image Remover for RAG";

    public string AboutDescription =>
        "Supprime les objets qui gênent la recherche documentaire — images de logo, " +
        "textes d'en-tête et de pied de page répétés, filets — des PDF avant leur entrée " +
        "dans votre pipeline RAG. Vos fichiers d'origine ne sont jamais modifiés et tout " +
        "s'exécute localement sur ce PC.";

    public string AboutAppLicense => "Distribué sous licence MIT.";
    public string AboutThirdPartyLicense => "Bibliothèques : PDFsharp (MIT), PdfPig (Apache-2.0)";
    public string AboutLicenseLink => "Informations de licence";

    public string ErrorSameAsSource =>
        "Impossible d'enregistrer par-dessus le PDF source. Choisissez un autre nom.";

    public string ErrorNoSelection => "Aucune image n'est sélectionnée pour suppression.";

    public string VerificationCleanerSummary(int pagesModified, int drawCallsRemoved) =>
        $"(nettoyage : {pagesModified} pages, {drawCallsRemoved} appels de dessin) ";

    public string VerificationMoreWarnings(int remaining) => $" et {remaining} de plus";

    public string ErrorVerificationFailedPrefix => "Échec de la vérification après enregistrement : ";

    public ErrorText NotAPdf => new(
        "Le fichier sélectionné n'est pas un PDF.",
        "Choisissez un fichier valide portant l'extension .pdf.");

    public ErrorText PdfCorrupted => new(
        "Le fichier PDF est endommagé ou dans un format illisible.",
        "Vérifiez si une autre visionneuse PDF parvient à l'ouvrir.");

    public ErrorText PdfEncrypted => new(
        "Ce PDF est chiffré.",
        "Cette version ne prend pas en charge les PDF protégés par mot de passe. Retirez la protection, puis réessayez.");

    public ErrorText PdfPasswordRequired => new(
        "Un mot de passe est requis pour ouvrir ce PDF.",
        "Cette version ne permet pas de saisir un mot de passe. Retirez la protection, puis réessayez.");

    public ErrorText UnsupportedEncryption => new(
        "Le PDF utilise un mode de chiffrement non pris en charge.",
        "Renseignez-vous auprès du producteur du document sur le chiffrement utilisé.");

    public ErrorText ImageExtractionFailed => new(
        "Impossible d'extraire les images du PDF.",
        "Vérifiez si le problème se reproduit avec un autre PDF. Si c'est le cas, copiez les détails et signalez-le.");

    public ErrorText ImageRemovalUnsafe => new(
        "Cette image ne peut pas être supprimée sans risque en raison de la structure complexe du PDF.",
        "Décochez l'image concernée, puis enregistrez de nouveau.");

    public ErrorText DestinationNotWritable => new(
        "La destination n'est pas accessible en écriture.",
        "Choisissez un autre dossier ou vérifiez les droits d'écriture. Le PDF source ne peut pas être remplacé.");

    public ErrorText FileInUse => new(
        "Le fichier est ouvert dans une autre application.",
        "Fermez l'application qui utilise le fichier, puis réessayez.");

    public ErrorText DiskFull => new(
        "L'espace disque disponible est insuffisant.",
        "Libérez de l'espace disque, puis réessayez.");

    public ErrorText PostSaveVerificationFailed => new(
        "La vérification après enregistrement a échoué ; le PDF n'a donc pas été enregistré.",
        "Le PDF source est inchangé. Copiez les détails et signalez le problème.");

    public ErrorText UserCancelled => new("L'opération a été annulée.", "");

    public ErrorText Unexpected => new(
        "Une erreur inattendue s'est produite.",
        "Copiez les détails et signalez-les au développeur.");
}
