using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace PdfImageRemoverForRag.App;

/// <summary>
/// One place an object is used: a file, a page in it, and the object's
/// bounding boxes on that page in PDF points. Images, text and shapes all
/// carry rectangles; a row without any (an object whose geometry could not be
/// resolved) still shows which file and page, just without an outline.
/// </summary>
internal sealed record UsageRow(
    string FilePath,
    string FileDisplayName,
    int PageNumber,
    IReadOnlyList<RectangleF> BoxesInPoints);

/// <summary>
/// The window opened from a row/tile's right-click menu: every file and page
/// where the object is used, each as a full-page picture with the object's
/// location outlined in light blue, captioned underneath with the file name
/// (no extension) and the page number.
///
/// Pages are rasterized on demand by <see cref="PdfPageRenderer"/> as they
/// scroll into view and disposed as they leave, so a logo used on 200 pages
/// costs a screenful of bitmaps, not 200 — the same viewport-bounded memory
/// policy the main window follows.
/// </summary>
internal sealed class UsageLocationsDialog : Form
{
    public UsageLocationsDialog(string title, IReadOnlyList<UsageRow> rows)
    {
        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;

        var list = new UsageListPanel(rows) { Dock = DockStyle.Fill };
        Controls.Add(list);

        using (var iconStream = typeof(MainForm).Assembly.GetManifestResourceStream("appicon.ico"))
        {
            if (iconStream is not null) Icon = new Icon(iconStream);
        }
    }

    // Logical (96-DPI) window size. Kept as it was when the page grew to fill
    // the row: the window is a comfortable size to open on, and the page is
    // centred in whatever width it is given.
    static readonly Size LogicalClientSize = new(622, 680);
    static readonly Size LogicalMinimumSize = new(420, 360);

