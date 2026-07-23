using PdfImageRemoverForRag.Core.Models;

namespace PdfImageRemoverForRag.Core.Abstractions;

/// <summary>
/// Runs the post-save simple verification (§16): reopen the cleaned PDF,
/// check page count, confirm the removed groups are gone, confirm every
/// non-removed group is still there, ensure no runtime exceptions leaked.
/// </summary>
public interface IPdfDocumentVerifier
{
    Task<VerificationReport> VerifyAsync(
        string originalPath,
        string cleanedPath,
        IReadOnlyList<string> removedGroupHashes,
        IReadOnlyList<string> retainedGroupHashes,
        CancellationToken ct = default);
}
