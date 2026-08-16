using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace PdfImageRemoverForRag.App;

/// <summary>
/// The figure the settings window shows: a chart with text set into it at the
/// sizes that decide whether a resolution is usable.
///
/// A chart on its own cannot answer the question the resolution list is really
/// asking. Bars and rules survive almost any reduction; what breaks first is
/// small text, and how small is too small depends on the writing system - the
/// numbers in that list came from measuring exactly that. So the specimen is
/// composed here, at run time, in the language the window is speaking, rather
/// than drawn into the picture once at build time in somebody else's alphabet.
/// </summary>
internal static class FigureSample
{
    /// <summary>
    /// The sizes a document sets its small text in. 6 pt is where the ladder
    /// starts because it is the smallest size anyone actually prints; 12 pt is
    /// body text and should survive everything.
    /// </summary>
    static readonly int[] PointSizes = { 6, 8, 10, 12 };

    /// <summary>Space around the specimen, in the sample's own pixels.</summary>
    const int Margin = 28;
    const int LineGap = 10;

    /// <summary>
    /// The chart with the specimen under it, as PNG bytes the preview can treat
    /// like any other sample. Returns the chart untouched if anything here
    /// fails: a settings window with no specimen is worse than one with, and
    /// far better than one that will not open.
    /// </summary>
    public static byte[] Compose(byte[] chartPng, string phrase, double widthInches, int height)
    {
        try
        {
            using var source = new MemoryStream(chartPng);
            using var chart = new Bitmap(source);

            // The sample stands for a picture this many inches wide, so this is
            // the resolution its own pixels are at, and a point is this many of
            // them. Everything below is drawn at the size it would really be.
            double dpi = chart.Width / widthInches;
            var family = SystemFonts.MessageBoxFont?.FontFamily ?? FontFamily.GenericSansSerif;
            var fonts = PointSizes
                .Select(points => new Font(
                    family, (float)(points / 72.0 * dpi), GraphicsUnit.Pixel))
                .ToArray();

            try
            {
                using var canvas = new Bitmap(chart.Width, height, PixelFormat.Format24bppRgb);
                using (var graphics = Graphics.FromImage(canvas))
                {
                    graphics.Clear(Color.White);
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    // Anti-aliased without hinting: hinting snaps stems to a
                    // pixel grid that this picture will be resampled off anyway,
                    // and the reader is here to judge the resampling.
                    graphics.TextRenderingHint = TextRenderingHint.AntiAlias;

                    int specimen = SpecimenHeight(graphics, fonts);
                    DrawChart(graphics, chart, canvas.Width, canvas.Height - specimen);
                    DrawSpecimen(graphics, fonts, phrase, canvas.Height - specimen + Margin);
                }

                using var output = new MemoryStream();
                canvas.Save(output, ImageFormat.Png);
                return output.ToArray();
            }
            finally
            {
                foreach (var font in fonts) font.Dispose();
            }
        }
        catch (Exception)
        {
            return chartPng;
        }
    }

    /// <summary>How much room the lines of text need, margins included.</summary>
    static int SpecimenHeight(Graphics graphics, Font[] fonts) =>
        fonts.Sum(font => (int)Math.Ceiling(font.GetHeight(graphics)))
        + (LineGap * (fonts.Length - 1))
        + (Margin * 2);

    /// <summary>
    /// The chart, as large as it fits in what the text left, and centred in it.
    /// </summary>
    static void DrawChart(Graphics graphics, Bitmap chart, int width, int height)
    {
        if (height <= 0) return;

        double scale = Math.Min(width / (double)chart.Width, height / (double)chart.Height);
        int drawnWidth = Math.Max(1, (int)Math.Round(chart.Width * scale));
        int drawnHeight = Math.Max(1, (int)Math.Round(chart.Height * scale));
        graphics.DrawImage(chart, new Rectangle(
            (width - drawnWidth) / 2, (height - drawnHeight) / 2, drawnWidth, drawnHeight));
    }

    /// <summary>
    /// One line per size, each labelled with its own. The label is what turns
    /// the block from a decoration into a reading: the question is not whether
    /// text survives but which sizes of it do.
    /// </summary>
    static void DrawSpecimen(Graphics graphics, Font[] fonts, string phrase, int top)
    {
        using var ink = new SolidBrush(Color.Black);
        for (int at = 0; at < fonts.Length; at++)
        {
            graphics.DrawString($"{PointSizes[at]} pt   {phrase}", fonts[at], ink, Margin, top);
            top += (int)Math.Ceiling(fonts[at].GetHeight(graphics)) + LineGap;
        }
    }
}
