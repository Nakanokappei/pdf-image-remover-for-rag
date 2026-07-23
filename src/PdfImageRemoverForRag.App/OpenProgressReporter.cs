using PdfImageRemoverForRag.Core.Models;

namespace PdfImageRemoverForRag.App;

/// <summary>
/// Turns analyzer progress into the line shown in the status bar and the
/// progress dialog.
///
/// The analyzer reports only a phase and a count — it sees one document and
/// knows nothing about file names or how many files the user picked. That
/// context lives here instead of as loose mutable fields on the form.
/// </summary>
internal sealed class OpenProgressReporter
{
    string _fileName = string.Empty;
    int _fileIndex;
    int _fileCount;

    /// <summary>Called as each file's analysis starts.</summary>
    public void BeginFile(string fileName, int index, int count)
    {
        _fileName = fileName;
        _fileIndex = index;
        _fileCount = count;
    }

    /// <summary>The user-facing line for one analyzer report.</summary>
    public string Describe(AnalysisProgress report)
    {
        // Page numbers read from 1; the analyzer counts finished units.
        int position = Math.Min(report.Completed + 1, Math.Max(report.Total, 1));
        var body = report.Phase switch
        {
            AnalysisPhase.ReadingPages =>
                L10n.ProgressReadingPages(_fileName, position, report.Total),
            AnalysisPhase.ExtractingThumbnails =>
                L10n.ProgressThumbnails(_fileName, position, report.Total),
            _ => L10n.ProgressGrouping(_fileName),
        };
        // The file counter only earns its space when there is more than one.
        return _fileCount > 1
            ? L10n.ProgressFileCounter(_fileIndex, _fileCount) + body
            : body;
    }
}
