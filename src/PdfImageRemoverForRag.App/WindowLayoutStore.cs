using System.Text.Json;

namespace PdfImageRemoverForRag.App;

/// <summary>
/// Persisted window placement plus a fingerprint of the display arrangement it
/// was captured on. On restore the placement is reused only when the
/// arrangement still matches (same screen index, that screen's size, and the
/// same screen count); otherwise the caller falls back to the default so the
/// window never opens off-screen or larger than the current display.
/// </summary>
internal sealed record WindowLayout(
    int X, int Y, int Width, int Height, bool Maximized,
    int ScreenIndex, int ScreenWidth, int ScreenHeight, int ScreenCount);

internal static class WindowLayoutStore
{
    static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PdfImageRemoverForRag", "window.json");

    /// <summary>Record the current placement and the display arrangement.</summary>
    public static void Save(Form form)
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
                index, screen.Bounds.Width, screen.Bounds.Height, screens.Length);

            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(layout));
        }
        catch
        {
            // Persisting placement is best-effort; never fail the app over it.
        }
    }

    /// <summary>
    /// The saved placement, or null when there is none or the display
    /// arrangement has changed since it was saved (screen count, the recorded
    /// screen's size, or a placement that would land off-screen).
    /// </summary>
    public static WindowLayout? TryLoad()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            var layout = JsonSerializer.Deserialize<WindowLayout>(File.ReadAllText(FilePath));
            if (layout is null) return null;

            var screens = Screen.AllScreens;
            if (layout.ScreenCount != screens.Length) return null;
            if (layout.ScreenIndex < 0 || layout.ScreenIndex >= screens.Length) return null;

            var screen = screens[layout.ScreenIndex];
            if (screen.Bounds.Width != layout.ScreenWidth || screen.Bounds.Height != layout.ScreenHeight)
            {
                return null;
            }
            if (!IsReasonablyVisible(layout, screen)) return null;
            return layout;
        }
        catch
        {
            return null;
        }
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
