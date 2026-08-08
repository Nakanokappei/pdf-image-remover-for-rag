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
/// is this object drawn?") and the objects panel's preview ("what would be baked
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
    /// Draw a page and mark places on it: the picture, a frame around it, an
    /// outline per place, and an arrow at each place too small to find. This
    /// whole sequence is what the two windows share — running it from one place
    /// is what stops them drifting into two visual languages for the same
    /// question.
    /// </summary>
    /// <param name="maxPenWidth">See <see cref="DrawOutlines"/>.</param>
    /// <param name="dimOutsideBoxes">
    /// True to hold everything but the marked places back, which is how the
    /// usage window says "here, on this page". The objects panel's preview
    /// passes false: the page it shows is the page as the save will write it,
    /// and dimming most of it would read as a judgement on the rest.
    /// </param>
    public static void DrawMarkedPage(
        Graphics g,
        Bitmap page,
        Rectangle display,
        IReadOnlyList<RectangleF> boxes,
        float maxPenWidth,
        bool dimOutsideBoxes)
    {
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        DrawPage(g, page, display, dimOutsideBoxes ? boxes : Array.Empty<RectangleF>());

        using (var frame = new Pen(SystemColors.ControlDark))
        {
            g.DrawRectangle(frame, display.X, display.Y, display.Width - 1, display.Height - 1);
        }
        DrawOutlines(g, boxes, maxPenWidth);
        DrawPointers(g, display, boxes);
    }

    /// <summary>
    /// Draw the page into <paramref name="destination"/>, dimmed everywhere
    /// except inside <paramref name="boxes"/>. With no boxes the page is drawn
    /// normally: there is nothing to point at, and dimming the whole thing would
    /// only say "nothing here".
    /// </summary>
    static void DrawPage(
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
    static void DrawOutlines(
        Graphics g, IReadOnlyList<RectangleF> boxes, float maxPenWidth)
    {
        if (boxes.Count == 0) return;

        var color = HighlightColour;
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

    /// <summary>The one light blue, so an outline and an arrow cannot differ.</summary>
    static Color HighlightColour => SystemInformation.HighContrast
        ? SystemColors.Highlight : Color.FromArgb(0x1E, 0x90, 0xFF);

    /// <summary>
    /// How small a box has to be before it is pointed at as well as outlined,
    /// as a fraction of the page's longer side. Two centimetres on A4 is 6.7 %
    /// of its height, and that is the size a reader reported not being able to
    /// find on a page this size — so the threshold is a shade above it.
    ///
    /// EITHER side counts, not the larger one. A rule across the page is 7 mm
    /// tall and just as easy to miss as a stamp is: thin is thin whichever way
    /// it runs.
    ///
    /// A fraction of the DISPLAYED page rather than a length in points: what is
    /// being answered is whether the eye can find it in this picture, and the
    /// picture is the same size whatever paper the page claims to be.
    /// </summary>
    const float PointerThreshold = 0.07f;

    /// <summary>
    /// Put an arrow beside every box too small to notice, in the same light
    /// blue as the outline.
    ///
    /// The arrow lies on the side of the box with more room, which for a small
    /// box is the far side from the nearest corner of the page: one in the
    /// top-left quarter is pointed at from below and to its right. That is what
    /// keeps every arrow on the page — the roomier half is at least half the
    /// page, and an arrow is a seventh of it.
    ///
    /// A box that is only thin ONE way is pointed at only that way: a rule
    /// across the page gets an arrow from above or below, aimed at its middle,
    /// because it needs no help being found from side to side.
    /// </summary>
    /// <param name="page">The page as displayed, which the halves divide.</param>
    static void DrawPointers(Graphics g, Rectangle page, IReadOnlyList<RectangleF> boxes)
    {
        if (boxes.Count == 0 || page.Width <= 0 || page.Height <= 0) return;

        float small = Math.Max(page.Width, page.Height) * PointerThreshold;
        // Proportional to the picture, so the arrow is the same gesture at any
        // size the page happens to be drawn at.
        float length = Math.Max(8f, Math.Min(page.Width, page.Height) * 0.12f);

        var colour = HighlightColour;
        var saved = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // One arrow per PLACE, not per box. A table's five rules are five thin
        // boxes a few pixels apart, each pointed at from the same side, and the
        // arrows landed one on top of another as a totem pole — five times the
        // ink to say what one arrow said.
        var pointedAt = new List<PointF>();
        foreach (var box in boxes)
        {
            if (Math.Min(box.Width, box.Height) > small) continue;

            // Within an arrow's own length of one already drawn is the same
            // place: an arrow is what it is pointing at, and two of them that
            // close are one gesture drawn twice.
            var tip = Aim(page, box, length, small);
            float apart = length * 2;
            if (pointedAt.Any(p => Math.Abs(p.X - tip.X) < apart && Math.Abs(p.Y - tip.Y) < apart))
            {
                continue;
            }
            pointedAt.Add(tip);
            DrawPointer(g, page, box, colour, length, small);
        }
        g.SmoothingMode = saved;
    }

    /// <summary>
    /// Which side of the box the arrow lies on: +1 means to the right of / below
    /// it. Which side has more room is the same question as which half of the
    /// page the box sits in, and it is the one that still answers for a box
    /// spanning the page. Zero means the box is long that way and needs no
    /// offset — the arrow meets it at its middle instead.
    /// </summary>
    static PointF Side(Rectangle page, RectangleF box, float small) => new(
        box.Width >= small ? 0
            : page.Right - box.Right > box.Left - page.Left ? 1 : -1,
        box.Height >= small ? 0
            : page.Bottom - box.Bottom > box.Top - page.Top ? 1 : -1);

    /// <summary>
    /// Where the arrow's tip would go for this box — the same arithmetic the
    /// drawing does, so "have I already pointed here" is asked of the answer
    /// rather than of a guess at it.
    /// </summary>
    static PointF Aim(Rectangle page, RectangleF box, float length, float small)
    {
        var side = Side(page, box, small);
        float gap = length / 5f;
        return new PointF(
            side.X > 0 ? box.Right + gap : side.X < 0 ? box.Left - gap : box.X + (box.Width / 2f),
            side.Y > 0 ? box.Bottom + gap : side.Y < 0 ? box.Top - gap : box.Y + (box.Height / 2f));
    }

    /// <summary>One arrow: a shaft out from the box and a head back at it.</summary>
    static void DrawPointer(
        Graphics g, Rectangle page, RectangleF box, Color colour, float length, float small)
    {
        var side = Side(page, box, small);
        float dx = side.X;
        float dy = side.Y;

        // The tip stops short of the box so the outline stays readable, and the
        // tail runs out from there.
        var tip = Aim(page, box, length, small);
        var tail = new PointF(tip.X + (dx * length), tip.Y + (dy * length));

        // The arrow is ONE closed shape — a shaft and a head described as a
        // single outline — and that is what makes the white edge possible.
        //
        // Drawn as a line plus a triangle it needed two pens, and each seam
        // between them showed: the head wore the shaft's whole width as a
        // collar, and the shaft's round cap peeped past the tip as a bead. The
        // classic answer to an outline is to stamp the silhouette eight times
        // around the shape and draw over it, which is a bitmap-era trick; with
        // a path there is a boundary to stroke, so it is stroked once.
        float thickness = Math.Max(1.5f, length / 12f);
        float head = length * 0.4f;
        float norm = MathF.Sqrt((dx * dx) + (dy * dy));

        // Along the shaft towards the box, and across it.
        var along = new PointF(-dx / norm, -dy / norm);
        var across = new PointF(-along.Y, along.X);
        var baseCentre = new PointF(tip.X - (along.X * head), tip.Y - (along.Y * head));

        float shaft = thickness / 2;
        float barb = head * 0.45f;
        PointF At(PointF from, float sideways) =>
            new(from.X + (across.X * sideways), from.Y + (across.Y * sideways));

        using var arrow = new GraphicsPath();
        arrow.AddPolygon(new[]
        {
            At(tail, shaft), At(baseCentre, shaft), At(baseCentre, barb),
            tip,
            At(baseCentre, -barb), At(baseCentre, -shaft), At(tail, -shaft),
        });

        // One pixel of white, centred on the boundary, so half of it lies
        // outside the arrow. Its job is to stop the blue meeting a colour like
        // itself — anything wider draws a second, white arrow.
        const float halo = 1f;
        using (var edge = new Pen(Color.White, halo * 2) { LineJoin = LineJoin.Round })
        {
            g.DrawPath(edge, arrow);
        }
        using (var brush = new SolidBrush(colour))
        {
            g.FillPath(brush, arrow);
        }
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
