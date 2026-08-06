using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using PdfImageRemoverForRag.Core.Models;

namespace PdfImageRemoverForRag.App;

/// <summary>
/// Draws a rendered PDF page with certain places on it picked out: everything
/// outside them desaturated and darkened, the places themselves left in full
/// colour and outlined.
///
/// Two windows point at a place on a page — the usage-locations window ("where
/// is this object drawn?") and the flatten tab's preview ("what would be baked
/// into an image?") — and they must not drift into two different visual
/// languages for the same question. The dimming came out of a specific
/// complaint, that an outline alone was not noticeable, so it is also the one
/// place to tune if that comes up again.
/// </summary>
internal static class PageHighlightPainter
{
    // Row = input channel, column = output channel; the luminance weights are
    // the usual 0.3086/0.6094/0.0820, mixed toward grey by KeptSaturation and
    // then scaled by Dimming.
    const float KeptSaturation = 0.12f;
    const float Dimming = 0.7f;
    static readonly ImageAttributes DimAttributes = BuildDimAttributes();

    static ImageAttributes BuildDimAttributes()
    {
        const float lr = 0.3086f, lg = 0.6094f, lb = 0.0820f;
        const float s = KeptSaturation, k = Dimming;
        var matrix = new ColorMatrix(new[]
        {
            new[] { (((1 - s) * lr) + s) * k, (1 - s) * lr * k,             (1 - s) * lr * k,             0f, 0f },
            new[] { (1 - s) * lg * k,         (((1 - s) * lg) + s) * k,     (1 - s) * lg * k,             0f, 0f },
            new[] { (1 - s) * lb * k,         (1 - s) * lb * k,             (((1 - s) * lb) + s) * k,     0f, 0f },
            new[] { 0f, 0f, 0f, 1f, 0f },
            new[] { 0f, 0f, 0f, 0f, 1f },
        });
        var attributes = new ImageAttributes();
        attributes.SetColorMatrix(matrix);
        return attributes;
    }

    /// <summary>
    /// Draw the page into <paramref name="destination"/>, dimmed everywhere
    /// except inside <paramref name="boxes"/>. With no boxes the page is drawn
    /// normally: there is nothing to point at, and dimming the whole thing would
    /// only say "nothing here".
    /// </summary>
    public static void DrawPage(
        Graphics g, Bitmap page, Rectangle destination, IReadOnlyList<RectangleF> boxes)
    {
        if (boxes.Count == 0)
        {
            g.DrawImage(page, destination);
            return;
        }

        g.DrawImage(page, destination, 0, 0, page.Width, page.Height,
            GraphicsUnit.Pixel, DimAttributes);

        // Repaint each place from the same bitmap, mapping the displayed
        // rectangle back to source pixels.
        double toSourceX = (double)page.Width / destination.Width;
        double toSourceY = (double)page.Height / destination.Height;
        foreach (var box in boxes)
        {
            var source = new RectangleF(
                (float)((box.X - destination.X) * toSourceX),
                (float)((box.Y - destination.Y) * toSourceY),
                (float)(box.Width * toSourceX), (float)(box.Height * toSourceY));
            g.DrawImage(page, box, source, GraphicsUnit.Pixel);
        }
    }

    /// <summary>
    /// Draw the page in full colour and grey out the given places — the inverse
    /// of <see cref="DrawPage"/>, and the layers panel's question rather than the
    /// usage window's. There, dimming answers "everything except this"; here it
    /// answers "this is not going to be there", because a hidden layer is one the
    /// save takes out. Which places are SELECTED is a different question, and
    /// the outlines answer it separately.
    /// </summary>
    public static void DrawPageWithDimmedPlaces(
        Graphics g, Bitmap page, Rectangle destination, IReadOnlyList<RectangleF> boxes)
    {
        g.DrawImage(page, destination);
        if (boxes.Count == 0) return;

        // Each place repainted from the same bitmap through the dimming matrix,
        // mapping the displayed rectangle back to source pixels.
        double toSourceX = (double)page.Width / destination.Width;
        double toSourceY = (double)page.Height / destination.Height;
        foreach (var box in boxes)
        {
            var source = new Rectangle(
                (int)Math.Floor((box.X - destination.X) * toSourceX),
                (int)Math.Floor((box.Y - destination.Y) * toSourceY),
                (int)Math.Ceiling(box.Width * toSourceX),
                (int)Math.Ceiling(box.Height * toSourceY));
            source.Intersect(new Rectangle(0, 0, page.Width, page.Height));
            if (source.Width < 1 || source.Height < 1) continue;

            g.DrawImage(page, Rectangle.Round(box),
                source.X, source.Y, source.Width, source.Height,
                GraphicsUnit.Pixel, DimAttributes);
        }
    }

