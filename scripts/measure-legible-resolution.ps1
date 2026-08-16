<#
.SYNOPSIS
    Measures the resolution at which small text still survives this app's image
    reduction, one writing system at a time.

.DESCRIPTION
    The resolutions offered in Tools -> Settings (140 / 200 / 300 dpi) were
    chosen with this script rather than guessed. It answers one question:

        below how many pixels per em does a reader stop telling the characters
        of this writing system apart?

    It renders a band of text at a resolution far above every candidate, then
    puts it through exactly what a save does to an image - a HighQualityBicubic
    redraw down to the target size, then a JPEG at the quality ceiling. What
    comes out is what an OCR or a vision model would be handed.

    THE METHOD MATTERS AS MUCH AS THE SCRIPT.

    1.  The text is random characters chosen for stroke density and mutual
        resemblance, never words. A word can be reconstructed from context long
        after its strokes have gone, which measures the reader rather than the
        resolution.
    2.  The answer key is written to key.txt and is NEVER printed. Do not open
        it until you have written your transcription down.
    3.  Read the images in ASCENDING order of resolution, and commit to each
        transcription before looking at the next. Reading a sharp one first
        tells you what the blurred one says.
    4.  Only then compare against key.txt and count substitutions. A dropped
        group counts for more than a substitution: text that goes missing is
        worse for retrieval than text that comes out wrong.

    KNOWN BIAS. For alphabetic and syllabic scripts (Latin, Vietnamese,
    Cyrillic, Devanagari, Hangul) random strings remove the distributional
    redundancy real text has - "toqfj" is harder than any real word, and
    the Hangul generator produces syllables that do not occur in Korean. Treat
    those figures as a floor. For the ideographic scripts the test is close to
    fair, because any character really can follow any other; the Japanese and
    Chinese numbers are the ones to trust.

    CONVERTING BETWEEN POINT SIZES. Legibility follows pixels per em, not dpi,
    so a threshold measured at one size transfers to another by proportion:

        required dpi = threshold px/em * 72 / point size

    The shipped values are the thresholds converted at 9 pt, which is the size
    a figure caption or a table cell is actually set in.

.PARAMETER Writing
    Which writing system to measure.

.PARAMETER PointSize
    The size the text is set at. 6 pt is the smallest a document is realistically
    set in; 9 pt is a caption; 10.5 pt is Japanese body text.

.PARAMETER Dpi
    The resolutions to produce, one image each. Read them low to high.

.PARAMETER JpegQuality
    The quality the images are encoded at. Matches the app's own default.

.PARAMETER Seed
    Changes the random text. Use a fresh seed whenever you re-measure something
    you have already seen the key for.

.EXAMPLE
    ./scripts/measure-legible-resolution.ps1 -Writing japanese -Dpi 150,220,300
#>

param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('latin', 'vietnamese', 'cyrillic', 'devanagari', 'hangul',
                 'japanese', 'chinese-traditional')]
    [string]$Writing,

    [ValidateRange(4, 72)]
    [double]$PointSize = 6,

    [int[]]$Dpi = @(92, 120, 150, 185, 220, 260, 300),

    [ValidateRange(1, 100)]
    [int]$JpegQuality = 85,

    [int]$Seed = 4242,

    [string]$OutputDirectory = (Join-Path $env:TEMP 'pdfimageremover-legible-resolution')
)

Add-Type -AssemblyName System.Drawing

# Every character is built from a code point rather than written literally.
# Windows PowerShell 5.1 decodes a UTF-8 file without a BOM as ANSI, and would
# turn each of the sets below into a parse error.
$ideographicSpace = [char]0x3000

