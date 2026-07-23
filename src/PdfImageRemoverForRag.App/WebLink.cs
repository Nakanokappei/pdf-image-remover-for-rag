using System.ComponentModel;
using System.Diagnostics;

namespace PdfImageRemoverForRag.App;

/// <summary>
/// Opens a URL in the user's default browser. Shared by the Help menu's
/// online-manual item and the About dialog's license link, so the fallback
/// behaviour is identical in both places.
/// </summary>
internal static class WebLink
{
    /// <summary>
    /// Hand the URL to the shell. If no browser is registered the shell throws,
    /// in which case the URL is shown so the user can copy it by hand rather
    /// than the click appearing to do nothing.
    /// </summary>
    public static void Open(IWin32Window owner, string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            MessageBox.Show(owner, L10n.LinkOpenFailed + url, L10n.AppTitle,
                MessageBoxButtons.OK, MessageBoxIcon.None);
        }
    }
}