    /// <summary>
    /// Outline each place in light blue (the theme's Highlight under high
    /// contrast). No translucent fill: the area inside the outline is the one
    /// part still in full colour, and a wash over it would undo that.
    /// </summary>
    /// <param name="maxPenWidth">
    /// Widest pen to use, in device pixels. A single line of text is only a few
    /// pixels tall on a page thumbnail, and a 2 px outline drawn on both sides of
    /// it fills the box solid — hiding the very thing being pointed at, so thin
    /// boxes get a thinner pen.
    /// </param>
    public static void DrawOutlines(
        Graphics g, IReadOnlyList<RectangleF> boxes, float maxPenWidth)
    {
        if (boxes.Count == 0) return;

        var color = SystemInformation.HighContrast
            ? SystemColors.Highlight : Color.FromArgb(0x1E, 0x90, 0xFF);
        var saved = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        foreach (var box in boxes)
        {
            float width = Math.Clamp(Math.Min(box.Width, box.Height) / 5f, 1f, maxPenWidth);
            using var pen = new Pen(color, width);
            g.DrawRectangle(pen, box.X, box.Y, box.Width, box.Height);
        }
        g.SmoothingMode = saved;
    }

    /// <summary>
    /// Map bounding boxes in PDF points onto the displayed page rectangle. The
    /// boxes are in content space (origin bottom-left) while the rendered page
    /// is drawn the way a viewer shows it, so each box goes through
    /// <see cref="PageRotation"/> — which at <paramref name="rotationDegrees"/>
    /// zero is the plain Y flip this always did.
    /// Boxes thinner than <paramref name="minimumExtent"/> device pixels are
    /// grown symmetrically — a rule has no thickness and a line of text is a
    /// couple of pixels tall on a page thumbnail, so either would otherwise be
    /// invisible.
    /// </summary>
    public static IReadOnlyList<RectangleF> MapToDisplay(
        Rectangle destination,
        double pageWidthPoints,
        double pageHeightPoints,
        int rotationDegrees,
        IReadOnlyList<RectangleF> boxesInPoints,
        float minimumExtent)
    {
        if (boxesInPoints.Count == 0 || pageWidthPoints <= 0 || pageHeightPoints <= 0)
        {
            return Array.Empty<RectangleF>();
        }

        // Scaled against the page as DISPLAYED: on a quarter turn the rendered
        // bitmap is the page on its side, so its width answers to the page's
        // height.
        var (displayWidth, displayHeight) =
            PageRotation.DisplaySize(pageWidthPoints, pageHeightPoints, rotationDegrees);
        double sx = destination.Width / displayWidth;
        double sy = destination.Height / displayHeight;
        var rects = new List<RectangleF>(boxesInPoints.Count);
        foreach (var box in boxesInPoints)
        {
            var displayed = PageRotation.ToDisplay(
                new PageRegion(box.X, box.Y, box.Width, box.Height),
                pageWidthPoints, pageHeightPoints, rotationDegrees);
            var rect = new RectangleF(
                destination.X + (float)(displayed.X * sx),
                destination.Y + (float)(displayed.Y * sy),
                (float)(displayed.Width * sx),
                (float)(displayed.Height * sy));
            if (rect.Width < minimumExtent) rect.Inflate((minimumExtent - rect.Width) / 2, 0);
            if (rect.Height < minimumExtent) rect.Inflate(0, (minimumExtent - rect.Height) / 2);
            // A box drawn partly off the page must not make the repaint read
            // outside the bitmap.
            rect.Intersect(destination);
            if (rect.Width >= 1 && rect.Height >= 1) rects.Add(rect);
        }
        return rects;
    }
}