# Per writing system: the confusable alphabet, two typefaces (a low-contrast
# face loses its strokes before a sturdy one), how many characters make a group,
# and what separates the groups.
switch ($Writing) {

    'latin' {
        # The pairs an OCR really confuses at small sizes.
        $points = 0x72,0x6E,0x6D,0x63,0x6C,0x64,0x31,0x49,0x30,0x4F,0x38,0x42,0x35,0x53,
                  0x36,0x47,0x32,0x5A,0x71,0x39,0x67,0x61,0x65,0x6F,0x73,0x7A,0x78,0x76,
                  0x79,0x75
        $fonts = 'Times New Roman', 'Arial'
        $groupSize = 5
        $separator = '  '
    }

    'vietnamese' {
        # Latin carrying stacked diacritics - the mark is what a reduction eats
        # first. Every character here carries one, which is harsher than real
        # Vietnamese, where the unmarked letters give the reader a foothold.
        $points = 0x1EAD,0x1EA9,0x1EAB,0x1EAF,0x1EB1,0x1EB3,0x1EB5,0x1EB7,0x1EBF,0x1EC1,
                  0x1EC3,0x1EC5,0x1EC7,0x1ED1,0x1ED3,0x1ED5,0x1ED7,0x1ED9,0x1EDB,0x1EDD,
                  0x1EDF,0x1EE1,0x1EE3,0x1EE9,0x1EEB,0x1EED,0x1EEF,0x1EF1,0x1ECB,0x0129,
                  0x1EC9,0x1EF9,0x1EF7,0x0111,0x1EA1,0x1EA3,0x00E3,0x00E1,0x00E0
        $fonts = 'Times New Roman', 'Arial'
        $groupSize = 5
        $separator = '  '
    }

    'cyrillic' {
        # Lower case, where the confusions live: the descender that tells sha
        # from shcha is the first thing to go.
        $points = 0x0438,0x0439,0x0446,0x0449,0x0448,0x044A,0x044B,0x044C,0x044D,0x044E,
                  0x044F,0x043D,0x043F,0x043B,0x0434,0x0431,0x0432,0x0433,0x0435,0x0436,
                  0x0437,0x043A,0x043C,0x043E,0x0440,0x0441,0x0442,0x0443,0x0444,0x0445,
                  0x0447,0x0430
        $fonts = 'Times New Roman', 'Arial'
        $groupSize = 5
        $separator = '  '
    }

    'devanagari' {
        # A consonant plus a vowel sign, which is the unit a reader resolves.
        # The sign is small and sits clear of the body, so it goes early.
        $consonants = 0x0915..0x0939
        $matras = 0x093E,0x093F,0x0940,0x0941,0x0942,0x0947,0x0948,0x094B,0x094C,0x0902
        $fonts = 'Nirmala UI', 'Nirmala UI'
        $groupSize = 3
        $separator = '  '
    }

    'hangul' {
        # Composed from jamo indices, always with a final consonant so every
        # syllable carries three parts rather than two. This produces syllables
        # that do not occur in Korean, which is why the figure is a floor.
        $fonts = 'Malgun Gothic', 'Malgun Gothic'
        $groupSize = 4
        $separator = '  '
    }

    'japanese' {
        # Kanji chosen for stroke density and for resembling one another.
        $points = 0x5132,0x511F,0x61B6,0x7A4D,0x7E54,0x8077,0x8B58,0x8B70,0x8B77,0x8B1B,
                  0x8CFC,0x74B0,0x9084,0x9078,0x9077,0x9F62,0x9B31,0x66DC,0x6FEF,0x8E8D,
                  0x7C4D,0x8584,0x7C3F,0x7D9A,0x7E3E,0x7DD1,0x7E01,0x89B3,0x89A7,0x9E97,
                  0x6B04,0x6F64,0x95B2,0x9B54,0x819C,0x81D3,0x8D08
        $fonts = 'Yu Mincho', 'Yu Gothic'
        $groupSize = 4
        $separator = $ideographicSpace
    }

    'chinese-traditional' {
        # The densest of the ideographic sets, and so the one that decides the
        # value shared by every complex script.
        $points = 0x9F8D,0x9451,0x7063,0x5EF3,0x9B31,0x89C0,0x91AB,0x9AD4,0x85DD,0x7E8C,
                  0x986F,0x8B93,0x9A5A,0x651D,0x8B70,0x8B77,0x6B0A,0x8B8A,0x5EE0,0x96A8,
                  0x908A,0x97FF,0x9858,0x7E54,0x8077,0x8B58,0x8D08,0x81DF,0x9F61,0x5C6C,
                  0x7051,0x91C0,0x9E7D,0x9748,0x9470
        $fonts = 'Microsoft JhengHei', 'Microsoft JhengHei'
        $groupSize = 4
        $separator = $ideographicSpace
    }
}

# A missing typeface would silently fall back to something else and measure the
# wrong thing, so it is worth refusing rather than guessing.
$installed = (New-Object System.Drawing.Text.InstalledFontCollection).Families | ForEach-Object { $_.Name }
foreach ($face in $fonts) {
    if ($installed -notcontains $face) {
        throw "The font '$face' is not installed, so '$Writing' cannot be measured on this machine."
    }
}

$rng = New-Object System.Random($Seed)

