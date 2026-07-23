namespace PdfImageRemoverForRag.Core.Formatting;

/// <summary>
/// Compact display of a set of page numbers — the UI "使用ページ" column
/// (§11.3) shows either "1, 2, 3" or "1〜50" or a mix like "1〜3, 5, 7〜9".
/// Ranges use the Japanese wave-dash (U+FF5E) exactly as printed in the spec.
/// </summary>
public static class UsagePageFormatter
{
    /// <summary>Wave-dash character used between range endpoints ("1〜50").</summary>
    public const string RangeSeparator = "〜";

    /// <summary>Character used between non-consecutive entries ("1, 3, 5").</summary>
    public const string ListSeparator = ", ";

    /// <summary>Minimum length of a consecutive run that switches to range form.</summary>
    public const int MinRunForRange = 3;

    public static string Format(IEnumerable<int> pageNumbers)
    {
        // Deduplicate + sort so upstream can hand us anything.
        var pages = pageNumbers.Distinct().OrderBy(p => p).ToArray();
        if (pages.Length == 0) return string.Empty;

        var parts = new List<string>();
        int runStart = pages[0];
        int runEnd = pages[0];

        // Standard "walk consecutive runs" loop. When the run ends (a gap is
        // seen or we hit the last element), emit either a range or the
        // individual items depending on run length.
        for (int i = 1; i <= pages.Length; i++)
        {
            bool isBoundary = i == pages.Length || pages[i] != runEnd + 1;
            if (isBoundary)
            {
                int runLength = runEnd - runStart + 1;
                if (runLength >= MinRunForRange)
                {
                    parts.Add($"{runStart}{RangeSeparator}{runEnd}");
                }
                else
                {
                    for (int p = runStart; p <= runEnd; p++) parts.Add(p.ToString());
                }
                if (i < pages.Length)
                {
                    runStart = pages[i];
                    runEnd = pages[i];
                }
            }
            else
            {
                runEnd = pages[i];
            }
        }

        return string.Join(ListSeparator, parts);
    }
}
