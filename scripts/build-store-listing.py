#!/usr/bin/env python3
"""Build the folder Partner Center imports: one CSV, sixteen languages, eighty pictures.

    python scripts/build-store-listing.py <exported.csv> [output-folder]

Partner Center exports a CSV whose rows are listing fields and whose columns are
languages. Adding a language is adding a column; adding a picture that has never
been uploaded is writing a path relative to the folder the CSV sits in, and
importing the FOLDER rather than the file.

So this reads the export, keeps its rows exactly as they came (the Field, ID and
Type columns must not change or the import is refused), adds a column per
language, and fills it from docs/store-listing-i18n/<locale>.json plus the
screenshots already taken for that language.

Why a script and not a spreadsheet: sixteen languages times five pictures is
eighty paths that have to agree with eighty files, and a typo in one of them is
an error message from a server after a long upload.
"""

import csv
import json
import shutil
import sys
from pathlib import Path

# The store's language-locale code, and the language the app's screenshots were
# taken in. They are not the same alphabet: the store wants a region, the app
# names its translations by the culture it resolves them under.
LOCALES = {
    "en-us": "en",
    "ja-jp": "ja",
    "de-de": "de",
    "fr-fr": "fr",
    "es-es": "es",
    "it-it": "it",
    "pt-br": "pt",
    "ru-ru": "ru",
    "ko-kr": "ko",
    "zh-cn": "zh-Hans",
    "zh-tw": "zh-Hant",
    "id-id": "id",
    "ms-my": "ms",
    "hi-in": "hi",
    "tr-tr": "tr",
    "vi-vn": "vi",
}

# The screenshots, in the order they appear in the listing.
VIEWS = ["table", "tiles", "objects", "shown-types", "usage", "settings"]

TRANSLATIONS = Path("docs/store-listing-i18n")
SCREENSHOTS = Path("docs/images")


def main() -> int:
    if len(sys.argv) < 2:
        print(__doc__)
        return 1

    # --text-only writes the CSV and nothing else, leaving every picture field
    # exactly as it was exported: URLs to what Partner Center already holds.
    # That is the import that goes through when a folder import does not.
    text_only = "--text-only" in sys.argv
    arguments = [a for a in sys.argv[1:] if not a.startswith("--")]

    exported = Path(arguments[0])
    output = Path(arguments[1] if len(arguments) > 1 else "artifacts/store-listing")
    root = output.name                      # the path in the CSV starts with it

    rows = list(csv.reader(exported.open(encoding="utf-8-sig", newline="")))
    header, body = rows[0], rows[1:]

    # Values per locale: the translated text, then the pictures.
    values = {}
    for locale, language in LOCALES.items():
        text = json.loads((TRANSLATIONS / f"{locale}.json").read_text(encoding="utf-8"))
        for index, view in enumerate(VIEWS, start=1):
            picture = SCREENSHOTS / f"store-{view}-{language}.png"
            if not picture.exists():
                print(f"missing screenshot: {picture}")
                return 1
            if not text_only:
                text[f"DesktopScreenshot{index}"] = f"{root}/images/{picture.name}"
        values[locale] = text

    # One column per locale, in the order above, keeping any the export already
    # had so nothing that is live is dropped.
    columns = list(header)
    for locale in LOCALES:
        if locale not in columns:
            columns.append(locale)

    written = []
    for row in body:
        row = row + [""] * (len(columns) - len(row))
        field = row[0]
        for locale, text in values.items():
            if field in text:
                row[columns.index(locale)] = text[field]
        written.append(row)

    output.mkdir(parents=True, exist_ok=True)
    if not text_only:
        (output / "images").mkdir(exist_ok=True)
        for locale, language in LOCALES.items():
            for view in VIEWS:
                picture = SCREENSHOTS / f"store-{view}-{language}.png"
                shutil.copy2(picture, output / "images" / picture.name)

    # UTF-8 with a BOM and CRLF, which is what the export was and what the
    # importer reads without being told.
    with (output / "listingData.csv").open("w", encoding="utf-8-sig", newline="") as file:
        writer = csv.writer(file, quoting=csv.QUOTE_MINIMAL)
        writer.writerow(columns)
        writer.writerows(written)

    print(f"{output}/listingData.csv: {len(columns)} columns, {len(written)} rows")
    if text_only:
        print("import this FILE in Partner Center (Import listings -> Import .csv)")
        return 0

    pictures = len({f"{v}-{l}" for l in LOCALES.values() for v in VIEWS})
    print(f"{output}/images: {pictures} pictures")
    print("import this FOLDER in Partner Center (Import listings -> Import folder)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