    /// <summary>
    /// Size the window in device units. This form is hand-built with no
    /// AutoScaleDimensions, so WinForms' scaling pass never runs on it, while
    /// everything inside is drawn through Dip() — set here, the window would
    /// stay 96-DPI sized around 200 % content, which is what squeezed the file
    /// name into one character per line at 200 %.
    /// </summary>
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        MinimumSize = LogicalToDeviceUnits(LogicalMinimumSize);
        ClientSize = LogicalToDeviceUnits(LogicalClientSize);
    }

    /// <summary>
    /// Escape closes the window. There is no button to make the form's
    /// CancelButton, and the scrolling list has the focus, so the key is taken
    /// here before it reaches it.
    /// </summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            Close();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    /// <summary>
    /// The scrolling, owner-drawn list of usage rows. Owner-drawn and
    /// virtualized for the same reason the tile view is: one control painting a
    /// handful of visible rows, never one control per row.
    /// </summary>
    sealed class UsageListPanel : Panel
    {
        // Logical (96-DPI) metrics; everything drawn goes through Dip().
        //
        // The page fills the row's width and the words go underneath it, as a
        // caption. It was a two-column row — page on the left, file name and
        // page number to its right — and the column of text was reserving a
        // third of the window for two short lines, at the expense of the one
        // thing the window exists to show. Three times the area (212*283 =
        // 59,996 -> 367*490 = 179,830), which is the same page and the same
        // outline at 1.73x.
        const int ThumbWidth = 367;
        const int ThumbHeight = 490;
        const int RowPadding = 12;
        const int CaptionGap = 8;

        readonly IReadOnlyList<UsageRow> _rows;
        readonly PdfPageRenderer _renderer = new();
        // Rendered pages keyed by row index. Bounded to the viewport: entries
        // far off screen are disposed.
        readonly Dictionary<int, RenderedPage> _rendered = new();
        bool _pumping;
        bool _closed;

        public UsageListPanel(IReadOnlyList<UsageRow> rows)
        {
            _rows = rows;
            AutoScroll = true;
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                   | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = SystemColors.Window;
        }

        int Dip(int logical) => LogicalToDeviceUnits(logical);

        /// <summary>
        /// The two caption lines. Measured from the font rather than from a
        /// constant, because the font is what decides it — and the font is
        /// already scaled for the monitor, so this must NOT go through Dip().
        /// </summary>
        int CaptionHeight => (Font.Height * 2) + Dip(2);

        int RowHeight =>
            Dip(ThumbHeight) + Dip(CaptionGap) + CaptionHeight + (Dip(RowPadding) * 2);

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            AutoScrollMinSize = new Size(0, _rows.Count * RowHeight);
            EnsureRenders();
        }

        (int First, int Count) VisibleRange()
        {
            if (_rows.Count == 0) return (0, 0);
            int scrolled = Math.Max(0, -AutoScrollPosition.Y);
            int first = Math.Max(0, scrolled / RowHeight);
            int count = (ClientSize.Height / RowHeight) + 2;
            return (first, Math.Min(count, _rows.Count - first));
        }

        Rectangle RowBounds(int index) => new(
            0, (index * RowHeight) + AutoScrollPosition.Y, ClientSize.Width, RowHeight);

        protected override void OnScroll(ScrollEventArgs se)
        {
            base.OnScroll(se);
            Invalidate();
            EnsureRenders();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            Invalidate();
            EnsureRenders();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var (first, count) = VisibleRange();
            for (int i = first; i < first + count; i++)
            {
                DrawRow(e.Graphics, i);
            }
        }

        void DrawRow(Graphics g, int index)
        {
            var row = _rows[index];
            var bounds = RowBounds(index);

            // Centred, and never wider than the window: the page is the row now,
            // so a narrow window shrinks it rather than clipping it.
            int thumbWidth = Math.Min(Dip(ThumbWidth), bounds.Width - (Dip(RowPadding) * 2));
            var thumbBox = new Rectangle(
                bounds.Left + ((bounds.Width - thumbWidth) / 2), bounds.Top + Dip(RowPadding),
                Math.Max(1, thumbWidth), Dip(ThumbHeight));

            // Thumbnail (or a placeholder while it renders), with the object's
            // location(s) outlined once the page is available.
            if (_rendered.TryGetValue(index, out var page))
            {
                var disp = FitInside(page.Bitmap.Size, thumbBox);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                var boxes = LocationRects(disp, page, row);
                PageHighlightPainter.DrawPage(g, page.Bitmap, disp, boxes);
                using (var frame = new Pen(SystemColors.ControlDark))
                {
                    g.DrawRectangle(frame, disp.X, disp.Y, disp.Width - 1, disp.Height - 1);
                }
                PageHighlightPainter.DrawOutlines(g, boxes, Dip(2));
                // An outline this small is findable only if something points at
                // it: at this size a 2 cm square on A4 is a few millimetres of
                // screen, and the reader is looking for it on a whole page.
                PageHighlightPainter.DrawPointers(g, disp, boxes);
            }
            else
            {
                using var back = new SolidBrush(SystemColors.ControlLight);
                g.FillRectangle(back, thumbBox);
                using var frame = new Pen(SystemColors.ControlDark);
                g.DrawRectangle(frame, thumbBox);
                TextRenderer.DrawText(g, L10n.ThumbnailPending, Font, thumbBox,
                    SystemColors.GrayText,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                    | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
            }

            // The caption: file name (no extension) on top, page number under
            // it, both centred on the page above them. It spans the whole row
            // rather than the picture, so a long file name has the width to
            // show itself before the ellipsis takes over.
            var nameLine = new Rectangle(
                bounds.Left + Dip(RowPadding), thumbBox.Bottom + Dip(CaptionGap),
                Math.Max(0, bounds.Width - (Dip(RowPadding) * 2)), Font.Height);
            using (var nameFont = new Font(Font, FontStyle.Bold))
            {
                TextRenderer.DrawText(g, row.FileDisplayName, nameFont, nameLine,
                    SystemColors.WindowText,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis);
            }
            var pageLine = nameLine with { Y = nameLine.Bottom + Dip(2) };
            TextRenderer.DrawText(g, L10n.UsagePageLabel(row.PageNumber), Font, pageLine,
                SystemColors.GrayText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis);

            // Separator under the row.
            using var line = new Pen(SystemColors.ControlLight);
            g.DrawLine(line, bounds.Left + Dip(RowPadding), bounds.Bottom - 1,
                bounds.Right - Dip(RowPadding), bounds.Bottom - 1);
        }

        /// <summary>
        /// The row's bounding boxes as display rectangles, clipped to the page.
        /// </summary>
        IReadOnlyList<RectangleF> LocationRects(Rectangle disp, RenderedPage page, UsageRow row) =>
            PageHighlightPainter.MapToDisplay(
                disp, page.PageWidthPoints, page.PageHeightPoints, page.RotationDegrees,
                row.BoxesInPoints, Dip(4));

        static Rectangle FitInside(Size imageSize, Rectangle area)
        {
            double scale = Math.Min(1.0, Math.Min(
                (double)area.Width / imageSize.Width,
                (double)area.Height / imageSize.Height));
            int w = Math.Max(1, (int)(imageSize.Width * scale));
            int h = Math.Max(1, (int)(imageSize.Height * scale));
            return new Rectangle(
                area.X + ((area.Width - w) / 2), area.Y + ((area.Height - h) / 2), w, h);
        }

        /// <summary>
        /// Render the visible rows that have no bitmap yet, one at a time, and
        /// drop rendered pages that have scrolled well out of view. Re-entrant
        /// guarded by <see cref="_pumping"/>; restarted by every scroll.
        /// </summary>
        async void EnsureRenders()
        {
            if (_pumping || _closed) return;
            _pumping = true;
            try
            {
                while (!_closed)
                {
                    var (first, count) = VisibleRange();
                    int target = -1;
                    for (int i = first; i < first + count; i++)
                    {
                        if (!_rendered.ContainsKey(i)) { target = i; break; }
                    }
                    if (target < 0) break;

                    int widthPx = Dip(ThumbWidth);
                    var page = await _renderer.RenderAsync(
                        _rows[target].FilePath, _rows[target].PageNumber, widthPx);
                    if (_closed)
                    {
                        page?.Bitmap.Dispose();
                        return;
                    }
                    if (page is not null)
                    {
                        _rendered[target] = page;
                        Invalidate(RowBounds(target));
                    }
                    else
                    {
                        // Nothing to draw, but mark it done so the pump moves on;
                        // an empty page (1x1) reads as "rendered, no box".
                        _rendered[target] = new RenderedPage(new Bitmap(1, 1), 0, 0);
                    }
                    EvictOffscreen();
                }
            }
            finally
            {
                _pumping = false;
            }
        }

        void EvictOffscreen()
        {
            var (first, count) = VisibleRange();
            int keepFrom = first - count;
            int keepTo = first + (count * 2);
            var drop = _rendered.Keys.Where(k => k < keepFrom || k > keepTo).ToList();
            foreach (var key in drop)
            {
                _rendered[key].Bitmap.Dispose();
                _rendered.Remove(key);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _closed = true;
                foreach (var page in _rendered.Values) page.Bitmap.Dispose();
                _rendered.Clear();
            }
            base.Dispose(disposing);
        }
    }
}
