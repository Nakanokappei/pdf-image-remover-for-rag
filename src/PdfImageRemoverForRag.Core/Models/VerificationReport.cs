namespace PdfImageRemoverForRag.Core.Models;

/// <summary>
/// Result of the post-save simple verification (§16). Every boolean maps to
/// one bullet in the spec; <see cref="IsOverallOk"/> is the aggregate.
/// </summary>
public sealed record VerificationReport(
    bool CleanedPdfOpens,
    bool PageCountMatches,
    bool NonEmptyFileSize,
    bool NoDoOperatorsForRemovedImages,
    bool NonRemovedImageGroupsRetained,
    bool NoRuntimeExceptions,
    IReadOnlyList<string> Warnings)
{
    /// <summary>Everything must be true — one flip and the tool aborts the swap.</summary>
    public bool IsOverallOk =>
        CleanedPdfOpens &&
        PageCountMatches &&
        NonEmptyFileSize &&
        NoDoOperatorsForRemovedImages &&
        NonRemovedImageGroupsRetained &&
        NoRuntimeExceptions;
}
