namespace PdfImageRemoverForRag.App;

/// <summary>
/// The proportion both splitters divide by when nobody has dragged them: the
/// object list to the objects panel, and inside that panel the list to the page
/// preview. Written once because the two are the same decision — what IS the
/// right share for a pane beside the thing it describes, at whatever size the
/// window happens to be — and a second copy of the number would let them drift.
/// </summary>
internal static class GoldenSection
{
    const double Ratio = 1.6180339887;

    /// <summary>
    /// The smaller pane's share when the two stand as φ to 1: 1/φ², a shade over
    /// a third.
    /// </summary>
    public const double MinorShare = 1 / (Ratio * Ratio);
}
