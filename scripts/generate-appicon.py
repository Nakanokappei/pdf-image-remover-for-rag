#!/usr/bin/env python3
"""Generate the application icon (.ico) embedded in the exe and title bar.

Run from the repository root:

    python3 scripts/generate-appicon.py

Reads  assets/app-icon-master.png
Writes src/PdfImageRemoverForRag.App/appicon.ico

The file carries seven sizes. Windows picks one per surface: 16 and 24 for the
title bar and small taskbar / Explorer views, the rest for larger views. The
16 and 24 entries come from icon_small.py because the master's detail does not
survive the downscale; 32 and above are the master itself.
"""

from pathlib import Path
from PIL import Image

import icon_small

ROOT = Path(__file__).resolve().parent.parent
MASTER = ROOT / "assets" / "app-icon-master.png"
OUT = ROOT / "src" / "PdfImageRemoverForRag.App" / "appicon.ico"

SIZES = (16, 24, 32, 48, 64, 128, 256)


def main() -> None:
    master = Image.open(MASTER).convert("RGBA")

    # Build every frame explicitly so the small ones can use the simplified
    # drawing; Pillow would otherwise downscale the master for all of them.
    frames = [
        icon_small.draw(size) if size <= icon_small.MAX_SIZE
        else master.resize((size, size), Image.LANCZOS)
        for size in SIZES
    ]

    # The largest frame is the base image; the rest ride along as extra entries.
    frames[-1].save(OUT, format="ICO",
                    sizes=[(s, s) for s in SIZES],
                    append_images=frames[:-1])

    written = Image.open(OUT)
    print(f"wrote {OUT.relative_to(ROOT)} — sizes {sorted(written.info['sizes'])}")


if __name__ == "__main__":
    main()
