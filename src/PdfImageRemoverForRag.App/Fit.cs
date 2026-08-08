namespace PdfImageRemoverForRag.App;

/// <summary>
/// Putting a picture inside a box, keeping its shape. Four surfaces draw a
/// picture into a rectangle they did not choose — the grid's thumbnail cell, the
/// objects panel's rows and its page preview, and the usage window's pages — and
/// each had its own copy of the same six lines.
/// </summary>
internal static class Fit
{
    /// <summary>
    /// The rectangle to draw <paramref name="picture"/> in: scaled to fit
    /// <paramref name="box"/> and centered in it, never distorted.
    /// </summary>
    /// <param name="mayEnlarge">
    /// True where the picture was rendered for this box and filling it is the
    /// point — a page rendered at the pane's own width, a thumbnail in a row.
    /// False where the picture is whatever a document happened to hold: a
    /// 40×30 logo blown up to fill a cell is a blur, and the usage window's
    /// one-pixel stand-in for a page that would not render would become a
    /// full-size gray rectangle.
    /// </param>
    public static Rectangle Inside(Size picture, Rectangle box, bool mayEnlarge)
    {
        double scale = Math.Min(
            (double)box.Width / picture.Width, (double)box.Height / picture.Height);
        if (!mayEnlarge) scale = Math.Min(1.0, scale);

        int width = Math.Max(1, (int)(picture.Width * scale));
        int height = Math.Max(1, (int)(picture.Height * scale));
        return new Rectangle(
            box.X + ((box.Width - width) / 2),
            box.Y + ((box.Height - height) / 2),
            width, height);
    }
}
