using System.Globalization;

namespace PdfImageRemoverForRag.Core.Formatting;

/// <summary>
/// Human-readable byte counts for the image list's "推定容量" column
/// (spec §11.3: KB or MB). Kept in Core next to the other UI formatters so
/// the App layer never invents its own numeric formatting.
/// </summary>
public static class ByteSizeFormatter
{
    const double BytesPerKilobyte = 1024;
    const double BytesPerMegabyte = 1024 * 1024;
    const double BytesPerGigabyte = 1024L * 1024 * 1024;

    /// <summary>
    /// Format a byte count as "84 B" / "1.5 KB" / "12.3 MB". One decimal at
    /// most, trailing ".0" trimmed, invariant culture so the string is
    /// stable across OS language settings.
    /// </summary>
    public static string Format(long bytes)
    {
        return bytes switch
        {
            < 0 => "0 B", // defensive — sizes are never negative upstream
            < (long)BytesPerKilobyte => $"{bytes} B",
            < (long)BytesPerMegabyte =>
                (bytes / BytesPerKilobyte).ToString("0.#", CultureInfo.InvariantCulture) + " KB",
            < (long)BytesPerGigabyte =>
                (bytes / BytesPerMegabyte).ToString("0.#", CultureInfo.InvariantCulture) + " MB",
            _ => (bytes / BytesPerGigabyte).ToString("0.#", CultureInfo.InvariantCulture) + " GB",
        };
    }
}
