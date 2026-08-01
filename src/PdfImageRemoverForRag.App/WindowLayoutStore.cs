using System.Text.Json;

namespace PdfImageRemoverForRag.App;

/// <summary>
/// Persisted window placement plus a fingerprint of the display arrangement it
/// was captured on. On restore the placement is reused only when the
/// arrangement still matches (same screen index, that screen's size, and the
/// same screen count); otherwise the caller falls back to the default so the
/// window never opens off-screen or larger than the current display.
/// </summary>
/// <param name="FlattenPanelWidth">
/// Width of the 統合 panel, and <paramref name="FlattenPreviewHeight"/> the
/// height of the preview inside it — both in LOGICAL (96-DPI) pixels, because a
/// width dragged on the 200 % VM would be half the panel on a 100 % display if
/// it were stored in device pixels. Zero means "never recorded", which is what
/// a window.json written before these existed deserializes to, so the defaults
/// apply and an old file needs no migration.
///
/// They ride along with the placement but are NOT subject to its
/// display-arrangement guard: a splitter position cannot put anything
/// off-screen, so plugging in a second monitor is no reason to forget it.
/// </param>
internal sealed record WindowLayout(
    int X, int Y, int Width, int Height, bool Maximized,
    int ScreenIndex, int ScreenWidth, int ScreenHeight, int ScreenCount,
    int FlattenPanelWidth = 0, int FlattenPreviewHeight = 0);

internal static class WindowLayoutStore
{
    static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PdfImageRemoverForRag", "window.json");

    /// <summary>
    /// Record the current placement, the display arrangement, and where the user
    /// left the two splitters. The splitter sizes are passed in rather than read
    /// off the form: they are logical pixels, and only the caller knows the scale
    /// they were measured at.
    /// </summary>
    public static void Save(Form form, int flattenPanelWidth, int flattenPreviewHeight)
    {
        try
        {
            // Normal (restore) bounds, so a maximized/minimized window still
            // records a sensible size to return to.
            var bounds = form.WindowState == FormWindowState.Normal ? form.Bounds : form.RestoreBounds;
            var screens = Screen.AllScreens;
            var screen = Screen.FromRectangle(bounds);
            int index = Array.IndexOf(screens, screen);
            if (index < 0) index = 0;

            var layout = new WindowLayout(
                bounds.X, bounds.Y, bounds.Width, bounds.Height,
                form.WindowState == FormWindowState.Maximized,
                index, screen.Bounds.Width, screen.Bounds.Height, screens.Length,
                flattenPanelWidth, flattenPreviewHeight);

            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(layout));
        }
        catch
        {
            // Persisting placement is best-effort; never fail the app over it.
        }
    }

    /// <summary>
    /// Everything that was saved, or null when there is no file or it cannot be
    /// read. This does NOT decide whether the window bounds are safe to use —
    /// ask <see cref="PlacementIsUsable"/> for that. The two are separate
    /// because the splitter sizes survive a display change that the bounds
    /// cannot.
    /// </summary>
    public static WindowLayout? TryLoad()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            return JsonSerializer.Deserialize<WindowLayout>(File.ReadAllText(FilePath));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Whether the saved bounds can be restored: the display arrangement has to
    /// still match (screen count, the recorded screen's size) and the window has
    /// to land somewhere reachable. False means open at the default size.
    /// </summary>
    public static bool PlacementIsUsable(WindowLayout layout)
    {
        var screens = Screen.AllScreens;
        if (layout.ScreenCount != screens.Length) return false;
        if (layout.ScreenIndex < 0 || layout.ScreenIndex >= screens.Length) return false;

        var screen = screens[layout.ScreenIndex];
        if (screen.Bounds.Width != layout.ScreenWidth || screen.Bounds.Height != layout.ScreenHeight)
        {
            return false;
        }
        return IsReasonablyVisible(layout, screen);
    }

    // Require a meaningful overlap with the screen's working area so the title
    // bar stays reachable even if the resolution changed within the same layout.
    static bool IsReasonablyVisible(WindowLayout layout, Screen screen)
    {
        var windowRect = new Rectangle(layout.X, layout.Y, layout.Width, layout.Height);
        var overlap = Rectangle.Intersect(windowRect, screen.WorkingArea);
        return overlap.Width >= 100 && overlap.Height >= 50;
    }
}
