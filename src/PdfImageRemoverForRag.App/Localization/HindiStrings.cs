namespace PdfImageRemoverForRag.App.Localization;

/// <summary>
/// Hindi (hi) UI text. Menu captions follow the Hindi Windows convention of
/// appending the Latin access key in parentheses, as Japanese does.
/// </summary>
internal sealed class HindiStrings : IStrings
{
    public string AppTitle => "PDF Image Remover for RAG";

    public string MenuFile => "फ़ाइल(&F)";
    public string MenuOpen => "खोलें(&O)…";
    public string MenuSave => "चयनित हटाएँ और सहेजें(&S)…";
    public string MenuCloseAll => "सभी बंद करें(&C)";
    public string MenuExit => "बाहर निकलें(&X)";
    public string MenuView => "देखें(&V)";
    public string MenuTableView => "तालिका(&T)";
    public string MenuTileView => "टाइल(&I)";
    public string MenuShownTypes => "दिखाए गए प्रकार(&S)";
    public string MenuShowImages => "छवियाँ";
    public string MenuShowShapes => "आकृतियाँ";
    public string MenuShowText => "टेक्स्ट";
    public string MenuHelp => "सहायता(&H)";
    public string MenuManual => "ऑनलाइन मैनुअल(&M)…";
    public string MenuAbout => "इसके बारे में(&A)…";

    // Only Japanese and English manual pages exist, so Hindi points at English.
    public string ManualUrl =>
        "https://github.com/Nakanokappei/pdf-image-remover-for-rag/blob/main/docs/manual.en.md";

    public string LinkOpenFailed =>
        "पेज नहीं खोला जा सका। कृपया यह URL अपने ब्राउज़र में खोलें:\n";

    public string ToolOpen => "PDF खोलें";
    public string ToolSave => "हटाएँ और सहेजें";
    public string ToolSelectAll => "सभी चुनें";
    public string ToolClearSelection => "चयन हटाएँ";

    // Column headers sit in narrow table columns, so these are deliberately
    // short: "उपयोग" for usage count and "अनु. आकार" for estimated size.
    public string ColumnThumbnail => "थंबनेल";
    public string ColumnImageId => "ऑब्जेक्ट ID";
    public string ColumnType => "प्रकार";
    public string TypeImage => "छवि";
    public string TypeText => "टेक्स्ट";
    public string TypeShape => "आकृति";
    public string ColumnSize => "आकार";
    public string ColumnUsageCount => "उपयोग";
    public string ColumnCompression => "संपीड़न";
    public string ColumnEstimatedSize => "अनु. आकार";
    public string ColumnWarning => "चेतावनी";
    public string AccessibleDeleteColumn => "हटाएँ";
    public string TextSize(int characterCount) => $"{characterCount} वर्ण";

    public string StatusOpenPrompt => "शुरू करने के लिए कोई PDF खोलें";
    public string StatusAnalyzing => "PDF का विश्लेषण हो रहा है…";

    public string Cancel => "रद्द करें";
    public string StatusCancelling => "रद्द किया जा रहा है…";
    public string StatusCancelled => "खोलना रद्द किया गया";

    public string ProgressReadingPages(string fileName, int page, int pageCount) =>
        $"{fileName} — पेज {page} / {pageCount} का विश्लेषण हो रहा है";

    public string ProgressThumbnails(string fileName, int page, int pageCount) =>
        $"{fileName} — थंबनेल बन रहे हैं, पेज {page} / {pageCount}";

    public string ProgressGrouping(string fileName) =>
        $"{fileName} — ऑब्जेक्ट समूहीकृत हो रहे हैं";

    public string ThumbnailPending => "थंबनेल बन रहा है…";

    public string StatusAnalyzed => "विश्लेषण पूरा हुआ";
    public string StatusOpenFailed => "PDF नहीं खोली जा सकी";
    public string StatusSaving => "सहेजा जा रहा है…";
    public string StatusSaveFailed => "सहेजना विफल रहा";

    public string StatusSaved(int fileCount, int drawCallsRemoved) =>
        $"{fileCount} फ़ाइल सहेजी गईं — {drawCallsRemoved} ड्रॉ कॉल हटाए गए, सत्यापन ठीक रहा";

    public string StatusSelection(int selectedCount) =>
        $"{selectedCount} छवि समूह हटाने के लिए चुने गए";

    public string WarningNotRemovable => "हटाया नहीं जा सकता";

    public string WarningFullPage =>
        "संभवतः स्कैन किया गया पेज - इसे हटाने पर पेज खाली रह जाएगा";

    public string TooltipUnsafe =>
        "PDF की जटिल संरचना के कारण यह छवि सुरक्षित रूप से नहीं हटाई जा सकती।";

    public string TooltipFullPage =>
        "यह छवि पूरा पेज घेर सकती है।\n" +
        "इसे हटाने पर उस पेज की सभी दिखने वाली सामग्री, मुख्य सामग्री सहित, मिट सकती है।";

    public string OpenDialogTitle => "PDF खोलें";
    public string PdfFileFilter => "PDF फ़ाइलें (*.pdf)|*.pdf";
    public string SaveDialogTitle => "चयनित हटाएँ और सहेजें";

    public string OutputFolderDescription =>
        "साफ़ की गई PDF के लिए फ़ोल्डर चुनें। हर फ़ाइल \"<name>_cleaned.pdf\" के रूप में सहेजी जाती है।";

    public string SameAsSourceMessage =>
        "साफ़ की गई PDF स्रोत फ़ाइल को अधिलेखित नहीं कर सकती। कोई दूसरा नाम चुनें।";

