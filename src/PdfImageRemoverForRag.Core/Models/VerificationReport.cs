namespace PdfImageRemoverForRag.Core.Models;

/// <summary>
/// Result of the post-save simple verification (§16). Every boolean maps to
/// one bullet in the spec; <see cref="IsOverallOk"/> is the aggregate.
/// </summary>
/// <param name="RemovedImagesGoneFromResources">
/// No removed image is still listed in any page's <c>/XObject</c> resources.
/// Checking only for the absence of a <c>Do</c> operator was not enough: it
/// asks whether the image is still PAINTED, and a reader that enumerates
/// objects instead of rendering pages — which is what a RAG pipeline does —
/// finds an image the user removed all the same. The tool exists to keep
/// images out of such readers, so this is part of the job being done, not a
/// refinement of it.
/// </param>
public sealed record VerificationReport(
    bool CleanedPdfOpens,
    bool PageCountMatches,
    bool NonEmptyFileSize,
    bool NoDoOperatorsForRemovedImages,
    bool RemovedImagesGoneFromResources,
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
        RemovedImagesGoneFromResources &&
        NonRemovedImageGroupsRetained &&
        NoRuntimeExceptions;
}
