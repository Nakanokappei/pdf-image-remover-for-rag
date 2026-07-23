#!/usr/bin/env python3
"""Generate the Partner Center listing artwork (poster art, box art, tile icon).

Run from the repository root:

    python3 scripts/generate-store-art.py

Reads  assets/app-icon-master.png
Writes assets/store-listing/

Separate from generate-store-assets.py because these are marketing images with
type set on them, not package icon assets: the store shows them at large sizes
next to other products, so they carry the product name rather than being a bare
glyph on an empty canvas.

Partner Center asks for three, per listing language:

  * 9:16 poster art  — "strongly recommended"; used as the MAIN logo on
    Windows 10/11, and required for Xbox to look right
  * 1:1 box art      — used across various store layouts, and as the main logo
    when poster art is absent
  * 1:1 tile icon    — takes precedence over the icon inside the app package

Only .png is accepted, under 50 MB.
"""

import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parent.parent
MASTER = ROOT / "assets" / "app-icon-master.png"
OUT = ROOT / "assets" / "store-listing"

# Sizes Microsoft documents for each slot.
POSTER = (720, 1080)     # 9:16
BOX = (1080, 1080)       # 1:1
TILE = (300, 300)        # 1:1

# Hiragino covers Latin and Japanese in one family, so both languages share a
# look. W6 (index 2) is the semibold face.
FONT_PATH = "/System/Library/Fonts/Hiragino Sans GB.ttc"
FONT_INDEX = 2

# Product names, matching L10n.AppTitle.
NAMES = {
    "ja": "RAG 用\nPDF 画像除去ツール",
    "en": "PDF Image\nRemover for RAG",
}

INK = (38, 42, 48, 255)
BACKDROP_TOP = (255, 255, 255, 255)
BACKDROP_BOTTOM = (228, 235, 242, 255)


def backdrop(size: tuple[int, int]) -> Image.Image:
    """A soft vertical wash, light enough that the white icon paper still reads."""
    width, height = size
    image = Image.new("RGBA", size)
    draw = ImageDraw.Draw(image)
    # One horizontal line per row is fast enough at these sizes and avoids
    # pulling in a gradient dependency.
    for y in range(height):
        blend = y / max(height - 1, 1)
        colour = tuple(
            round(top + (bottom - top) * blend)
            for top, bottom in zip(BACKDROP_TOP, BACKDROP_BOTTOM)
        )
        draw.line([(0, y), (width, y)], fill=colour)
    return image


def draw_centred_text(canvas: Image.Image, text: str, font: ImageFont.FreeTypeFont,
                      top: int, line_gap: int) -> int:
    """Draw pre-broken lines centred horizontally; returns the y below the block."""
    draw = ImageDraw.Draw(canvas)
    y = top
    for line in text.split("\n"):
        left, upper, right, lower = draw.textbbox((0, 0), line, font=font)
        draw.text(((canvas.width - (right - left)) / 2 - left, y), line,
                  font=font, fill=INK)
        y += (lower - upper) + line_gap
    return y


def compose(size: tuple[int, int], master: Image.Image, name: str,
            icon_fraction: float, font_size: int) -> Image.Image:
    """Icon above, product name below, both centred on the wash."""
    canvas = backdrop(size)
    width, height = size

    icon_edge = round(width * icon_fraction)
    icon = master.resize((icon_edge, icon_edge), Image.LANCZOS)

    font = ImageFont.truetype(FONT_PATH, font_size, index=FONT_INDEX)
    line_gap = round(font_size * 0.22)
    line_count = name.count("\n") + 1
    text_height = line_count * (font_size + line_gap)

    # Centre the icon-plus-text block as a whole, so neither floats alone.
    block_height = icon_edge + round(font_size * 0.9) + text_height
    icon_top = (height - block_height) // 2
    canvas.paste(icon, ((width - icon_edge) // 2, icon_top), icon)

    draw_centred_text(canvas, name, font,
                      icon_top + icon_edge + round(font_size * 0.9), line_gap)
    return canvas


def main() -> None:
    master = Image.open(MASTER).convert("RGBA")
    OUT.mkdir(parents=True, exist_ok=True)

    for language, name in NAMES.items():
        # Poster art is tall, so the icon takes a smaller share of the width and
        # the name sits under it with room to breathe.
        compose(POSTER, master, name, icon_fraction=0.56, font_size=58).save(
            OUT / f"PosterArt-720x1080-{language}.png")
        compose(BOX, master, name, icon_fraction=0.48, font_size=76).save(
            OUT / f"BoxArt-1080x1080-{language}.png")
        print(f"{language}: poster + box art")

    # The tile icon replaces the package icon, so it stays a bare mark with the
    # master's transparency — no wash, no type.
    master.resize(TILE, Image.LANCZOS).save(OUT / "TileIcon-300x300.png")
    print("tile icon")

    total = len(list(OUT.glob('*.png')))
    print(f"{OUT.relative_to(ROOT)} now holds {total} files")


if __name__ == "__main__":
    main()
