namespace PdfImageRemoverForRag.Core.Models;

/// <summary>
/// Which part of the analysis is running. A 30 MB PDF can take minutes, and
/// the two long phases fail differently to the user's eye — page sweeping
/// advances steadily, thumbnail extraction can stall on one huge image — so
/// they are reported separately rather than as one opaque percentage.
/// </summary>
public enum AnalysisPhase
{
    /// <summary>Walking pages, collecting images, text and shapes.</summary>
    ReadingPages,

    /// <summary>Decoding thumbnails for the images that were found.</summary>
    ExtractingThumbnails,

    /// <summary>Grouping discoveries by hash. Fast; reported for completeness.</summary>
    Grouping,
}

/// <summary>
/// One progress report from an analysis in flight.
///
/// Deliberately says nothing about which file this is, or how many files the
/// caller queued: the analyzer sees one document. The App layer wraps these
/// reports with the file name and position when it builds the status text.
/// </summary>
/// <param name="Phase">The phase producing this report.</param>
/// <param name="Completed">Units finished so far (pages, or images).</param>
/// <param name="Total">
/// Units expected in total, or 0 when it is not known ahead of time — the UI
/// then shows an indeterminate bar rather than inventing a percentage.
/// </param>
public readonly record struct AnalysisProgress(AnalysisPhase Phase, int Completed, int Total)
{
    /// <summary>Fraction complete in 0..1, or null when the total is unknown.</summary>
    public double? Fraction => Total > 0 ? Math.Clamp((double)Completed / Total, 0, 1) : null;
}
