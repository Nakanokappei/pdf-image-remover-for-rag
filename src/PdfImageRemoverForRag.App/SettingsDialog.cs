using PdfImageRemoverForRag.Core.Models;

namespace PdfImageRemoverForRag.App;

/// <summary>
/// The settings window: how large the images in a saved PDF may be.
///
/// The quality control is a slider with a live preview rather than a number,
/// because the number means nothing to the person using this app. "85" is the
/// JPEG encoder's own vocabulary; someone preparing documents for a retrieval
/// pipeline has no way to turn it into an answer about their own file.
///
/// Two samples side by side, because a PDF holds pictures two ways and the
/// quality slider only reaches one of them. A photograph is stored as a JPEG,
/// so resizing it writes it again at the chosen quality and the slider is what
/// it costs. A figure is stored losslessly, and stays that way however the
/// slider moves - which is worth showing, because it is the promise that a
/// diagram will not come back with rings around its lines.
///
/// All three controls redraw them. Resolution is the one that matters most and
/// was the last to be wired: it decides how many pixels survive, which is the
/// question someone preparing a retrieval pipeline is actually asking.
///
/// Layout follows <see cref="AboutDialog"/>: auto-sizing panels rather than
/// absolute coordinates, and the few measurements that cannot come from
/// AutoSize go through <see cref="Control.LogicalToDeviceUnits(int)"/>.
/// </summary>
internal sealed class SettingsDialog : Form
{
    /// <summary>
    /// The size both samples end up at, 16:9. The photograph is cut to it; the
    /// figure is composed to it. The two bands are then the same shape and
    /// neither picture is letterboxed against the other.
    /// </summary>
    const int SampleWidth = 1366;
    const int SampleHeight = 768;
    const double SampleAspect = SampleWidth / (double)SampleHeight;

    /// <summary>
    /// How wide a page the samples stand for. A resolution is a limit per inch
    /// and says nothing at all until a physical size is named, so one is: three
    /// inches, about a third of the width of A4, which is the size a figure
    /// gets when it sits beside its text rather than across the page.
    ///
    /// It is also the largest size that keeps every setting honest. The samples
    /// are 1366 px across and nothing is ever enlarged, so a width past 3.4
    /// inches would put the top of the range beyond them - and two settings
    /// that hand back the same untouched picture look like a control that has
    /// stopped working.
    /// </summary>
    const double SampleWidthInches = 3.0;

    // Logical (96-DPI) metrics; scaled to device pixels in ApplyDpiDependentLayout.
    const int SliderWidth = 260;
    const int ComboPadding = 32;
    const int MinimumComboWidth = 220;
    const int ScreenMargin = 120;
    const int MaximumMagnification = 8;

    /// <summary>How short a sample band may be squeezed before it stops giving.</summary>
    const int MinimumSampleHeight = 60;

    /// <summary>The space between the two bands.</summary>
    const int SampleGap = 10;

    /// <summary>
    /// How narrow a band may get before it stops being worth looking at.
    ///
    /// The bands used to take their width from the resolution list, and at 100%
    /// that left them about a hundred pixels each - too small to see a JPEG
    /// artifact in, and far too small to read 6 pt text. They set the width of
    /// this window now, rather than the other way round.
    /// </summary>
    const int MinimumSampleWidth = 300;

    /// <summary>Does the imaging work, and the same instance a save would.</summary>
    static readonly WindowsImageResampler Resampler = new();

    readonly CheckBox _shrinkImages;
    readonly Label _resolutionLabel;
    readonly ComboBox _resolutionBox;
    readonly Label _qualityLabel;
    readonly TrackBar _qualitySlider;
    readonly Label _qualityValue;
    readonly Label _figureCaption;
    readonly SamplePreview _figurePreview;
    readonly Label _photoCaption;
    readonly SamplePreview _photoPreview;
    readonly Label _zoomLabel;
    readonly TrackBar _zoomSlider;
    readonly Label _zoomValue;
    readonly Label _description;

    readonly byte[]? _photoPng = LoadSample("quality-sample-photo.png");

    /// <summary>
    /// The chart with a specimen of the reader's own writing system set into
    /// it. Composed once here rather than on every tick of a slider: nothing
    /// about it depends on a setting.
    /// </summary>
    readonly byte[]? _figurePng = Compose(LoadSample("quality-sample-figure.png"));

    static byte[]? Compose(byte[]? chart) => chart is null
        ? null
        : FigureSample.Compose(chart, L10n.SettingsSampleText, SampleWidthInches, SampleHeight);

