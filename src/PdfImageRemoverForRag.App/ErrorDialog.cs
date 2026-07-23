using PdfImageRemoverForRag.Core.Errors;

namespace PdfImageRemoverForRag.App;

/// <summary>
/// Error dialog per spec §17: user-facing description, suggested remedy,
/// and a "詳細をコピー" button that puts the technical details (exception
/// type, message, stack trace) on the clipboard. The stack trace itself is
/// never rendered on screen.
/// </summary>
internal sealed class ErrorDialog : Form
{
    readonly string _technicalDetails;

    ErrorDialog(string description, string remedy, string technicalDetails)
    {
        _technicalDetails = technicalDetails;

        Text = L10n.ErrorDialogTitle;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(440, 190);

        // Description (what happened) on top, remedy (what to do) below it.
        var descriptionLabel = new Label
        {
            Text = description,
            Location = new Point(16, 16),
            Size = new Size(408, 48),
            AutoEllipsis = true,
        };
        var remedyLabel = new Label
        {
            Text = remedy,
            Location = new Point(16, 70),
            Size = new Size(408, 64),
            ForeColor = SystemColors.GrayText,
        };

        var copyDetailsButton = new Button
        {
            Text = L10n.CopyDetails,
            Location = new Point(16, 146),
            Size = new Size(110, 28),
        };
        copyDetailsButton.Click += OnCopyDetailsClicked;

        var okButton = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            Location = new Point(334, 146),
            Size = new Size(90, 28),
        };
        AcceptButton = okButton;

        Controls.AddRange(new Control[] { descriptionLabel, remedyLabel, copyDetailsButton, okButton });
    }

    void OnCopyDetailsClicked(object? sender, EventArgs e)
    {
        try
        {
            Clipboard.SetText(_technicalDetails);
        }
        catch
        {
            // Clipboard access can fail if another process holds it; the
            // button silently doing nothing beats a second error dialog.
        }
    }

    /// <summary>
    /// Show the dialog for a domain exception, resolving the localized text
    /// from <see cref="ErrorMessageCatalog"/>.
    /// </summary>
    public static void Show(IWin32Window owner, PdfCleanerException exception)
    {
        var text = ErrorMessageCatalog.Resolve(exception.Kind);
        var details = $"Kind: {exception.Kind}{Environment.NewLine}{exception}";
        using var dialog = new ErrorDialog(text.Description, text.Remedy, details);
        dialog.ShowDialog(owner);
    }
}
