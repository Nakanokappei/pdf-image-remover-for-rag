"""App icon for small sizes (16-24px), drawn pixel by pixel.

The 1024px master (assets/app-icon-master.png) carries a thin red border, a
photo-like picture symbol, and dissolving pixels. Downscaled below ~32px those
details turn into a grey blur with a washed-out border, so the small sizes are
drawn here instead — same motif, snapped to the pixel grid so every edge stays
crisp.

The motif follows the supplied 16/20px artwork: a red-bordered square sheet, a
picture of two overlapping peaks (a pale one behind, a dark one in front), a
cyan pixel breaking away at the right (the "image being removed"), and a body
text line below.

Geometry is a hand-tuned recipe per size rather than a scaled formula — at
these sizes rounding a formula costs a pixel here and there, and a pixel is a
large fraction of the icon. Adjusting a size means editing its integers below.

Used by generate-appicon.py (the .ico embedded in the exe) and
generate-store-assets.py (MSIX target sizes). Sizes above MAX_SIZE use the
master, which is sharper once there is room for its detail.
"""

from PIL import Image, ImageDraw

# Palette sampled from the supplied artwork.
RED = (230, 32, 32, 255)
DARK = (60, 64, 67, 255)      # front peak and ground line
MID = (140, 145, 150, 255)    # peak behind
BLUE = (0, 168, 240, 255)     # the pixel breaking away
LINE = (184, 188, 192, 255)   # body text line
PAPER = (255, 255, 255, 255)

# Largest size drawn here; above this the master downscales cleanly.
MAX_SIZE = 24

# Per-size geometry, in whole pixels and inclusive of both endpoints.
#   frame  — outer sheet rectangle       border — its stroke width
#   back   — pale peak triangle          front  — dark peak triangle
#   ground — the line both peaks sit on  body   — text line(s) below
#   blue   — the pixel breaking away at the right
RECIPES = {
    16: dict(
        frame=(1, 1, 14, 14), border=1,
        back=((9, 4), (6, 10), (12, 10)),
        front=((6, 6), (3, 10), (9, 10)),
        ground=(3, 10, 12, 10),
        body=[(3, 12, 11, 12)],
        blue=(12, 8, 12, 8),
    ),
    20: dict(
        frame=(1, 1, 18, 18), border=2,
        back=((12, 6), (8, 13), (16, 13)),
        front=((8, 8), (4, 13), (12, 13)),
        ground=(4, 13, 15, 13),
        body=[(4, 15, 14, 15)],
        blue=(15, 9, 16, 10),
    ),
    24: dict(
        frame=(1, 1, 22, 22), border=2,
        back=((15, 7), (10, 16), (20, 16)),
        front=((9, 9), (4, 16), (14, 16)),
        ground=(4, 16, 19, 16),
        body=[(4, 18, 17, 18)],
        blue=(18, 11, 19, 12),
    ),
}


def draw(size: int) -> Image.Image:
    """Draw the icon at one of the recipe sizes, on a transparent canvas."""
    if size not in RECIPES:
        raise ValueError(f"no recipe for {size}px (have {sorted(RECIPES)})")
    recipe = RECIPES[size]

    image = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    canvas = ImageDraw.Draw(image)

    # The sheet: white fill inside a red stroke. Drawn first so everything
    # else lands on top of the paper.
    canvas.rectangle(list(recipe["frame"]), fill=PAPER, outline=RED,
                     width=recipe["border"])

    # The picture: pale peak first, dark peak over it, then the ground line
    # that ties both to a common base.
    canvas.polygon(list(recipe["back"]), fill=MID)
    canvas.polygon(list(recipe["front"]), fill=DARK)
    canvas.rectangle(list(recipe["ground"]), fill=DARK)

    # Body text below the picture.
    for line in recipe["body"]:
        canvas.rectangle(list(line), fill=LINE)

    # The breaking-away pixel goes last so it stays visible over the peaks.
    canvas.rectangle(list(recipe["blue"]), fill=BLUE)

    return image
