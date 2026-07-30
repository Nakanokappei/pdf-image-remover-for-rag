using PdfImageRemoverForRag.Core.Models;

namespace PdfImageRemoverForRag.Core.Abstractions;

/// <summary>
/// Executes the removal plan: drops the drawing commands for the selected
/// image groups and writes the result to a new PDF via a temp-file swap
/// (spec §15). Never touches the source file.
/// </summary>
public interface IPdfDocumentCleaner
{
    /// <param name="selections">
    /// Groups to remove from the whole file. May be empty when the run only
    /// flattens.
    /// </param>
    /// <param name="regionsToFlatten">
    /// Places to replace with a raster image of themselves, each holding only
    /// the members the user chose. Flattening and removal are opposite
    /// operations — one keeps the appearance and drops the text layer, the other
    /// drops the appearance — so they are separate arguments, and a single run
    /// flattens before it removes.
    /// </param>
    Task<CleaningResult> CleanAsync(
        string sourcePath,
        string destinationPath,
        IReadOnlyList<ImageRemovalSelection> selections,
        IReadOnlyList<OverlapRegion>? regionsToFlatten = null,
        CancellationToken ct = default);
}
