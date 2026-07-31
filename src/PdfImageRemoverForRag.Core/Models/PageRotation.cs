namespace PdfImageRemoverForRag.Core.Models;

/// <summary>
/// The page's <c>/Rotate</c> entry: how far clockwise a viewer turns the paper
/// before showing it.
///
/// Everything else in this program works in CONTENT space — the coordinates the
/// content stream itself uses, origin at the bottom-left, y upwards — because
/// that is the space objects are found in and the space they are rewritten in.
/// <c>/Rotate</c> changes neither; it only changes what a viewer puts on screen.
/// So the rotation matters at exactly the boundaries where this program hands a
/// rectangle to something that draws the page as a VIEWER would: the operating
/// system's rasterizer, and the previews that outline a place on a rendered
/// page. Both were wrong on rotated pages until this existed — the rasterizer
/// asked for a rectangle off the edge of the paper and got back a blank, which
/// on the flatten path means the region is skipped and the page left alone.
///
/// The mapping was measured rather than derived, against the OS renderer, for
/// all four values and with deliberately asymmetric test regions (a mirrored
/// mapping passes a symmetric one).
/// </summary>
public static class PageRotation
{
    /// <summary>
    /// The <c>/Rotate</c> value reduced to one of 0, 90, 180, 270. The entry is
    /// allowed to be negative or past a full turn, and anything that is not a
    /// multiple of 90 is invalid — treated here as no rotation, since a viewer
    /// showing the page unturned is what such a file gets anyway.
    /// </summary>
    public static int Normalize(int degrees)
    {
        int turned = ((degrees % 360) + 360) % 360;
        return turned is 0 or 90 or 180 or 270 ? turned : 0;
    }

    /// <summary>
    /// The page's size as a viewer shows it: width and height swap on a quarter
    /// turn. Same unit in as out.
    /// </summary>
    public static (double Width, double Height) DisplaySize(
        double widthPoints, double heightPoints, int rotationDegrees) =>
        Normalize(rotationDegrees) is 90 or 270
            ? (heightPoints, widthPoints)
            : (widthPoints, heightPoints);

    /// <summary>
    /// Map a rectangle from content space onto the page as a viewer shows it:
    /// origin at the DISPLAYED page's top-left, y downwards, sized by
    /// <see cref="DisplaySize"/>. Same unit in as out.
    /// </summary>
    /// <remarks>
    /// At 0° this is the plain y-flip between PDF's bottom-left origin and a
    /// screen's top-left one, which is why it replaces that flip at its two call
    /// sites rather than sitting in front of it.
    /// </remarks>
    public static PageRegion ToDisplay(
        PageRegion region, double pageWidthPoints, double pageHeightPoints, int rotationDegrees)
    {
        double w = pageWidthPoints, h = pageHeightPoints;
        return Normalize(rotationDegrees) switch
        {
            // A quarter turn clockwise puts the content's bottom-left corner at
            // the display's top-left, so content y runs along display x.
            90 => new PageRegion(region.Y, region.X, region.Height, region.Width),
            180 => new PageRegion(w - (region.X + region.Width), region.Y, region.Width, region.Height),
            270 => new PageRegion(
                h - (region.Y + region.Height),
                w - (region.X + region.Width),
                region.Height,
                region.Width),
            _ => new PageRegion(
                region.X, h - (region.Y + region.Height), region.Width, region.Height),
        };
    }
}
