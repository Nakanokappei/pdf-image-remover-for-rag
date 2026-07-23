using System.Drawing.Drawing2D;
using PdfImageRemoverForRag.Core.Models;
using PdfImageRemoverForRag.Core.Validation;

namespace PdfImageRemoverForRag.App;

/// <summary>
/// Display formatting for the 画像一覧 (image list) — everything that turns a
/// <see cref="CrossFileImageGroup"/> into cell text, tooltips, or images
/// lives here so MainForm stays layout-and-events only. All strings come
/// from <see cref="L10n"/>.
/// </summary>
internal static class ImageListRow
{
    /// <summary>
    /// Short text for the 警告 column. Unsafe wins over full-page because it
    /// changes what the user can do (the checkbox is disabled).
    /// </summary>
    public static string WarningLabel(CrossFileImageGroup group)
    {
        if (!group.IsSafelyRemovable) return L10n.WarningNotRemovable;
        if (group.IsPossibleFullPageImage) return L10n.WarningFullPage;
        return string.Empty;
    }

    /// <summary>Hover text for the 警告 column; both §7 and §14.3 texts can apply.</summary>
    public static string WarningToolTip(CrossFileImageGroup group)
    {
        var parts = new List<string>(2);
        if (!group.IsSafelyRemovable) parts.Add(L10n.TooltipUnsafe);
        if (group.IsPossibleFullPageImage) parts.Add(L10n.TooltipFullPage);
        return string.Join("\n\n", parts);
    }

    /// <summary>
    /// The string drawn in the thumbnail cell/tile for text groups, or null
    /// for images and shapes (which use a real bitmap). Only text is rendered
    /// as text.
    /// </summary>
    public static string? ThumbnailText(CrossFileImageGroup group) =>
        group.Kind == RemovableKind.Text ? group.TextValue ?? string.Empty : null;

    /// <summary>タイプ cell: image / text / shape, localized.</summary>
    public static string TypeLabel(CrossFileImageGroup group) => group.Kind switch
    {
        RemovableKind.Text => L10n.TypeText,
        RemovableKind.Shape => L10n.TypeShape,
        _ => L10n.TypeImage,
    };

    /// <summary>サイズ cell: "W×H" px (image), char count (text), or "W×H pt" (shape).</summary>
    public static string SizeLabel(CrossFileImageGroup group) => group.Kind switch
    {
        RemovableKind.Text => L10n.TextSize(group.TextValue?.Length ?? 0),
        RemovableKind.Shape => L10n.ShapeSize(group.PixelWidth, group.PixelHeight),
        _ => $"{group.PixelWidth}×{group.PixelHeight}",
    };

    /// <summary>圧縮 cell: filter label for images, "N/A" for text and shapes
    /// (compression is an image-only attribute).</summary>
    public static string CompressionLabel(CrossFileImageGroup group) => group.Kind switch
    {
        RemovableKind.Image => CompressionLabel(group.Compression),
        _ => L10n.CompressionNotApplicable,
    };

    /// <summary>
    /// Map PDF filter names to the short labels the 圧縮 column shows
    /// ("JPEG、Flateなど" per spec §11.3). Chained filters keep the "+".
    /// </summary>
    public static string CompressionLabel(string filterName)
    {
        return string.Join("+", filterName.Split('+').Select(MapSingleFilter));

        static string MapSingleFilter(string filter) => filter switch
        {
            "/DCTDecode" => "JPEG",
            "/FlateDecode" => "Flate",
            "/JPXDecode" => "JPEG2000",
            "/CCITTFaxDecode" => "CCITT",
            "/JBIG2Decode" => "JBIG2",
            "/LZWDecode" => "LZW",
            "/RunLengthDecode" => "RunLength",
            "Raw" => "Raw",
            _ => filter.TrimStart('/'),
        };
    }

