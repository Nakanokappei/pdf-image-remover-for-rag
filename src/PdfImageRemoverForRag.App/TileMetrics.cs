namespace PdfImageRemoverForRag.App;

/// <summary>
/// The タイル形式 view's measurements, in LOGICAL (96-DPI) pixels.
///
/// Every one of these must be put through <c>Dip()</c> before it reaches a
/// coordinate. The tile view is measured entirely by hand — it paints its own
/// tiles rather than hosting controls — so WinForms never scales any of it.
/// Left unscaled the tiles came out a third of their size at 300 %, which
/// crushed the text inside them and shrank the margin into nothing.
/// </summary>
internal static class TileMetrics
{
    // 4x the original 128x104 area (2x per side) so thumbnails are actually
    // legible — the whole point of the tile view.
    public const int TileWidth = 256;
    public const int TileHeight = 208;

    /// <summary>Gap between neighbouring tiles.</summary>
    public const int TileMargin = 6;

    /// <summary>Inset from the view's edge to the first tile.</summary>
    public const int PanelPadding = 8;

    /// <summary>Content area for the thumbnail after tile padding.</summary>
    public const int ContentWidth = TileWidth - 20;
    public const int ContentHeight = TileHeight - 20;
}
