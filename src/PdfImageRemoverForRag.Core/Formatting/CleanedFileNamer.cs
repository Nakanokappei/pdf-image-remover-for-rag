namespace PdfImageRemoverForRag.Core.Formatting;

/// <summary>
/// Builds the default save filename (§15) and enforces the "must not
/// overwrite the source" rule (§15, §25). Everything path-shaped runs
/// through this helper so the App and Infrastructure both use identical
/// conventions.
/// </summary>
public static class CleanedFileNamer
{
    /// <summary>Suffix appended to the base name (spec §15 "manual_cleaned.pdf").</summary>
    public const string CleanedSuffix = "_cleaned";

    /// <summary>
    /// Convert a source path into the default cleaned-file path in the same
    /// directory: <c>manual.pdf</c> → <c>manual_cleaned.pdf</c>.
    /// </summary>
    public static string BuildDefaultDestination(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            throw new ArgumentException("source path must not be empty", nameof(sourcePath));
        }
        var dir = Path.GetDirectoryName(sourcePath) ?? string.Empty;
        var stem = Path.GetFileNameWithoutExtension(sourcePath);
        var ext = Path.GetExtension(sourcePath);
        // Keep the extension casing of the source so "MANUAL.PDF" stays
        // "MANUAL_cleaned.PDF" — small nicety for users on case-sensitive
        // filesystems who round-trip files.
        return Path.Combine(dir, stem + CleanedSuffix + ext);
    }

    /// <summary>
    /// True when <paramref name="destination"/> refers to the same file as
    /// <paramref name="source"/>. Compared via <c>Path.GetFullPath</c> so a
    /// caller with a relative path against the same current directory is
    /// caught correctly. On Windows the comparison is case-insensitive; on
    /// other platforms it is case-sensitive.
    /// </summary>
    public static bool WouldOverwriteSource(string source, string destination)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(destination)) return false;
        var lhs = Path.GetFullPath(source);
        var rhs = Path.GetFullPath(destination);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(lhs, rhs, comparison);
    }
}