    /// <summary>
    /// Decode a PNG thumbnail into a GDI+ image that owns its pixels (no
    /// live reference to the source stream), or null when decoding fails —
    /// the caller substitutes the placeholder icon (§12).
    /// </summary>
    public static Image? DecodeThumbnail(byte[]? thumbnailBytes)
    {
        if (thumbnailBytes is not { Length: > 0 } bytes) return null;

        // Read the declared dimensions before decoding. These bytes came out of
        // a PDF the user opened, and a few kilobytes can legally declare
        // 40000x40000 pixels — about 6 GB once decoded. Catching the
        // out-of-memory afterwards is no defence, because by then the
        // allocation has been attempted.
        if (!RasterImageHeader.IsSafeToDecode(bytes)) return null;

        try
        {
            // Image.FromStream keeps the stream alive internally, so copy
            // into a stream-independent Bitmap and dispose the original.
            // The format (PNG or JPEG) is auto-detected by GDI+.
            using var stream = new MemoryStream(bytes);
            using var decoded = Image.FromStream(stream);
            return new Bitmap(decoded);
        }
        catch
        {
            // Malformed bytes must not break the list — fall back to the
            // placeholder icon (§12: サムネイル失敗だけで解析を中止しない).
            return null;
        }
    }

    /// <summary>
    /// Scale-to-fit copy (aspect preserved, never upscaled) — the grid and
    /// the tile view each hold their own appropriately-sized bitmap instead
    /// of sharing one full-resolution image.
    /// </summary>
    public static Image CreateScaledCopy(Image source, int maxWidth, int maxHeight)
    {
        double scale = Math.Min(1.0, Math.Min(
            (double)maxWidth / source.Width,
            (double)maxHeight / source.Height));
        int width = Math.Max(1, (int)(source.Width * scale));
        int height = Math.Max(1, (int)(source.Height * scale));

        // Two passes, and never an intermediate bigger than twice the target.
        //
        // Going straight to HighQualityBicubic costs time proportional to the
        // SOURCE size, which made a 2,000-object document take minutes. The
        // first attempt at fixing that halved the image repeatedly, but the
        // first half-size copy of a 5000px scan is itself ~100 MB, and the
        // batch ran out of memory partway through. So: one cheap pass straight
        // down to a small intermediate, then one quality pass from there.
        // Extreme reductions alias slightly in the cheap pass; at 90x64 that is
        // not visible, and memory stays bounded no matter how large the source.
        int interWidth = Math.Min(source.Width, width * 2);
        int interHeight = Math.Min(source.Height, height * 2);

        using var intermediate = new Bitmap(interWidth, interHeight);
        using (var fast = Graphics.FromImage(intermediate))
        {
            fast.InterpolationMode = InterpolationMode.Bilinear;
            fast.PixelOffsetMode = PixelOffsetMode.Half;
            fast.DrawImage(source, 0, 0, interWidth, interHeight);
        }

        var bitmap = new Bitmap(width, height);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.Half;
            graphics.DrawImage(intermediate, 0, 0, width, height);
        }
        return bitmap;
    }

    /// <summary>Brightness threshold above which a shape needs a dark background.</summary>
    static readonly double LightGrayLuminance = new RgbColor(211, 211, 211).Luminance;

    /// <summary>
    /// Render a shape's geometry into a thumbnail using its real colors: fit
    /// its bounding box into the padded area (aspect preserved), flip Y (PDF
    /// origin is bottom-left), and stroke/fill the path. Shapes brighter than
    /// light gray get a black background so they stay visible. The caller owns
    /// (and must dispose) the image.
    /// </summary>
    public static Image CreateShapeThumbnail(ShapeGeometry geometry, int width, int height)
    {
        bool fill = geometry.PaintOperator is "f" or "F" or "f*" or "B" or "B*" or "b" or "b*";
        bool stroke = geometry.PaintOperator is "S" or "s" or "B" or "B*" or "b" or "b*";

        // Default PDF drawing color is black. The "representative" color (for
        // the background decision) is the one actually painted.
        var strokeColor = ToColor(geometry.StrokeColor, Color.Black);
        var fillColor = ToColor(geometry.FillColor, Color.Black);
        var representative = fill ? geometry.FillColor : geometry.StrokeColor;
        bool needsDarkBackground = representative is { } rep && rep.Luminance > LightGrayLuminance;

        var bitmap = new Bitmap(width, height);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(needsDarkBackground ? Color.Black : Color.White);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (var border = new Pen(needsDarkBackground ? Color.FromArgb(80, 80, 80) : Color.Gainsboro))
        {
            graphics.DrawRectangle(border, 0, 0, width - 1, height - 1);
        }

        const int pad = 6;
        var area = new RectangleF(pad, pad, width - 2 * pad, height - 2 * pad);
        double scale = 1;
        if (geometry.Width > 0) scale = area.Width / geometry.Width;
        if (geometry.Height > 0) scale = Math.Min(scale, area.Height / geometry.Height);
        if (scale <= 0 || double.IsInfinity(scale)) scale = 1;

        // Center the scaled bbox; flip Y so the shape draws upright.
        float offsetX = area.X + (float)(area.Width - geometry.Width * scale) / 2;
        float offsetY = area.Y + (float)(area.Height - geometry.Height * scale) / 2;
        PointF Map(PointD p) => new(
            (float)(offsetX + p.X * scale),
            (float)(offsetY + (geometry.Height - p.Y) * scale));

        using var path = BuildGraphicsPath(geometry, Map);
        if (fill)
        {
            using var brush = new SolidBrush(fillColor);
            graphics.FillPath(brush, path);
        }
        if (stroke || !fill)
        {
            using var pen = new Pen(strokeColor, 1.4f);
            graphics.DrawPath(pen, path);
        }
        return bitmap;
    }

    static Color ToColor(RgbColor? color, Color fallback) =>
        color is { } c ? Color.FromArgb(c.R, c.G, c.B) : fallback;

    static GraphicsPath BuildGraphicsPath(ShapeGeometry geometry, Func<PointD, PointF> map)
    {
        var path = new GraphicsPath();
        PointF current = default;
        bool hasCurrent = false;
        foreach (var element in geometry.Elements)
        {
            var pts = element.Points;
            switch (element.Operator)
            {
                case "m":
                    if (pts.Count >= 1) { current = map(pts[0]); hasCurrent = true; path.StartFigure(); }
                    break;
                case "l":
                    if (hasCurrent && pts.Count >= 1) { var to = map(pts[0]); path.AddLine(current, to); current = to; }
                    break;
                case "c":
                    if (hasCurrent && pts.Count >= 3)
                    {
                        var c1 = map(pts[0]); var c2 = map(pts[1]); var end = map(pts[2]);
                        path.AddBezier(current, c1, c2, end); current = end;
                    }
                    break;
                case "v":
                    if (hasCurrent && pts.Count >= 2)
                    {
                        var c2 = map(pts[0]); var end = map(pts[1]);
                        path.AddBezier(current, current, c2, end); current = end;
                    }
                    break;
                case "y":
                    if (hasCurrent && pts.Count >= 2)
                    {
                        var c1 = map(pts[0]); var end = map(pts[1]);
                        path.AddBezier(current, c1, end, end); current = end;
                    }
                    break;
                case "re":
                    if (pts.Count >= 4)
                    {
                        var mapped = pts.Select(map).ToArray();
                        float x = mapped.Min(p => p.X), y = mapped.Min(p => p.Y);
                        float w = mapped.Max(p => p.X) - x, h = mapped.Max(p => p.Y) - y;
                        path.StartFigure();
                        path.AddRectangle(new RectangleF(x, y, Math.Max(w, 1), Math.Max(h, 1)));
                        hasCurrent = false;
                    }
                    break;
                case "h":
                    path.CloseFigure();
                    break;
            }
        }
        return path;
    }

    /// <summary>
    /// Gray placeholder with a "?" — shown when thumbnail generation failed.
    /// The caller owns (and must dispose) the returned image.
    /// </summary>
    public static Image CreatePlaceholderIcon(int width = 64, int height = 48)
    {
        var bitmap = new Bitmap(width, height);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Gainsboro);
        graphics.DrawRectangle(Pens.Silver, 0, 0, width - 1, height - 1);
        using var font = new Font(FontFamily.GenericSansSerif, 14, FontStyle.Bold);
        var text = "?";
        var textSize = graphics.MeasureString(text, font);
        graphics.DrawString(text, font, Brushes.Gray,
            (width - textSize.Width) / 2, (height - textSize.Height) / 2);
        return bitmap;
    }
}
