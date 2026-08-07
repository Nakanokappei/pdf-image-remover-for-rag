namespace PdfImageRemoverForRag.App;

/// <summary>
/// The poses the app can be asked to hold, and the languages it can hold them
/// in. Printed by <c>--list-views</c> so whoever is directing the shoot can ask
/// the app what it can do rather than keep a list of its own that goes stale.
///
/// One entry per store screenshot. They are named for what a reader sees, not
/// for the control involved, because the name ends up in the file name and in
/// the listing's image slot.
/// </summary>
internal static class ScreenshotViews
{
    public const string Table = "table";
    public const string Tiles = "tiles";
    public const string Objects = "objects";
    public const string ShownTypes = "shown-types";
    public const string Usage = "usage";

    /// <summary>Every pose, in the order a listing would show them.</summary>
    public static readonly IReadOnlyList<(string Name, string Shows)> All = new[]
    {
        (Table, "the object list as a table, with several objects ticked for removal"),
        (Tiles, "the same objects as tiles"),
        (Objects, "the graphics-objects panel, with one object outlined on the page"),
        (ShownTypes, "the Shown Types menu open over the list"),
        (Usage, "the usage window: every page an object is drawn on"),
    };

    /// <summary>
    /// The languages the store listing is published in — the app's sixteen
    /// translations, each named by the culture the app resolves it under.
    /// The bare "zh" is deliberately absent: it is a fallback for a Chinese
    /// with no script, not a market of its own.
    /// </summary>
    public static readonly IReadOnlyList<string> Languages = new[]
    {
        "ja", "en", "zh-Hans", "zh-Hant", "ko", "de", "fr", "es",
        "it", "pt", "ru", "id", "ms", "hi", "tr", "vi",
    };

    public static bool Exists(string name) =>
        All.Any(v => string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// What <c>--list-views</c> prints: tab-separated, so it can be read by a
    /// person or split by a script without either having to guess.
    /// </summary>
    public static string Describe()
    {
        var text = new System.Text.StringBuilder();
        text.AppendLine("views:");
        foreach (var (name, shows) in All) text.AppendLine($"\t{name}\t{shows}");
        text.AppendLine("languages:");
        text.AppendLine($"\t{string.Join(" ", Languages)}");
        text.AppendLine("default size:");
        text.AppendLine($"\t{CommandLine.DefaultWidth}x{CommandLine.DefaultHeight}");
        text.AppendLine("example:");
        text.AppendLine(
            "\tPdfImageRemoverForRag.exe sample.pdf --language ja "
            + "--screenshot view=table width=2710 height=1525 out=table-ja.png");
        return text.ToString();
    }
}
