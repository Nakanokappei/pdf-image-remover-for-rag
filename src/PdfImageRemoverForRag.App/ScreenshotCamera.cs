using System.Runtime.InteropServices;

namespace PdfImageRemoverForRag.App;

/// <summary>
/// Photographs a window, at an exact pixel size, off the screen.
///
/// Off the SCREEN rather than out of the window, because two of the pictures
/// wanted are of a menu standing open and of a second window: both are windows
/// of their own, and a window asked to draw itself draws only itself. What the
/// eye sees is what the screen holds, so that is what is copied.
///
/// Two things have to be arranged first, and both are one call each. A window's
/// own bounds include an invisible resize border, so the size asked for is
/// applied to the frame the eye can SEE; and Windows 11 rounds a window's
/// corners, which on a screen copy come out as four little pieces of whatever
/// was behind it.
/// </summary>
internal static class ScreenshotCamera
{
    /// <summary>
    /// Give the window a visible frame of exactly this size. Measured and
    /// corrected rather than computed: the difference between the window
    /// rectangle and the visible one is a theme's business, not this code's.
    ///
    /// Corrected in a loop with a pause, because the measurement is of the
    /// COMPOSITED window and the compositor is a frame behind. Measuring
    /// immediately after a resize answers about the window as it was, and one
    /// correction from a stale reading left the last twenty-five rows of the
    /// picture showing whatever was behind the app.
    /// </summary>
    public static async Task SizeVisibleFrameAsync(Form form, int width, int height)
    {
        SquareTheCorners(form.Handle);
        for (int attempt = 0; attempt < 6; attempt++)
        {
            var visible = VisibleFrame(form.Handle);
            int widthShort = width - visible.Width;
            int heightShort = height - visible.Height;
            if (widthShort == 0 && heightShort == 0) return;

            form.Size = new Size(form.Width + widthShort, form.Height + heightShort);
            await Task.Delay(80);
        }
    }

    /// <summary>
    /// Copy the window's visible frame into a PNG. Answers the size written, so
    /// the caller can check it got what it asked for rather than trust it.
    /// </summary>
    public static Size Capture(Form form, string path)
    {
        var frame = VisibleFrame(form.Handle);
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");

        using var bitmap = new Bitmap(frame.Width, frame.Height);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(frame.Location, Point.Empty, frame.Size);
        }
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        return bitmap.Size;
    }

    /// <summary>
    /// The rectangle the user can see, which is not the window's own: a resize
    /// border sits outside it, invisible and several pixels wide.
    /// </summary>
    static Rectangle VisibleFrame(IntPtr handle)
    {
        if (DwmGetWindowAttribute(handle, DwmwaExtendedFrameBounds,
                out var bounds, Marshal.SizeOf<Rect>()) != 0)
        {
            // No composition to ask: the window rectangle is all there is.
            GetWindowRect(handle, out bounds);
        }
        return Rectangle.FromLTRB(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
    }

    /// <summary>
    /// Take the rounded corners off. They are drawn by the desktop compositor
    /// OUTSIDE the window, so a screen copy of a rounded window has the desktop
    /// showing through its four corners — wallpaper in a store screenshot.
    /// </summary>
    static void SquareTheCorners(IntPtr handle)
    {
        int doNotRound = DwmwcpDoNotRound;
        DwmSetWindowAttribute(handle, DwmwaWindowCornerPreference, ref doNotRound, sizeof(int));
    }

    const int DwmwaExtendedFrameBounds = 9;
    const int DwmwaWindowCornerPreference = 33;
    const int DwmwcpDoNotRound = 1;

    [StructLayout(LayoutKind.Sequential)]
    struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmGetWindowAttribute(
        IntPtr window, int attribute, out Rect value, int size);

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(
        IntPtr window, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetWindowRect(IntPtr window, out Rect rectangle);
}
