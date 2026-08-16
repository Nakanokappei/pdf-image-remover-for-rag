<#
.SYNOPSIS
    Build the two pictures the settings window shows beside the quality slider.

.DESCRIPTION
    The settings window previews what a resolution and a quality cost, using
    one figure and one photograph shown side by side. Both are embedded in the
    application, so they are built here rather than dropped in by hand, and
    this script records what was done to them.

    They come out at the same width. The photograph also takes the target
    height; the figure keeps its own, because the application composes it with
    a block of text and needs somewhere to put that.

    The figure has its blank border trimmed before it is scaled. Image
    generators return a drawing centered on a white field, and that field is
    not part of the figure - dropping it first is what keeps the drawing itself
    from being cropped to fit. Then it is fitted rather than cropped, because a
    figure is as wide as it is and covering the frame would take off the legend.

    Its color is flattened last. A chart drawn by a spreadsheet or a plotting
    library is flat: one value per fill, and long runs of identical pixels for
    an encoder to collapse. An image generator does not draw one - it paints
    something that looks like one, dithered by a level or two everywhere, which
    is why this source arrived at 985 KB. Snapping the channels back to a coarse
    grid is what makes the sample stand for the thing it is supposed to stand
    for, and it happens after the scaling because interpolation puts a gradient
    back across every edge it touches.

    The photograph is scaled to cover the frame and the overhang is cropped.

    Neither is re-encoded on the way in. Both are stored as they will be read,
    so the only lossy step anywhere is the one the preview performs in front of
    the reader; a sample that already carried JPEG artifacts could not be used
    to show what JPEG artifacts look like.

.EXAMPLE
    ./scripts/make-quality-samples.ps1 `
        -Figure "$HOME/Downloads/figure.png" `
        -Photo  "$HOME/Downloads/photo.png"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string] $Figure,
    [Parameter(Mandatory = $true)][string] $Photo,
    [string] $Destination = (Join-Path $PSScriptRoot '..\src\PdfImageRemoverForRag.App\Resources'),

    # 16:9, and large enough that the highest resolution setting still has more
    # pixels to give away than the preview band can show.
    [int] $Width = 1366,
    [int] $Height = 768,

    # Where the crop sits vertically, as a share of the picture. 0.5 is the
    # middle; a photograph whose subject sits low wants more.
    [double] $FigureCentre = 0.5,
    [double] $PhotoCentre = 0.5,

    # A row counts as blank when every channel of every pixel is at least this.
    [int] $BlankFloor = 244,

    # Levels kept per color channel when flattening the figure. 17 leaves a step
    # of 16, which swallows a dither of one or two levels and is still far finer
    # than anything the eye separates in flat art.
    [int] $Levels = 17
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

