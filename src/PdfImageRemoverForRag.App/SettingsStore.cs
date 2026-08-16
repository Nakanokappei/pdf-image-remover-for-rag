using System.Text.Json;
using System.Text.Json.Serialization;
using PdfImageRemoverForRag.Core.Models;

namespace PdfImageRemoverForRag.App;

/// <summary>
/// What the user chose in the settings window, kept between runs.
///
/// A separate file from window.json on purpose. The two have nothing in common
/// but a folder: a window placement is discarded whenever the displays change,
/// and a settings choice never is. Sharing one file would mean one of them
/// deciding when the other is thrown away.
///
/// Best-effort in both directions, like the placement store: a preference that
/// cannot be written is not a reason to fail a save, and a file that cannot be
/// read is not a reason to refuse to start.
/// </summary>
internal static class SettingsStore
{
    static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PdfImageRemoverForRag", "settings.json");

    /// <summary>
    /// The resolution is written by NAME rather than by number. The file is one
    /// a person can open, and a name survives a new entry being added to the
    /// list in the middle, which a number does not.
    /// </summary>
    static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new JsonStringEnumConverter() },
        WriteIndented = true,
    };

    public static void Save(ImageReduction reduction)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(reduction, Options));
        }
        catch
        {
            // Persisting a preference is best-effort; never fail the app over it.
        }
    }

    /// <summary>
    /// What was saved, or the defaults when there is no file or it cannot be
    /// read. Anything unreadable is treated as absent rather than repaired:
    /// guessing at half a settings file is how a user ends up with a setting
    /// nobody chose.
    /// </summary>
    public static ImageReduction Load()
    {
        // Reduction is ON for a first run, because a PDF on its way into a RAG
        // pipeline nearly always wants it, and the one number that cannot be
        // guessed - which script the documents are in - is seeded from the
        // display language. See IStrings.DefaultImageSizeLimit for why that is
        // a starting point rather than an answer.
        var fallback = new ImageReduction(
            Enabled: true, L10n.DefaultImageSizeLimit, ImageReduction.DefaultJpegQuality);

        try
        {
            if (!File.Exists(FilePath)) return fallback;
            return JsonSerializer.Deserialize<ImageReduction>(File.ReadAllText(FilePath), Options)
                   ?? fallback;
        }
        catch
        {
            return fallback;
        }
    }
}
