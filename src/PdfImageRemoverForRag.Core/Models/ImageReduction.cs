namespace PdfImageRemoverForRag.Core.Models;

/// <summary>
/// How large an image is allowed to be in a saved PDF.
///
/// Every one of the five is a resolution, measured against the page the image
/// is drawn on. Three were chosen by rendering 6 pt text through this app's own
/// resize pass and reading it back: a writing system needs a certain number of
/// pixels per em before its characters stop being told apart, and below that a
/// retrieval pipeline reads the wrong word without any sign that it did. The
/// numbers are the pixels-per-em thresholds converted at 9 pt, which is the
/// size a figure caption or a table cell is actually set in.
/// </summary>
public enum ImageSizeLimit
{
    /// <summary>
    /// 92 dpi: what a page-sized picture gets when it is made to fit an
    /// ordinary monitor. The smallest option, and the one that does not
    /// promise the small text survives - that is the price of the smallest
    /// file, not a defect.
    /// </summary>
    Screen,

    /// <summary>140 dpi: Latin and Cyrillic down to 9 pt.</summary>
    RagLatin,

    /// <summary>
    /// 200 dpi: Japanese, Chinese, Korean, Devanagari and Vietnamese down to
    /// 9 pt, and Latin well past it.
    /// </summary>
    RagComplexScripts,

    /// <summary>
    /// 300 dpi: the same complex scripts down to 6 pt. The only value here
    /// measured at no errors rather than derived from one.
    /// </summary>
    RagFinePrint,

    /// <summary>400 dpi, with room above the printing standard for line art.</summary>
    Print,
}

/// <summary>
/// What a save does to the images it writes out.
/// </summary>
/// <param name="Enabled">
/// False leaves every image exactly as it came in. The setting is the user's,
/// so this is a real state and not merely the absence of one.
/// </param>
/// <param name="JpegQuality">
/// What a JPEG is written at. One quality for all of them is one thing fewer
/// to reason about; see <see cref="Imaging.JpegQuality"/> for why an image
/// that is not being resized is only rewritten when it sits ABOVE this.
/// </param>
public sealed record ImageReduction(
    bool Enabled,
    ImageSizeLimit SizeLimit,
    int JpegQuality)
{
    public const int MinimumJpegQuality = 50;
    public const int MaximumJpegQuality = 100;
    public const int DefaultJpegQuality = 85;

    /// <summary>
    /// The largest ceiling on offer. It doubles as the absolute cap on anything
    /// this app rasterizes, INCLUDING when reduction is switched off: a whole
    /// page rendered without any ceiling is a bitmap large enough for the
    /// imaging library to fail the allocation, which is a crash and not a
    /// bigger file.
    /// </summary>
    public const ImageSizeLimit AbsoluteCeiling = ImageSizeLimit.Print;

    /// <summary>Images are written out as they came in.</summary>
    public static ImageReduction Off { get; } =
        new(false, ImageSizeLimit.RagComplexScripts, DefaultJpegQuality);

    /// <summary>
    /// Clamped, because settings.json is a file a person can open and type into,
    /// and a quality of zero would write unreadable JPEGs into every output.
    /// </summary>
    public int JpegQuality { get; } =
        Math.Clamp(JpegQuality, MinimumJpegQuality, MaximumJpegQuality);

    /// <summary>The resolution a limit stands for.</summary>
    public static int DpiOf(ImageSizeLimit limit) => limit switch
    {
        ImageSizeLimit.Screen => 92,
        ImageSizeLimit.RagLatin => 140,
        ImageSizeLimit.RagComplexScripts => 200,
        ImageSizeLimit.RagFinePrint => 300,
        _ => 400,
    };

    /// <summary>
    /// The ceiling in pixels for an image drawn on a page of this size. It
    /// follows the page's own shape, so a landscape page is not punished for
    /// lying down.
    /// </summary>
    public (int Width, int Height) CeilingFor(double pageWidthPoints, double pageHeightPoints)
    {
        int dpi = DpiOf(SizeLimit);

        // A page reported as nothing would produce a ceiling of nothing, and an
        // image fitted to that is gone. A4 portrait is the safe stand-in.
        if (pageWidthPoints <= 0 || pageHeightPoints <= 0)
        {
            (pageWidthPoints, pageHeightPoints) = (595.28, 841.89);
        }

        return (
            Math.Max(1, (int)Math.Round(pageWidthPoints / 72.0 * dpi)),
            Math.Max(1, (int)Math.Round(pageHeightPoints / 72.0 * dpi)));
    }
}
