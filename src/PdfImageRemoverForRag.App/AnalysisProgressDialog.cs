namespace PdfImageRemoverForRag.App;

/// <summary>
/// Modal progress dialog shown while PDFs are being analyzed, with a Cancel
/// button. Only appears when the work has already run for a couple of seconds
/// — a dialog that flashes up and vanishes on every small file is worse than
/// no dialog at all.
///
/// The dialog does not start the work; it attaches to a task already running
/// and closes itself when that task finishes (see <see cref="OnShown"/>). That
/// ordering removes the race a "start work, then show dialog" design has, where
/// the work can finish before the window exists and leave the dialog stuck open.
///
/// Layout follows the same rule as <see cref="AboutDialog"/>: auto-sizing
/// panels, with the few real measurements scaled for DPI.
/// </summary>
internal sealed class AnalysisProgressDialog : Form
{
    const int ContentWidth = 380;
    const int BarHeight = 16;

    readonly Task _work;
    readonly CancellationTokenSource _cancellation;
    readonly Label _statusLabel;
    readonly ProgressBar _progressBar;
    readonly Button _cancelButton;

    AnalysisProgressDialog(Task work, CancellationTokenSource cancellation, string initialStatus)
    {
        _work = work;
        _cancellation = cancellation;

        Text = L10n.AppTitle;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        // No close box: cancelling must go through the button so the token is
        // always signalled. ControlBox off also removes Alt+F4's silent exit.
        ControlBox = false;
        BackColor = SystemColors.Window;
        AutoScaleMode = AutoScaleMode.Font;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        _statusLabel = new Label
        {
            Text = initialStatus,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 12),
        };

        // Style is set per report: determinate while a page count is known,
        // marching otherwise.
        _progressBar = new ProgressBar
        {
            Style = ProgressBarStyle.Marquee,
            Maximum = 1000,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 0, 0, 14),
        };

        _cancelButton = new Button
        {
            Text = L10n.Cancel,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(96, 26),
            Anchor = AnchorStyles.Right,
            Margin = new Padding(0),
        };
        _cancelButton.Click += OnCancelClicked;

        var footer = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.Controls.Add(_cancelButton, 0, 0);

        var root = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            Padding = new Padding(20),
        };
        root.Controls.Add(_statusLabel);
        root.Controls.Add(_progressBar);
        root.Controls.Add(footer);
        Controls.Add(root);
    }

    /// <summary>Reflect one report from the analysis. Call on the UI thread.</summary>
    public void Update(string status, double? fraction)
    {
        _statusLabel.Text = status;
        if (fraction is { } value)
        {
            // Switching out of Marquee needs the style change before the value,
            // or the bar keeps animating and ignores Value.
            if (_progressBar.Style != ProgressBarStyle.Continuous)
            {
                _progressBar.Style = ProgressBarStyle.Continuous;
            }
            _progressBar.Value = (int)Math.Round(value * _progressBar.Maximum);
        }
        else if (_progressBar.Style != ProgressBarStyle.Marquee)
        {
            _progressBar.Style = ProgressBarStyle.Marquee;
        }
    }

    void OnCancelClicked(object? sender, EventArgs e)
    {
        // Cancellation is not instant — the analyzer checks the token between
        // pages — so the button reports that the request was heard rather than
        // appearing to do nothing.
        _cancelButton.Enabled = false;
        _statusLabel.Text = L10n.StatusCancelling;
        _progressBar.Style = ProgressBarStyle.Marquee;
        _cancellation.Cancel();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _progressBar.Height = LogicalToDeviceUnits(BarHeight);
        _progressBar.Width = LogicalToDeviceUnits(ContentWidth);
        _statusLabel.MaximumSize = new Size(LogicalToDeviceUnits(ContentWidth), 0);
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        // Await the work rather than subscribing to a continuation: if the task
        // is already finished this simply falls through and closes at once.
        try
        {
            await _work;
        }
        catch
        {
            // The caller awaits the same task and handles its failure; the
            // dialog's only job is to stop showing itself.
        }
        Close();
    }

    /// <summary>
    /// Show the dialog for work already in flight and return when that work
    /// ends. <paramref name="onCreated"/> receives the instance so the caller
    /// can push progress reports into it.
    /// </summary>
    public static void ShowFor(IWin32Window owner, Task work,
                               CancellationTokenSource cancellation,
                               string initialStatus,
                               Action<AnalysisProgressDialog> onCreated)
    {
        using var dialog = new AnalysisProgressDialog(work, cancellation, initialStatus);
        onCreated(dialog);
        dialog.ShowDialog(owner);
    }
}
