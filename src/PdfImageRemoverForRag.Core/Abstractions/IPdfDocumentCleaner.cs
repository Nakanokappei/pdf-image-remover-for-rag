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
    /// <param name="regionsToClear">
    /// Places whose objects are taken out with nothing drawn in their stead —
    /// what hiding a layer means. Told apart from a removal selection by being
    /// ONE place on ONE page: the same image drawn elsewhere is untouched.
    /// </param>
    /// <param name="imageReduction">
    /// How large the images in the output may be. Null and
    /// <see cref="ImageReduction.Off"/> both mean they are written out as they
    /// came in.
    /// </param>
    /// <param name="isFinalOutput">
    /// Whether this is the file the user keeps, rather than an intermediate the
    /// app writes for itself. Two things turn on it.
    ///
    /// The images are reduced only for the final file: re-encoding the same
    /// pictures on every pass costs detail each time. (The reduction is still
    /// READ for an intermediate, for one thing — what resolution to render a
    /// flattened region at. That picture is made once, on the pass that
    /// flattens, and every later pass can only make it smaller.)
    ///
    /// And a run with nothing to remove, nothing to flatten and nothing to
    /// clear is refused for an intermediate, because something was supposed to
    /// happen to it, but allowed for the final file: a save that only flattened
    /// arrives with nothing left to do, since the flattening is already in the
    /// working copy it reads from, and writing that out IS the job.
    ///
    /// It defaults to false, the careful reading. A caller that means to write
    /// the user's own file has to say so.
    /// </param>
    Task<CleaningResult> CleanAsync(
        string sourcePath,
        string destinationPath,
        IReadOnlyList<ObjectRemovalSelection> selections,
        IReadOnlyList<OverlapRegion>? regionsToFlatten = null,
        IReadOnlyList<OverlapRegion>? regionsToClear = null,
        ImageReduction? imageReduction = null,
        bool isFinalOutput = false,
        CancellationToken ct = default);
}