    public string SameAsSourceTitle => "सहेजने का स्थान";
    public string ConfirmTitle => "पुष्टि करें";

    public string ConfirmSaveBeforeOpen =>
        "आपने कुछ ऑब्जेक्ट हटाने के लिए चुने हैं। नई फ़ाइलें खोलने से पहले सहेजें?\n" +
        "\"नहीं\" चुनने पर मौजूदा चयन हटा दिया जाएगा।";

    public string ConfirmDiscardBeforeOpen =>
        "वर्तमान में खुली फ़ाइलें बंद करके नई फ़ाइलें खोलें?";

    public string ErrorDialogTitle => "त्रुटि";
    public string CopyDetails => "विवरण कॉपी करें";
    public string AboutTitle => "PDF Image Remover for RAG के बारे में";

    public string AboutDescription =>
        "आपकी RAG पाइपलाइन में जाने से पहले PDF से वे ऑब्जेक्ट हटाता है जो पुनर्प्राप्ति में " +
        "बाधा डालते हैं — लोगो छवियाँ, बार-बार आने वाला हेडर और फ़ुटर टेक्स्ट, और लाइनें। " +
        "आपकी मूल फ़ाइलें कभी नहीं बदली जातीं, और सब कुछ इसी PC पर स्थानीय रूप से चलता है।";

    public string AboutAppLicense => "MIT लाइसेंस के अंतर्गत जारी।";
    public string AboutThirdPartyLicense => "लाइब्रेरी: PDFsharp (MIT), PdfPig (Apache-2.0)";
    public string AboutLicenseLink => "लाइसेंस जानकारी";

    public string ErrorSameAsSource =>
        "स्रोत PDF पर सहेजा नहीं जा सकता। कोई दूसरा नाम चुनें।";

    public string ErrorNoSelection => "हटाने के लिए कोई छवि नहीं चुनी गई है।";

    public string VerificationCleanerSummary(int pagesModified, int drawCallsRemoved) =>
        $"(क्लीनर: {pagesModified} पेज, {drawCallsRemoved} ड्रॉ कॉल) ";

    public string VerificationMoreWarnings(int remaining) => $" और {remaining} अन्य";

    public string ErrorVerificationFailedPrefix => "सहेजने के बाद का सत्यापन विफल रहा: ";

    public ErrorText NotAPdf => new(
        "चुनी गई फ़ाइल PDF नहीं है।",
        ".pdf एक्सटेंशन वाली मान्य फ़ाइल चुनें।");

    public ErrorText PdfCorrupted => new(
        "PDF फ़ाइल क्षतिग्रस्त है या ऐसे प्रारूप में है जिसे पढ़ा नहीं जा सकता।",
        "जाँचें कि क्या कोई दूसरा PDF व्यूअर इसे खोल पाता है।");

    public ErrorText PdfEncrypted => new(
        "यह PDF एन्क्रिप्टेड है।",
        "यह संस्करण पासवर्ड से सुरक्षित PDF का समर्थन नहीं करता। सुरक्षा हटाकर फिर कोशिश करें।");

    public ErrorText PdfPasswordRequired => new(
        "इस PDF को खोलने के लिए पासवर्ड आवश्यक है।",
        "यह संस्करण पासवर्ड दर्ज करने का समर्थन नहीं करता। सुरक्षा हटाकर फिर कोशिश करें।");

    public ErrorText UnsupportedEncryption => new(
        "यह PDF असमर्थित एन्क्रिप्शन योजना का उपयोग करती है।",
        "दस्तावेज़ बनाने वाले से उपयोग किए गए एन्क्रिप्शन के बारे में पूछें।");

    public ErrorText ImageExtractionFailed => new(
        "PDF से छवियाँ निकाली नहीं जा सकीं।",
        "जाँचें कि क्या यह समस्या किसी दूसरी PDF के साथ भी होती है। यदि हाँ, तो विवरण कॉपी करके रिपोर्ट करें।");

    public ErrorText ImageRemovalUnsafe => new(
        "PDF की जटिल संरचना के कारण यह छवि सुरक्षित रूप से नहीं हटाई जा सकती।",
        "संबंधित छवि का चयन हटाकर फिर से सहेजें।");

    public ErrorText DestinationNotWritable => new(
        "गंतव्य में लिखा नहीं जा सकता।",
        "कोई दूसरा फ़ोल्डर चुनें या लिखने की अनुमति जाँचें। स्रोत PDF को अधिलेखित नहीं किया जा सकता।");

    public ErrorText FileInUse => new(
        "फ़ाइल किसी दूसरे एप्लिकेशन में खुली है।",
        "फ़ाइल का उपयोग कर रहे एप्लिकेशन को बंद करें, फिर दोबारा कोशिश करें।");

    public ErrorText DiskFull => new(
        "डिस्क में पर्याप्त खाली जगह नहीं है।",
        "डिस्क में जगह खाली करें, फिर दोबारा कोशिश करें।");

    public ErrorText PostSaveVerificationFailed => new(
        "सहेजने के बाद का सत्यापन विफल रहा, इसलिए PDF सहेजी नहीं गई।",
        "स्रोत PDF अपरिवर्तित है। विवरण कॉपी करके समस्या की रिपोर्ट करें।");

    public ErrorText UserCancelled => new("कार्रवाई रद्द कर दी गई।", "");

    public ErrorText Unexpected => new(
        "एक अप्रत्याशित त्रुटि हुई।",
        "विवरण कॉपी करके डेवलपर को भेजें।");
}
