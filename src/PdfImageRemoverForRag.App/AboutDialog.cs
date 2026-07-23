namespace PdfImageRemoverForRag.App;

/// <summary>
/// The About dialog: app icon and name at the top, one paragraph of what the
/// app does, then the copyright and licence notices.
///
/// A custom form rather than a MessageBox because a MessageBox cannot show the
/// app icon (its icon slot only takes the system alert glyphs) and cannot host
/// the licence link.
///
/// Layout uses nested auto-sizing panels, NOT absolute coordinates. An earlier
/// version positioned every control by hand and clipped every string at 300%
/// DPI: the fonts scaled but the hand-written bounds did not. With AutoSize
/// throughout, the only measurements left are the icon box and the wrap width,
/// and both go through <see cref="Control.LogicalToDeviceUnits(int)"/> when the
/// handle is created — the same rule the main window follows.
/// </summary>
internal sealed class AboutDialog : Form
{
    // Logical (96-DPI) metrics; scaled to device pixels in ApplyDpiDependentLayout.
    const int IconEdge = 48;
    const int WrapWidth = 400;

    readonly Image? _iconImage;
    readonly PictureBox _iconBox;
    readonly Label _descriptionLabel;
    readonly Label _separator;

    AboutDialog()
    {
        Text = L10n.AboutTitle;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        BackColor = SystemColors.Window;
        AutoScaleMode = AutoScaleMode.Font;
        // The form takes its size from the content, so no string can be cut off
        // by a hard-coded client size.
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        _iconImage = LoadAppIconImage();

        // --- header: icon beside the product name and build ------------------
        _iconBox = new PictureBox
        {
            Image = _iconImage,
            SizeMode = PictureBoxSizeMode.Zoom,
            Size = new Size(IconEdge, IconEdge),
            Margin = new Padding(0, 0, 12, 0),
        };

        var nameLabel = new Label
        {
            Text = L10n.AppTitle,
            Font = new Font(Font.FontFamily, Font.Size + 3.5f, FontStyle.Bold),
            AutoSize = true,
            Margin = new Padding(0),
        };
        // The build number is here so a support question can be pinned to an
        // exact build without hunting through logs.
        var versionLabel = new Label
        {
            Text = AppVersion.Display,
            ForeColor = SystemColors.GrayText,
            AutoSize = true,
            Margin = new Padding(0, 4, 0, 0),
        };

        var nameStack = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Margin = new Padding(0),
            Anchor = AnchorStyles.Left,
        };
        nameStack.Controls.AddRange(new Control[] { nameLabel, versionLabel });

        var header = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 16),
        };
        header.Controls.AddRange(new Control[] { _iconBox, nameStack });

        // --- body ------------------------------------------------------------
        // One paragraph. Anything longer belongs in the online manual, which is
        // one menu item away.
        _descriptionLabel = new Label
        {
            Text = L10n.AboutDescription,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 16),
        };

        // A hairline rule separating "what it is" from the legal notices.
        _separator = new Label
        {
            BorderStyle = BorderStyle.Fixed3D,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 0, 0, 14),
        };

        var copyrightLabel = new Label
        {
            Text = L10n.AboutCopyright,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 6),
        };
        var appLicenseLabel = new Label
        {
            Text = L10n.AboutAppLicense,
            ForeColor = SystemColors.GrayText,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 2),
        };
        var thirdPartyLabel = new Label
        {
            Text = L10n.AboutThirdPartyLicense,
            ForeColor = SystemColors.GrayText,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 14),
        };

        // --- footer: licence link on the left, OK on the right ---------------
        // Full notices live in the repository; the dialog links rather than
        // reproducing two library licences inside a small window.
        var licenseLink = new LinkLabel
        {
            Text = L10n.AboutLicenseLink,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0),
        };
        licenseLink.LinkClicked += (_, _) => WebLink.Open(this, L10n.AboutLicenseUrl);

        var okButton = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(88, 26),
            Anchor = AnchorStyles.Right,
            Margin = new Padding(0),
        };
        AcceptButton = okButton;
        CancelButton = okButton;

        var footer = new TableLayoutPanel
        {
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
        };
        // The first column absorbs the slack, pushing OK to the right edge.
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.Controls.Add(licenseLink, 0, 0);
        footer.Controls.Add(okButton, 1, 0);

        // --- root ------------------------------------------------------------
        var root = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            Padding = new Padding(20),
        };
        foreach (var child in new Control[]
                 {
                     header, _descriptionLabel, _separator,
                     copyrightLabel, appLicenseLabel, thirdPartyLabel, footer,
                 })
        {
            root.Controls.Add(child);
        }
        Controls.Add(root);
    }

    /// <summary>
    /// Apply the two measurements that cannot come from AutoSize: the icon box
    /// and the width the description wraps at.
    /// </summary>
    void ApplyDpiDependentLayout()
    {
        int iconEdge = LogicalToDeviceUnits(IconEdge);
        _iconBox.Size = new Size(iconEdge, iconEdge);

        // MaximumSize with width only lets the label grow downward as it wraps.
        _descriptionLabel.MaximumSize = new Size(LogicalToDeviceUnits(WrapWidth), 0);

        // A 2px rule stays a hairline at 300% unless it is scaled too.
        _separator.Height = LogicalToDeviceUnits(2);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyDpiDependentLayout();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        ApplyDpiDependentLayout();
    }

    /// <summary>
    /// Read the largest frame of the embedded multi-size icon, so the picture
    /// box has enough pixels to scale down cleanly at any DPI.
    /// </summary>
    static Image? LoadAppIconImage()
    {
        using var iconStream = typeof(AboutDialog).Assembly.GetManifestResourceStream("appicon.ico");
        if (iconStream is null) return null;
        using var icon = new Icon(iconStream, 256, 256);
        return icon.ToBitmap();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _iconImage?.Dispose();
        base.Dispose(disposing);
    }

    public static void ShowFor(IWin32Window owner)
    {
        using var dialog = new AboutDialog();
        dialog.ShowDialog(owner);
    }
}