# One group of characters, drawn from whatever this writing system supplies.
function New-Group {
    $text = ''
    for ($i = 0; $i -lt $groupSize; $i++) {
        if ($Writing -eq 'hangul') {
            # syllable = 0xAC00 + (initial * 21 + medial) * 28 + final
            $initial = $rng.Next(19)
            $medial = $rng.Next(21)
            $final = 1 + $rng.Next(27)
            $text += [char](0xAC00 + (($initial * 21 + $medial) * 28) + $final)
        }
        elseif ($Writing -eq 'devanagari') {
            $text += [char]$consonants[$rng.Next($consonants.Count)]
            $text += [char]$matras[$rng.Next($matras.Count)]
        }
        else {
            $text += [char]$points[$rng.Next($points.Count)]
        }
    }
    return $text
}

# Five groups a line, spaced so no group can be read as a word, and four lines
# so both typefaces get two.
$lines = 1..4 | ForEach-Object {
    ((1..5 | ForEach-Object { New-Group }) -join $separator)
}

if (-not (Test-Path $OutputDirectory)) {
    New-Item -ItemType Directory $OutputDirectory | Out-Null
}
$lines | Set-Content -Path (Join-Path $OutputDirectory 'key.txt') -Encoding utf8

# The master is rendered well above the highest candidate, so that it is never
# itself the limit on what the reader can see.
$highest = ($Dpi | Measure-Object -Maximum).Maximum
$sourceDpi = [Math]::Max(600, $highest * 2)
$emPixels = $PointSize * $sourceDpi / 72.0
$lineHeight = $emPixels * 1.75

$fontA = New-Object System.Drawing.Font($fonts[0], $emPixels, [System.Drawing.GraphicsUnit]::Pixel)
$fontB = New-Object System.Drawing.Font($fonts[1], $emPixels, [System.Drawing.GraphicsUnit]::Pixel)

# Sized to the text rather than to a fixed rectangle, so a different point size
# does not silently crop the last group off every line.
$margin = [int][Math]::Round($emPixels)
$measured = 0.0
$probe = New-Object System.Drawing.Bitmap(1, 1)
$probeGraphics = [System.Drawing.Graphics]::FromImage($probe)
foreach ($line in $lines) {
    foreach ($face in @($fontA, $fontB)) {
        $measured = [Math]::Max($measured, $probeGraphics.MeasureString($line, $face).Width)
    }
}
$probeGraphics.Dispose()
$probe.Dispose()

$width = [int][Math]::Ceiling($measured) + ($margin * 2)
$height = [int][Math]::Ceiling($lineHeight * $lines.Count) + ($margin * 2)

$master = New-Object System.Drawing.Bitmap($width, $height, [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
$graphics = [System.Drawing.Graphics]::FromImage($master)
$graphics.Clear([System.Drawing.Color]::White)
$graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit

$y = [double]$margin
for ($i = 0; $i -lt $lines.Count; $i++) {
    $face = if ($i -lt 2) { $fontA } else { $fontB }
    $graphics.DrawString($lines[$i], $face, [System.Drawing.Brushes]::Black, [double]$margin, $y)
    $y += $lineHeight
}
$graphics.Dispose()
$fontA.Dispose()
$fontB.Dispose()

$codec = [System.Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() |
    Where-Object { $_.FormatID -eq [System.Drawing.Imaging.ImageFormat]::Jpeg.Guid }
$encoderParameters = New-Object System.Drawing.Imaging.EncoderParameters(1)
$encoderParameters.Param[0] = New-Object System.Drawing.Imaging.EncoderParameter(
    [System.Drawing.Imaging.Encoder]::Quality, $JpegQuality)

# One file per resolution, produced the way the app produces one.
$report = foreach ($target in ($Dpi | Sort-Object)) {
    $scale = $target / $sourceDpi
    $w = [Math]::Max(1, [int][Math]::Round($width * $scale))
    $h = [Math]::Max(1, [int][Math]::Round($height * $scale))

    $small = New-Object System.Drawing.Bitmap($w, $h, [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $smallGraphics = [System.Drawing.Graphics]::FromImage($small)
    $smallGraphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $smallGraphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $smallGraphics.DrawImage($master, 0, 0, $w, $h)
    $smallGraphics.Dispose()

    $path = Join-Path $OutputDirectory ("dpi-{0:d3}.jpg" -f $target)
    $small.Save($path, $codec, $encoderParameters)
    $small.Dispose()

    [pscustomobject]@{
        Dpi        = $target
        PixelsPerEm = [Math]::Round($PointSize * $target / 72.0, 1)
        Size       = "$w x $h"
        File       = $path
    }
}
$master.Dispose()

Write-Output ("{0}, {1} pt, JPEG quality {2}, seed {3}" -f $Writing, $PointSize, $JpegQuality, $Seed)
$report | Format-Table -AutoSize
Write-Output "Read them lowest first, write down what you see, and only then open key.txt."
