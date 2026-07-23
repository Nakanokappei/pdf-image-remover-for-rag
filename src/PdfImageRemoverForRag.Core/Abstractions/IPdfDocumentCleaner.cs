using PdfImageRemoverForRag.Core.Models;

namespace PdfImageRemoverForRag.Core.Abstractions;

/// <summary>
/// Executes the removal plan: drops the drawing commands for the selected
/// image groups and writes the result to a new PDF via a temp-file swap
/// (spec §15). Never touches the source file.
/// </summary>
public interface IPdfDocumentCleaner
{
    Task<CleaningResult> CleanAsync(
        string sourcePath,
        string destinationPath,
        IReadOnlyList<ImageRemovalSelection> selections,
        CancellationToken ct = default);
}
