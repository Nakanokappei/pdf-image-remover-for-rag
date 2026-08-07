using System.Globalization;

namespace PdfImageRemoverForRag.App;

/// <summary>
/// One photograph to take: the pose, the size of the picture, and where to put
/// it. Sizes are DEVICE pixels — what the file will measure — so the caller
/// asks for what the store wants and the display scale is this side's problem.
/// </summary>
internal sealed record ScreenshotRequest(
    string View, int Width, int Height, string OutputPath, int SettleMilliseconds);

/// <summary>
/// What the command line asked for.
///
/// The app is a desktop tool, not a command-line one, and this exists for one
/// job: the store listing needs the same handful of screens photographed in
/// sixteen languages, which is eighty pictures nobody is going to take by hand.
/// So the app can be told to open a document, pose, photograph itself and quit
/// — one run per picture, with the language and the size given as arguments.
///
/// The plain form (paths only) is what Explorer passes when a PDF is dropped on
/// the icon, and it keeps working exactly as it did.
/// </summary>
internal sealed record CommandLine(
    IReadOnlyList<string> PdfPaths,
    CultureInfo? Language,
    ScreenshotRequest? Screenshot,
    bool ListViews,
    string? Error)
{
    /// <summary>
    /// The store listing's picture size: the golden section, 2710 by 2710/φ.
    /// It was 16:9 while the pictures were taken by hand at whatever the window
    /// happened to be; a camera can be asked for a proportion.
    /// </summary>
    public const int DefaultWidth = 2710;
    public const int DefaultHeight = 1675;

    /// <summary>
    /// How long to let the window settle before the shutter. Opening a document
    /// finishes before this, but thumbnails arrive on a timer afterwards, and a
    /// picture of half-drawn rows is worse than a slow run.
    /// </summary>
    public const int DefaultSettleMilliseconds = 2500;

    public static CommandLine Parse(string[] args)
    {
        var paths = new List<string>();
        CultureInfo? language = null;
        var screenshot = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        bool wantsScreenshot = false;
        bool listViews = false;

        for (int i = 0; i < args.Length; i++)
        {
            var argument = args[i];
            switch (argument.ToLowerInvariant())
            {
                case "--list-views":
                    listViews = true;
                    continue;

                case "--language":
                    if (++i >= args.Length) return Failed("--language needs a language");
                    language = ParseLanguage(args[i]);
                    if (language is null) return Failed($"unknown language: {args[i]}");
                    continue;

                case "--screenshot":
                    wantsScreenshot = true;
                    // Everything that follows in key=value form belongs to it,
                    // so the switch reads as one instruction to the camera.
                    while (i + 1 < args.Length && args[i + 1].Contains('=')
                           && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    {
                        var pair = args[++i].Split('=', 2);
                        screenshot[pair[0]] = pair[1];
                    }
                    continue;
            }

            // Not a switch: a document to open. Anything that is not an existing
            // PDF is dropped silently — the user dropped it on the wrong app,
            // and an error before the window exists would be worse than
            // starting empty.
            //
            // Made ABSOLUTE here. Explorer always passes full paths, so nothing
            // noticed that a relative one reaches the page renderer as it was
            // typed — and the operating system's PDF renderer refuses anything
            // but a full path, so every preview and every page picture silently
            // failed when the app was started from a shell.
            if (string.Equals(Path.GetExtension(argument), ".pdf", StringComparison.OrdinalIgnoreCase)
                && File.Exists(argument))
            {
                paths.Add(Path.GetFullPath(argument));
            }
        }

        if (!wantsScreenshot)
        {
            return new CommandLine(paths, language, null, listViews, null);
        }

        var view = screenshot.TryGetValue("view", out var named) ? named : ScreenshotViews.Table;
        if (!ScreenshotViews.Exists(view)) return Failed($"unknown view: {view}");
        if (!TryPixels(screenshot, "width", DefaultWidth, out int width, out var widthError))
        {
            return Failed(widthError!);
        }
        if (!TryPixels(screenshot, "height", DefaultHeight, out int height, out var heightError))
        {
            return Failed(heightError!);
        }
        if (!TryPixels(screenshot, "settle", DefaultSettleMilliseconds, out int settle, out var settleError))
        {
            return Failed(settleError!);
        }

        // Named for what it shows when the caller does not say, so a run without
        // an out= still leaves a file whose name means something.
        var output = screenshot.TryGetValue("out", out var given)
            ? given
            : $"{view}-{language?.TwoLetterISOLanguageName ?? "default"}.png";

        return new CommandLine(
            paths, language,
            new ScreenshotRequest(view, width, height, Path.GetFullPath(output), settle),
            listViews, null);
    }

    static CommandLine Failed(string message) =>
        new(Array.Empty<string>(), null, null, false, message);

    static bool TryPixels(
        IReadOnlyDictionary<string, string> values, string key, int fallback,
        out int result, out string? error)
    {
        error = null;
        result = fallback;
        if (!values.TryGetValue(key, out var text)) return true;
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out result)
            && result > 0)
        {
            return true;
        }
        error = $"{key} must be a positive whole number, not \"{text}\"";
        return false;
    }

    /// <summary>
    /// A language as either a culture tag ("ja", "zh-Hant") or its English name
    /// ("japanese"). Both because the tags are what the app's translations are
    /// keyed by, and the names are what a person types.
    /// </summary>
    static CultureInfo? ParseLanguage(string value)
    {
        try
        {
            return CultureInfo.GetCultureInfo(value);
        }
        catch (CultureNotFoundException)
        {
            // Not a tag, so try the names.
        }

        return CultureInfo.GetCultures(CultureTypes.NeutralCultures)
            .FirstOrDefault(c => string.Equals(c.EnglishName, value, StringComparison.OrdinalIgnoreCase));
    }
}