    /// <summary>What the user left the window with. Read after an OK result.</summary>
    public ImageReduction Reduction =>
        new(_shrinkImages.Checked, SelectedLimit, _qualitySlider.Value);

    SettingsDialog(ImageReduction current)
    {
        Text = L10n.SettingsTitle;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Font;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        // --- the switch ------------------------------------------------------
        _shrinkImages = new CheckBox
        {
            Text = L10n.SettingsShrinkImages,
            Checked = current.Enabled,
            AutoSize = true,
            TabIndex = 0,
            Margin = new Padding(0, 0, 0, 10),
            AccessibleName = Strip(L10n.SettingsShrinkImages),
        };
        _shrinkImages.CheckedChanged += (_, _) => ApplyEnabledState();

        // --- resolution ------------------------------------------------------
        _resolutionLabel = new Label
        {
            Text = L10n.SettingsResolution,
            AutoSize = true,
            TabIndex = 1,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 6, 8, 6),
        };
        _resolutionBox = new ComboBox
        {
            // A list, never a text field: every value here is one this app
            // knows how to apply, and there is nothing sensible to type.
            DropDownStyle = ComboBoxStyle.DropDownList,
            TabIndex = 2,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 3, 0, 3),
            AccessibleName = Strip(L10n.SettingsResolution),
        };
        // Ordered by resolution rather than by declaration, so the list reads as
        // the ladder it is and a number inserted later lands in its right place.
        foreach (var limit in Enum.GetValues<ImageSizeLimit>()
                     .OrderBy(ImageReduction.DpiOf))
        {
            _resolutionBox.Items.Add(new ResolutionEntry(limit));
        }
        _resolutionBox.SelectedIndex = IndexOf(current.SizeLimit);
        _resolutionBox.SelectedIndexChanged += (_, _) => RefreshPreview();

        // --- quality ---------------------------------------------------------
        _qualityLabel = new Label
        {
            Text = L10n.SettingsJpegQuality,
            AutoSize = true,
            TabIndex = 3,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 6, 8, 6),
        };
        _qualitySlider = new TrackBar
        {
            Minimum = ImageReduction.MinimumJpegQuality,
            Maximum = ImageReduction.MaximumJpegQuality,
            Value = current.JpegQuality,
            TickFrequency = 10,
            SmallChange = 1,
            LargeChange = 5,
            AutoSize = false,
            TabIndex = 4,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0),
            AccessibleName = Strip(L10n.SettingsJpegQuality),
        };
        // The number stays visible beside the slider. A slider alone cannot be
        // written down, and a setting that cannot be quoted cannot be compared
        // with a colleague's.
        _qualityValue = new Label
        {
            Text = current.JpegQuality.ToString(),
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(8, 10, 0, 0),
        };
        _qualitySlider.ValueChanged += (_, _) =>
        {
            _qualityValue.Text = _qualitySlider.Value.ToString();
            RefreshPreview();
        };

        var grid = new TableLayoutPanel
        {
            ColumnCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 0, 0, 8),
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.Controls.Add(_resolutionLabel, 0, 0);
        grid.Controls.Add(_resolutionBox, 1, 0);
        grid.Controls.Add(_qualityLabel, 0, 1);
        grid.Controls.Add(QualityRow(), 1, 1);

        // --- the two samples -------------------------------------------------
        // Side by side rather than stacked. They are meant to be compared, and
        // two pictures a screen apart are two pictures nobody compares; it also
        // halves the height, which is what let this window fit a 175% screen.
        _figureCaption = NewCaption();
        _figurePreview = new SamplePreview { TabIndex = 5, Margin = new Padding(0, 0, 0, 2) };
        // The gap between the two columns lives on the right-hand one, and is
        // the same figure ApplyDpiDependentLayout takes off the pair's width.
        _photoCaption = NewCaption();
        _photoCaption.Margin = new Padding(SampleGap, 0, 0, 2);
        _photoPreview = new SamplePreview { TabIndex = 6, Margin = new Padding(SampleGap, 0, 0, 2) };

        var samples = new TableLayoutPanel
        {
            ColumnCount = 2,
            RowCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 0, 0, 8),
        };
        samples.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        samples.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        // Both captions share a row, so however many lines the longer one wraps
        // to, the two pictures still start at the same height.
        samples.Controls.Add(_figureCaption, 0, 0);
        samples.Controls.Add(_photoCaption, 1, 0);
        samples.Controls.Add(_figurePreview, 0, 1);
        samples.Controls.Add(_photoPreview, 1, 1);

        // --- how far in ------------------------------------------------------
        // At life size the artifact is real, measurable and still below what an
        // eye resolves; magnified, the picture is no longer recognisable. So
        // neither is the default and the reader moves between them.
        _zoomLabel = new Label
        {
            Text = L10n.SettingsZoom,
            AutoSize = true,
            TabIndex = 7,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 6, 8, 6),
        };
        _zoomSlider = new TrackBar
        {
            Minimum = 1,
            Maximum = MaximumMagnification,
            Value = 1,
            TickFrequency = 1,
            SmallChange = 1,
            LargeChange = 1,
            AutoSize = false,
            TabIndex = 8,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0),
            AccessibleName = Strip(L10n.SettingsZoom),
        };
        // Reads out what is really on the screen, not where the slider sits.
        // The band is a few hundred pixels wide and the sample is over a
        // thousand, so the picture arrives already shrunk; calling the left end
        // of the slider "1x" would claim life size for something displayed at
        // under a third of it.
        _zoomValue = new Label
        {
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(8, 10, 0, 0),
        };
        _zoomSlider.ValueChanged += (_, _) =>
        {
            _figurePreview.Magnification = _zoomSlider.Value;
            _photoPreview.Magnification = _zoomSlider.Value;
            ShowDrawnScale();
        };

        var zoomRow = new TableLayoutPanel
        {
            ColumnCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 0, 0, 4),
        };
        zoomRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        zoomRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        zoomRow.Controls.Add(_zoomLabel, 0, 0);
        zoomRow.Controls.Add(ZoomRow(), 1, 0);

        // --- what it does and does not do ------------------------------------
        _description = new Label
        {
            Text = L10n.SettingsShrinkDescription,
            ForeColor = SystemColors.GrayText,
            AutoSize = true,
            Margin = new Padding(0, 6, 0, 0),
        };

        var groupBody = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            Padding = new Padding(12, 8, 12, 12),
        };
        groupBody.Controls.AddRange(new Control[]
        {
            _shrinkImages, grid, samples, zoomRow, _description,
        });

        var group = new GroupBox
        {
            Text = L10n.SettingsImagesGroup,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 0, 0, 16),
        };
        group.Controls.Add(groupBody);

        // --- footer ----------------------------------------------------------
        var okButton = new Button
        {
            Text = L10n.Ok,
            DialogResult = DialogResult.OK,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(88, 26),
            TabIndex = 9,
            Margin = new Padding(0, 0, 8, 0),
        };
        var cancelButton = new Button
        {
            Text = L10n.Cancel,
            DialogResult = DialogResult.Cancel,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(88, 26),
            TabIndex = 10,
            Margin = new Padding(0),
        };
        AcceptButton = okButton;
        CancelButton = cancelButton;

        var footer = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Anchor = AnchorStyles.Right,
            Margin = new Padding(0),
        };
        footer.Controls.AddRange(new Control[] { okButton, cancelButton });

        var root = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            Padding = new Padding(20),
        };
        root.Controls.Add(group);
        root.Controls.Add(footer);
        Controls.Add(root);

        ApplyEnabledState();
    }

    FlowLayoutPanel QualityRow() => Row(_qualitySlider, _qualityValue);

    FlowLayoutPanel ZoomRow() => Row(_zoomSlider, _zoomValue);

    static FlowLayoutPanel Row(params Control[] children)
    {
        var row = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Margin = new Padding(0),
        };
        row.Controls.AddRange(children);
        return row;
    }

    /// <summary>
    /// Put the real magnification beside the slider, as a percentage of life
    /// size. Both bands draw at the same scale, so one of them can answer for
    /// both. Not a translated string: a number and a percent sign read the
    /// same in every language on the list.
    /// </summary>
    void ShowDrawnScale() =>
        _zoomValue.Text = $"{Math.Round(_photoPreview.DrawnScale * 100)}%";

    static Label NewCaption() => new()
    {
        AutoSize = true,
        ForeColor = SystemColors.GrayText,
        Margin = new Padding(0, 0, 0, 2),
    };

    /// <summary>The bytes of one embedded sample, or null if it will not load.</summary>
    static byte[]? LoadSample(string resourceName)
    {
        try
        {
            using var stream = typeof(SettingsDialog).Assembly
                .GetManifestResourceStream(resourceName);
            if (stream is null) return null;

            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            return copy.ToArray();
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Everything below the switch only means something while the reduction is
    /// on. Disabled rather than hidden: what the file would be saved at is
    /// worth seeing even when it is not being applied.
    /// </summary>
    void ApplyEnabledState()
    {
        bool on = _shrinkImages.Checked;
        foreach (var control in new Control[]
                 {
                     _resolutionLabel, _resolutionBox, _qualityLabel, _qualitySlider,
                     _qualityValue, _figureCaption, _figurePreview, _photoCaption,
                     _photoPreview, _zoomLabel, _zoomSlider, _zoomValue,
                 })
        {
            control.Enabled = on;
        }
    }

    /// <summary>
    /// The measurements AutoSize cannot supply. The resolution list is the
    /// reason this method exists: a ComboBox stays at its designed width however
    /// long its entries are, and these entries carry a phrase and two numbers.
    /// </summary>
    void ApplyDpiDependentLayout()
    {
        int padding = LogicalToDeviceUnits(ComboPadding);
        int widest = 0;
        using (var graphics = CreateGraphics())
        {
            foreach (var item in _resolutionBox.Items)
            {
                widest = Math.Max(
                    widest,
                    (int)Math.Ceiling(graphics.MeasureString(item!.ToString(), Font).Width));
            }
        }

        // The dropdown may be as wide as it likes; the closed box may not push
        // the window off the screen it opens on. At 450% scaling the two part
        // company, and only the dropdown can afford to win.
        int wanted = widest + padding;
        _resolutionBox.DropDownWidth = wanted;

        int roomOnScreen =
            Screen.FromControl(this).WorkingArea.Width - LogicalToDeviceUnits(ScreenMargin);
        _resolutionBox.Width = Math.Clamp(
            wanted, LogicalToDeviceUnits(MinimumComboWidth), Math.Max(1, roomOnScreen));

        int width = _resolutionBox.Width;
        _qualitySlider.Width = Math.Min(width, LogicalToDeviceUnits(SliderWidth));
        _zoomSlider.Width = _qualitySlider.Width;

        // Two bands with a gap between them, at least as wide as the list above
        // and never narrower than a picture can usefully be. Shaped like the
        // samples, so each whole picture fills its band with nothing left over
        // at the edges. Whether they can stay that TALL is a question for
        // FitSamplesOnScreen, once the window knows how tall it is.
        int gap = LogicalToDeviceUnits(SampleGap);
        int band = Math.Max(LogicalToDeviceUnits(MinimumSampleWidth), (width - gap) / 2);
        band = Math.Min(band, Math.Max(1, (roomOnScreen - gap) / 2));
        SizeSamples(new Size(band, (int)Math.Round(band / SampleAspect)));

        // Each caption wraps inside its own column rather than widening it.
        _figureCaption.MaximumSize = new Size(band, 0);
        _photoCaption.MaximumSize = new Size(band, 0);

        // The description runs the width of the pair, which is now what sets
        // how wide this window is.
        _description.MaximumSize = new Size((band * 2) + gap, 0);
    }

    /// <summary>
    /// Shrink the two sample bands until the window stands inside the working
    /// area of the screen it opens on.
    ///
    /// Measured at 175% on a 1920 x 1080 screen: the window came out 1108 px
    /// against 996 px of working area, with OK and Cancel below the bottom
    /// edge - still reachable by Enter and Esc, and invisible to anyone who
    /// did not already know that. Everything above them is text at the
    /// reader's own size and must not be squeezed. The pictures are the one
    /// part that can give, and they give in both directions, because a sample
    /// out of shape is a sample nobody can judge.
    ///
    /// Called once the window has settled rather than from the layout pass:
    /// at handle-creation time Height still read 64 px short of what the
    /// window became, and half of that error went straight into the bands.
    /// </summary>
    void FitSamplesOnScreen()
    {
        int room = Screen.FromControl(this).WorkingArea.Height;

        // A few passes, because each new band size moves the height that the
        // next guess is measured against.
        for (int pass = 0; pass < 3; pass++)
        {
            PerformLayout();
            int overflow = Height - room;
            if (overflow <= 0) return;

            // One row of bands, so it carries the whole of what will not fit.
            var band = _figurePreview.Size;
            int height = Math.Max(
                LogicalToDeviceUnits(MinimumSampleHeight), band.Height - overflow);
            if (height == band.Height) return;

            SizeSamples(new Size((int)Math.Round(height * SampleAspect), height));
        }
    }

    void SizeSamples(Size size)
    {
        _figurePreview.Size = size;
        _photoPreview.Size = size;
    }

    /// <summary>
    /// Put both samples through what a save does to a picture: redraw it no
    /// larger than the resolution allows, and re-encode it the way the PDF
    /// already stores it.
    ///
    /// Which way that is comes from the sample, not from a size comparison.
    /// Resizing an image in a PDF never changes how it is stored - a figure
    /// held losslessly stays lossless and the quality slider cannot reach it,
    /// a photograph held as JPEG is written again at the chosen quality. That
    /// is the difference the two samples exist to show, and it is exactly what
    /// their captions say.
    /// </summary>
    void RefreshPreview()
    {
        RefreshSample(
            _figurePreview, _figureCaption, _figurePng, L10n.SettingsSampleFigure, lossy: false);
        RefreshSample(
            _photoPreview, _photoCaption, _photoPng, L10n.SettingsSamplePhoto, lossy: true);

        // The scale moved: a resolution change hands the bands a different
        // number of pixels to fit into the same space.
        ShowDrawnScale();
    }

    void RefreshSample(
        SamplePreview preview, Label caption, byte[]? png, string name, bool lossy)
    {
        if (png is null)
        {
            caption.Text = string.Empty;
            return;
        }

        var reduced = Resampler.RedrawNoWiderThan(png, StoredWidth);
        var stored = lossy ? Resampler.ReencodeAsJpeg(reduced, _qualitySlider.Value) : reduced;

        try
        {
            using var stream = new MemoryStream(stored);
            using var decoded = new Bitmap(stream);

            // The pixel count belongs in the caption beside the byte count. It
            // is the resolution setting's whole effect, and on three of the
            // five settings a photograph is the only place the eye can see it.
            var size = new Size(decoded.Width, decoded.Height);
            caption.Text = Caption(name, stored.LongLength, size, lossy);
            preview.Announce(caption.Text);

            // Copied out: a Bitmap built on a stream needs that stream alive for
            // as long as it is drawn.
            preview.Show(new Bitmap(decoded));
        }
        catch (Exception)
        {
            caption.Text = string.Empty;
            preview.Show(null);
        }
    }

    static string Caption(string name, long bytes, Size pixels, bool lossy) => lossy
        ? L10n.SettingsPreviewLossy(name, bytes, pixels.Width, pixels.Height)
        : L10n.SettingsPreviewLossless(name, bytes, pixels.Width, pixels.Height);

    /// <summary>
    /// How many pixels across the samples survive at the chosen resolution.
    /// A limit per inch means nothing without a size on the page, so the
    /// samples stand for a picture <see cref="SampleWidthInches"/> wide.
    /// </summary>
    int StoredWidth => (int)Math.Round(
        ImageReduction.DpiOf(SelectedLimit) * SampleWidthInches);

    ImageSizeLimit SelectedLimit => ((ResolutionEntry)_resolutionBox.SelectedItem!).Limit;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyDpiDependentLayout();
        RefreshPreview();
    }

    /// <summary>
    /// The last measurement, taken when the window has its real height and
    /// before anything of it has been painted.
    /// </summary>
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        FitSamplesOnScreen();
        // The bands may have just given ground, which changes the scale.
        ShowDrawnScale();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        ApplyDpiDependentLayout();
        FitSamplesOnScreen();
        RefreshPreview();
    }

    int IndexOf(ImageSizeLimit limit)
    {
        for (int i = 0; i < _resolutionBox.Items.Count; i++)
        {
            if (((ResolutionEntry)_resolutionBox.Items[i]!).Limit == limit) return i;
        }
        return 0;
    }

    /// <summary>
    /// A caption without its access-key marker and trailing colon, for the
    /// spoken name. A screen reader announcing "ampersand R resolution colon"
    /// is reading the markup rather than the label.
    /// </summary>
    static string Strip(string caption) =>
        caption.Replace("&", string.Empty).TrimEnd(':', '：', ' ');

    /// <summary>
    /// One line of the resolution list. ToString is what a ComboBox draws, so
    /// the label is built where the value is, and the two cannot drift apart.
    /// </summary>
    sealed record ResolutionEntry(ImageSizeLimit Limit)
    {
        public override string ToString() => L10n.ResolutionLabel(Limit);
    }

    /// <summary>
    /// Show the window and answer what the user chose, or null when they
    /// cancelled — which has to be told apart from "chose the same again",
    /// because only the first means nothing needs writing to disk.
    /// </summary>
    public static ImageReduction? ShowFor(IWin32Window owner, ImageReduction current)
    {
        using var dialog = new SettingsDialog(current);
        return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.Reduction : null;
    }

    /// <summary>
    /// The window itself, for the camera. <see cref="ShowFor"/> is how a person
    /// opens it, and it does not answer until the window is gone — which is on
    /// the far side of the shutter. The pose shows it without waiting instead.
    /// </summary>
    public static SettingsDialog ForTheCamera(ImageReduction shown) => new(shown);
}
