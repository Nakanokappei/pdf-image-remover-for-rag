#!/usr/bin/env python3
"""Generate the Microsoft Store / MSIX image assets from the icon master.

Run from the repository root:

    python3 scripts/generate-store-assets.py

Reads  assets/app-icon-master.png  (1024x1024, transparent background)
Writes assets/store/                (package assets, overwritten on every run)

Listing artwork (poster art / box art / tile icon) is a separate concern and
lives in generate-store-art.py.

Sizes of 24px and below come from icon_small.py instead of the master — see
that module for why.

The set matches what a Windows Application Packaging Project expects, so the
output folder can be dropped in as the package's Assets folder and referenced
from Package.appxmanifest without renaming anything.
"""

from pathlib import Path
from PIL import Image

import icon_small

ROOT = Path(__file__).resolve().parent.parent
MASTER = ROOT / "assets" / "app-icon-master.png"
OUT = ROOT / "assets" / "store"

# Scale factors Windows requests for tile and logo assets. 100 is the base size.
SCALES = (100, 125, 150, 200, 400)

# Square assets, keyed by manifest name -> logical edge length in pixels.
SQUARE_ASSETS = {
    "Square44x44Logo": 44,
    "Square71x71Logo": 71,
    "Square150x150Logo": 150,
    "Square310x310Logo": 310,
    "StoreLogo": 50,
}

# Non-square assets: manifest name -> (logical width, logical height).
# The icon is drawn square and centred; the remaining width stays transparent.
# No SplashScreen — a full-trust desktop app never shows one.
WIDE_ASSETS = {
    "Wide310x150Logo": (310, 150),
}

# Target sizes for the app-list icon. Windows picks one per surface (taskbar,
# Start list, Alt+Tab, ...). The unplated variants are shown without the
# coloured plate behind them, so they must keep their transparency.
TARGET_SIZES = (16, 20, 24, 30, 32, 36, 40, 48, 56, 60, 64, 72, 80, 96, 256)
UNPLATED_SUFFIXES = ("", "_altform-unplated", "_altform-lightunplated")


def resized(master: Image.Image, edge: int) -> Image.Image:
    """Render the icon at the given edge length.

    Up to icon_small.MAX_SIZE the master's detail collapses into a grey blur,
    so those sizes use the simplified pixel-grid drawing instead.
    """
    if edge <= icon_small.MAX_SIZE:
        return icon_small.draw(edge)
    return master.resize((edge, edge), Image.LANCZOS)


def main() -> None:
    master = Image.open(MASTER).convert("RGBA")
    if master.width != master.height:
        raise SystemExit(f"master must be square, got {master.width}x{master.height}")

    OUT.mkdir(parents=True, exist_ok=True)
    written = 0

    # Square tiles and logos, one file per scale factor.
    for name, logical in SQUARE_ASSETS.items():
        for scale in SCALES:
            edge = round(logical * scale / 100)
            resized(master, edge).save(OUT / f"{name}.scale-{scale}.png")
            written += 1
        # StoreLogo is also referenced without a scale qualifier by some tooling.
        if name == "StoreLogo":
            resized(master, logical).save(OUT / "StoreLogo.png")
            written += 1

    # Wide tile and splash screen: square icon centred on a transparent canvas.
    for name, (logical_w, logical_h) in WIDE_ASSETS.items():
        for scale in SCALES:
            width = round(logical_w * scale / 100)
            height = round(logical_h * scale / 100)
            canvas = Image.new("RGBA", (width, height), (0, 0, 0, 0))
            # Leave a margin so the icon does not touch the tile edge.
            edge = round(height * 0.8)
            icon = resized(master, edge)
            canvas.paste(icon, ((width - edge) // 2, (height - edge) // 2), icon)
            canvas.save(OUT / f"{name}.scale-{scale}.png")
            written += 1

    # App-list icon at every target size, in plated and unplated variants.
    for size in TARGET_SIZES:
        icon = resized(master, size)
        for suffix in UNPLATED_SUFFIXES:
            icon.save(OUT / f"Square44x44Logo.targetsize-{size}{suffix}.png")
            written += 1

    print(f"wrote {written} files to {OUT.relative_to(ROOT)}")


if __name__ == "__main__":
    main()
