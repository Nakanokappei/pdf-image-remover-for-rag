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
    /// <param name="fitImagesToScreen">
    /// Redraw every image the output holds at the size it will be looked at,
    /// which is what keeps a screenshot-heavy manual under an upload limit. Off
    /// by default, and asked for only when the file being written is the one the
    /// user keeps: doing it to an intermediate would re-encode the same pictures
    /// again on the next pass.
    /// </param>
    Task<CleaningResult> CleanAsync(
        string sourcePath,
        string destinationPath,
        IReadOnlyList<ImageRemovalSelection> selections,
        IReadOnlyList<OverlapRegion>? regionsToFlatten = null,
        bool fitImagesToScreen = false,
        CancellationToken ct = default);
}