Add-Type @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class Sample
{
    // Snap every channel to a coarse grid, in place. Reading and writing whole
    // rows rather than pixels keeps a megapixel picture to well under a second;
    // GetPixel/SetPixel would take minutes.
    public static void Quantize(Bitmap picture, int levels)
    {
        int step = 255 / (levels - 1);
        var area = new Rectangle(Point.Empty, picture.Size);
        var data = picture.LockBits(area, ImageLockMode.ReadWrite, PixelFormat.Format24bppRgb);
        try
        {
            var row = new byte[data.Stride];
            for (int y = 0; y < picture.Height; y++)
            {
                IntPtr line = data.Scan0 + (y * data.Stride);
                Marshal.Copy(line, row, 0, data.Stride);
                for (int at = 0; at < picture.Width * 3; at++)
                {
                    int snapped = ((row[at] + (step / 2)) / step) * step;
                    row[at] = (byte)(snapped > 255 ? 255 : snapped);
                }
                Marshal.Copy(row, 0, line, data.Stride);
            }
        }
        finally
        {
            picture.UnlockBits(data);
        }
    }

    // The part of the picture left once blank rows and columns at the edges are
    // dropped. Returns the whole picture when nothing is blank, and never an
    // empty rectangle, so the caller can use the answer without checking.
    public static Rectangle Drawn(Bitmap picture, int floor)
    {
        var area = new Rectangle(Point.Empty, picture.Size);
        var data = picture.LockBits(area, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        try
        {
            int top = picture.Height, bottom = -1, left = picture.Width, right = -1;
            var row = new byte[data.Stride];
            for (int y = 0; y < picture.Height; y++)
            {
                Marshal.Copy(data.Scan0 + (y * data.Stride), row, 0, data.Stride);
                for (int x = 0; x < picture.Width; x++)
                {
                    int at = x * 3;
                    if (row[at] >= floor && row[at + 1] >= floor && row[at + 2] >= floor) continue;
                    if (y < top) top = y;
                    if (y > bottom) bottom = y;
                    if (x < left) left = x;
                    if (x > right) right = x;
                }
            }
            if (bottom < 0) return area;
            return Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
        }
        finally
        {
            picture.UnlockBits(data);
        }
    }
}
'@ -ReferencedAssemblies System.Drawing

# Resolve the destination before doing any work, so a wrong path fails early.
$Destination = (Resolve-Path $Destination).Path
Write-Host "Destination: $Destination  target ${Width}x${Height}"

# Scale the source so it covers the target, then take the target out of the
# middle - or wherever `centre` asks for. Cover rather than fit, so no sample
# carries bars of its own into a band that is already the right shape.
function New-Cover {
    [OutputType([System.Drawing.Bitmap])]
    param(
        [System.Drawing.Bitmap] $Source,
        [System.Drawing.Rectangle] $Region,
        [double] $Centre
    )

    $scale = [Math]::Max($Width / $Region.Width, $Height / $Region.Height)
    $scaledWidth = [Math]::Max($Width, [int][Math]::Round($Region.Width * $scale))
    $scaledHeight = [Math]::Max($Height, [int][Math]::Round($Region.Height * $scale))

    # Where the target sits inside the scaled picture, kept inside its edges.
    $offsetX = [int][Math]::Round(($scaledWidth - $Width) * 0.5)
    $offsetY = [int][Math]::Round(($scaledHeight - $Height) * $Centre)
    $offsetY = [Math]::Max(0, [Math]::Min($scaledHeight - $Height, $offsetY))

    $canvasImage = New-Object System.Drawing.Bitmap $Width, $Height, ([System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $g = [System.Drawing.Graphics]::FromImage($canvasImage)
    try {
        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        # Destination spelled out: an overload that takes only a position would
        # draw at the picture's physical size and scale by dpi.
        $destination = New-Object System.Drawing.Rectangle (-$offsetX), (-$offsetY), $scaledWidth, $scaledHeight
        $g.DrawImage($Source, $destination, $Region, [System.Drawing.GraphicsUnit]::Pixel)
    } finally { $g.Dispose() }
    return $canvasImage
}

function Report([string] $label, [string] $path, [long] $wasBytes, [string] $note) {
    $file = Get-Item $path
    $image = [System.Drawing.Image]::FromFile($path)
    $shape = '{0}x{1}' -f $image.Width, $image.Height
    $image.Dispose()
    '{0,-8} {1,-11} {2,10:n0} bytes  (from {3:n0})  {4}' -f $label, $shape, $file.Length, $wasBytes, $note
}

# --- the figure: trimmed, flattened, then covered ---------------------------
$figureOut = Join-Path $Destination 'quality-sample-figure.png'
$figureWas = (Get-Item $Figure).Length
$source = [System.Drawing.Image]::FromFile($Figure)
try {
    # Copied into a 24-bit surface first: the blank-border scan reads three
    # bytes per pixel, and a PNG may arrive in any number of other formats.
    $whole = New-Object System.Drawing.Bitmap $source.Width, $source.Height, ([System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
    try {
        $g = [System.Drawing.Graphics]::FromImage($whole)
        try {
            $g.DrawImage($source, (New-Object System.Drawing.Rectangle 0, 0, $source.Width, $source.Height))
        } finally { $g.Dispose() }

        # The blank border goes on all four sides. Dropping only the top and
        # bottom would leave the drawing wider than the frame it has to fit,
        # and every pixel spent on that margin is one the reader cannot judge.
        $drawn = [Sample]::Drawn($whole, $BlankFloor)
        $figureNote = 'trimmed to {0}x{1} (from {2}x{3})' -f $drawn.Width, $drawn.Height, $whole.Width, $whole.Height

        # Scaled to the target width and left at whatever height that gives. The
        # application composes this with a block of text at 6, 8, 10 and 12 pt
        # in the reader's own language, and it needs the room below the drawing
        # to put it in - a chart already padded out to 16:9 here would only be
        # squeezed again there.
        $scale = $Width / $drawn.Width
        $drawnHeight = [Math]::Max(1, [int][Math]::Round($drawn.Height * $scale))
        $chart = New-Object System.Drawing.Bitmap $Width, $drawnHeight, ([System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
        try {
            $g = [System.Drawing.Graphics]::FromImage($chart)
            try {
                $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
                # Both rectangles declared with their type, and under names used
                # nowhere else. PowerShell hands a struct to a method as a
                # wrapped object and will not always unwrap it back to the
                # parameter's type; a typed variable forces the conversion at
                # the assignment, where it works. The type sticks to the name
                # for the rest of the script, though, and variable names here
                # are not case-sensitive - so these two do not borrow one.
                [System.Drawing.Rectangle] $chartDestination =
                    [System.Drawing.Rectangle]::new(0, 0, $Width, $drawnHeight)
                [System.Drawing.Rectangle] $chartRegion =
                    [System.Drawing.Rectangle]::new($drawn.X, $drawn.Y, $drawn.Width, $drawn.Height)
                $g.DrawImage($whole, $chartDestination, $chartRegion, [System.Drawing.GraphicsUnit]::Pixel)
            } finally { $g.Dispose() }
            [Sample]::Quantize($chart, $Levels)
            $chart.Save($figureOut, [System.Drawing.Imaging.ImageFormat]::Png)
        } finally { $chart.Dispose() }
    } finally { $whole.Dispose() }
} finally { $source.Dispose() }

# --- the photograph: covered, never re-encoded before that ------------------
$photoOut = Join-Path $Destination 'quality-sample-photo.png'
$photoWas = (Get-Item $Photo).Length
$source = [System.Drawing.Image]::FromFile($Photo)
try {
    $everything = New-Object System.Drawing.Rectangle 0, 0, $source.Width, $source.Height
    $photoNote = 'cropped from {0}x{1}' -f $source.Width, $source.Height
    $covered = New-Cover -Source $source -Region $everything -Centre $PhotoCentre
    try {
        $covered.Save($photoOut, [System.Drawing.Imaging.ImageFormat]::Png)
    } finally { $covered.Dispose() }
} finally { $source.Dispose() }

Report 'figure' $figureOut $figureWas $figureNote
Report 'photo'  $photoOut  $photoWas  $photoNote
