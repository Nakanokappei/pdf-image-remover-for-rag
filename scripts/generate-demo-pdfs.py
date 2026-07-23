#!/usr/bin/env python3
"""Generate demo PDFs for screenshots. Run on macOS from the repository root.

    scratchpad-venv/bin/python scripts/generate-demo-pdfs.py demo-pdfs/

Needs reportlab (not a project dependency; install it into a throwaway venv).

Why this exists
---------------
The store listing needs screenshots that show the product doing real work: a
long list of removable objects, a shared logo whose usage count runs into the
dozens, repeated footers, and ruling lines. Real-world PDFs would show that,
but their logos and text are somebody else's copyright, and the screenshots go
out on the Microsoft Store. So we generate documents for a clearly fictitious
organisation instead.

What it produces
----------------
Three PDFs for the same fictitious institute, 8 pages each. They deliberately
share content so the cross-file grouping is visible:

  * one logo bitmap, drawn on every page of all three files (24 occurrences)
  * one footer string, repeated on every page (24 occurrences)
  * header/footer rules with identical width and colour (48 occurrences)
  * per-file chart images and tables, so each file also has unique rows

The generated PDFs are NOT committed (demo-pdfs/ is gitignored): they embed a
subset of MS PGothic, and redistributing font data is a licensing question we
do not need to have. The script is committed so anyone can regenerate them.

Rows the app finds per file: 3 images (shared logo + 2 charts), 4 repeated
strings (organisation, title, footer), 9 table labels, 3 shapes — 19 in all.
Open all three at once and the shared logo, strings and rules merge into one
row each, with usage counts in the twenties and forties.
"""

import sys
from io import BytesIO
from pathlib import Path

from PIL import Image, ImageDraw
from reportlab.lib.colors import Color
from reportlab.lib.pagesizes import A4
from reportlab.lib.utils import ImageReader
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.pdfgen import canvas

# Japanese font, subset-embedded by reportlab — which also writes the
# /ToUnicode map the app's text extraction relies on. Note that reportlab
# cannot read PostScript-outline fonts, which rules out Hiragino and the
# Morisawa OTFs; these candidates all carry TrueType outlines. Ordered by
# preference: MS PGothic matches what the screenshots will show on Windows.
FONT_CANDIDATES = [
    (str(Path.home() / "Library/Fonts/msgothic.ttc"), 2),   # MS PGothic
    (str(Path.home() / "Library/Fonts/msgothic.ttc"), 0),   # MS Gothic
    ("/System/Library/Fonts/Supplemental/Arial Unicode.ttf", 0),
]
FONT_REGULAR, FONT_BOLD = "JaRegular", "JaBold"

# A fictitious organisation. Nothing here refers to a real company.
ORG_JA = "サンプル総合研究所"
ORG_EN = "Sample Research Institute"

PAGE_W, PAGE_H = A4
MARGIN = 50
BRAND = Color(0.09, 0.36, 0.56)     # header rule / logo
RULE = Color(0.72, 0.75, 0.78)      # thin separators
BODY = Color(0.18, 0.19, 0.21)


def make_logo() -> Image.Image:
    """Draw the institute's mark: a ring with a wedge cut out, plus a bar."""
    size = 240
    image = Image.new("RGB", (size, size), (255, 255, 255))
    d = ImageDraw.Draw(image)
    brand = (23, 92, 143)
    accent = (0, 168, 200)
    # Outer ring.
    d.ellipse([10, 10, size - 10, size - 10], outline=brand, width=22)
    # A quadrant of the ring in the accent colour, so the mark is not symmetric.
    d.arc([10, 10, size - 10, size - 10], start=-40, end=50, fill=accent, width=22)
    # Inner bar, offset from centre.
    d.rectangle([70, 108, size - 70, 132], fill=brand)
    return image


def make_chart(kind: str, seed: int) -> Image.Image:
    """Draw a simple chart bitmap. Deterministic from seed, no randomness."""
    w, h = 520, 300
    image = Image.new("RGB", (w, h), (250, 251, 252))
    d = ImageDraw.Draw(image)
    brand = (23, 92, 143)
    accent = (0, 168, 200)

    # Axes, common to both chart kinds.
    d.line([60, h - 50, w - 30, h - 50], fill=(150, 155, 160), width=2)
    d.line([60, 30, 60, h - 50], fill=(150, 155, 160), width=2)

    if kind == "bars":
        # Bar heights come from the seed so each file gets a different chart.
        values = [(seed * 7 + i * 13) % 60 + 25 for i in range(7)]
        slot = (w - 110) // len(values)
        for i, value in enumerate(values):
            x = 75 + i * slot
            top = h - 50 - value * 2.6
            d.rectangle([x, top, x + slot - 14, h - 52],
                        fill=brand if i % 2 == 0 else accent)
    else:
        # A polyline trend with markers.
        values = [(seed * 5 + i * 17) % 55 + 20 for i in range(9)]
        points = [(70 + i * ((w - 110) / 8), h - 52 - v * 2.8)
                  for i, v in enumerate(values)]
        d.line(points, fill=brand, width=4)
        for x, y in points:
            d.ellipse([x - 5, y - 5, x + 5, y + 5], fill=accent)

    return image


class DemoDocument:
    """One PDF: fixed furniture on every page, plus per-page body content."""

    def __init__(self, path: Path, title: str, logo: ImageReader, japanese: bool):
        self.canvas = canvas.Canvas(str(path), pagesize=A4)
        self.canvas.setTitle(title)
        self.title = title
        self.logo = logo
        self.japanese = japanese
        self.page_number = 0

    def text(self, ja: str, en: str) -> str:
        return ja if self.japanese else en

    def draw_furniture(self) -> None:
        """Header logo + title and footer text, drawn identically on each page.

        This is what the app is meant to find: the same logo XObject, the same
        footer string, and the same two rules, over and over.
        """
        c = self.canvas
        # Header: the shared logo bitmap. Passing the same ImageReader every
        # time makes reportlab emit one XObject and reference it repeatedly —
        # exactly the case the app groups into a single row.
        c.drawImage(self.logo, MARGIN, PAGE_H - 88, width=44, height=44, mask=None)
        c.setFont(FONT_BOLD, 12)
        c.setFillColor(BRAND)
        c.drawString(MARGIN + 56, PAGE_H - 66,
                     self.text(ORG_JA, ORG_EN))
        c.setFont(FONT_REGULAR, 9)
        c.setFillColor(BODY)
        c.drawString(MARGIN + 56, PAGE_H - 80, self.title)

        # Header rule and footer rule: identical geometry, width and colour, so
        # they collapse into one shape group with a high usage count.
        c.setStrokeColor(RULE)
        c.setLineWidth(0.8)
        c.line(MARGIN, PAGE_H - 96, PAGE_W - MARGIN, PAGE_H - 96)
        c.line(MARGIN, 62, PAGE_W - MARGIN, 62)

        # Footer: the repeated string the text detector should surface.
        c.setFont(FONT_REGULAR, 8)
        c.setFillColor(Color(0.45, 0.47, 0.5))
        c.drawString(MARGIN, 48, self.text(f"{ORG_JA}｜社外秘", f"{ORG_EN} | Confidential"))
        c.drawRightString(PAGE_W - MARGIN, 48, str(self.page_number))

    def paragraph(self, y: float, lines: list[str], leading: float = 15) -> float:
        """Draw pre-wrapped body lines, returning the y below the block."""
        self.canvas.setFont(FONT_REGULAR, 9.5)
        self.canvas.setFillColor(BODY)
        for line in lines:
            self.canvas.drawString(MARGIN, y, line)
            y -= leading
        return y

    def heading(self, y: float, text: str) -> float:
        self.canvas.setFont(FONT_BOLD, 13)
        self.canvas.setFillColor(BRAND)
        self.canvas.drawString(MARGIN, y, text)
        return y - 24

    def table(self, y: float, headers: list[str], rows: list[list[str]]) -> float:
        """A ruled table: every horizontal rule is another repeated shape."""
        c = self.canvas
        columns = len(headers)
        col_w = (PAGE_W - 2 * MARGIN) / columns
        row_h = 20

        # Header band.
        c.setFillColor(Color(0.93, 0.95, 0.97))
        c.rect(MARGIN, y - row_h, PAGE_W - 2 * MARGIN, row_h, stroke=0, fill=1)
        c.setFont(FONT_BOLD, 8.5)
        c.setFillColor(BODY)
        for i, head in enumerate(headers):
            c.drawString(MARGIN + 6 + i * col_w, y - 14, head)

        # Body rows, each closed by a rule of identical width and colour.
        c.setFont(FONT_REGULAR, 8.5)
        for r, row in enumerate(rows):
            row_y = y - row_h * (r + 2)
            for i, cell in enumerate(row):
                c.drawString(MARGIN + 6 + i * col_w, row_y + 6, cell)
            c.setStrokeColor(RULE)
            c.setLineWidth(0.5)
            c.line(MARGIN, row_y, PAGE_W - MARGIN, row_y)

        return y - row_h * (len(rows) + 1) - 20

    def new_page(self) -> None:
        if self.page_number:
            self.canvas.showPage()
        self.page_number += 1
        self.draw_furniture()

    def save(self) -> None:
        self.canvas.showPage()
        self.canvas.save()


# Body copy per document, as templates filled with page-derived numbers.
#
# Why templates rather than fixed prose: an earlier version drew the same
# paragraph on every page, and the app duly listed each body sentence as
# "repeated text" — 28 rows of it. No real document repeats its body, so the
# screenshots looked wrong and oversold what the text detector is for. Varying
# the figures per page leaves only the genuine furniture (organisation name,
# document title, footer) repeating, which is exactly what users remove.
BODY_TEMPLATES = {
    "quarterly": {
        "ja": [
            "本節では、第 {a} 期における研究開発活動の進捗を整理する。対象期間中の",
            "試験件数は {b} 件であり、前年同期比で {c} %の増加となった。",
            "",
            "材料解析部門の稼働率は {d} %で推移し、目標値を上回った。計測技術部門で",
            "は試作機 {e} 台を追加導入し、待ち時間を平均 {f} 時間短縮している。",
            "データ処理基盤の更新は、次期の重点課題として継続する。",
        ],
        "en": [
            "This section reviews research and development activity for period {a}.",
            "During the period the division ran {b} tests, an increase of {c} % over",
            "the same period last year.",
            "",
            "Utilisation in materials analysis held at {d} %, above target. Measurement",
            "technology brought {e} additional prototypes into service, cutting average",
            "queue time by {f} hours. Platform renewal continues as a priority.",
        ],
    },
    "catalog": {
        "ja": [
            "本節では、標準メニュー {a} 群のサービス仕様を示す。掲載の標準納期は",
            "{b} 営業日、同時受付可能な試料数は {c} 点を上限とする。",
            "",
            "測定条件の変更を伴う場合、納期は最大 {d} 営業日まで延長されることが",
            "ある。試料の前処理が必要な場合は、別途 {e} 営業日を見込まれたい。",
            "詳細は各部門の窓口まで照会されたい。",
        ],
        "en": [
            "This section sets out the specifications for service group {a}. The",
            "standard lead time is {b} working days, for up to {c} samples handled",
            "together.",
            "",
            "Where measurement conditions have to be altered, lead time may extend to",
            "{d} working days. Allow a further {e} working days if the sample requires",
            "preparation. Contact the relevant division for details.",
        ],
    },
    "technical": {
        "ja": [
            "本節では、条件 {a} における信号対雑音比の測定結果を述べる。取得時間を",
            "{b} 秒に固定したとき、S/N 比は {c} dB であった。",
            "",
            "帯域制限を併用した場合、同一取得時間で {d} dB の改善が得られた。ただし",
            "立ち上がり時間は {e} %増加しており、急峻な信号を含む試料では波形の",
            "なまりが観測される。適用範囲の見極めが今後の課題である。",
        ],
        "en": [
            "This section reports signal-to-noise measurements under condition {a}.",
            "With acquisition time fixed at {b} seconds, the ratio measured {c} dB.",
            "",
            "Adding band limiting improved this by {d} dB at equal acquisition time.",
            "Rise time grew by {e} %, however, and samples containing sharp signal",
            "transitions show visible rounding of the waveform. Determining where the",
            "method applies remains future work.",
        ],
    },
}


def body_for_page(kind: str, japanese: bool, page: int) -> list[str]:
    """Fill one document's templates with numbers derived from the page.

    The arithmetic only needs to be deterministic and plausible; it exists so
    that no two pages produce an identical line.
    """
    lines = BODY_TEMPLATES[kind]["ja" if japanese else "en"]
    values = {
        "a": page,
        "b": 120 + page * 37,
        "c": round(4.2 + page * 1.7, 1),
        "d": round(82.0 + page * 1.3, 1),
        "e": 2 + page,
        "f": round(0.8 + page * 0.4, 1),
    }
    return [line.format(**values) if "{" in line else line for line in lines]


def rows_for_page(rows: list[list[str]], page: int) -> list[list[str]]:
    """Vary the numeric columns per page so table text does not repeat either.

    The first column (a label) stays put; any cell containing a digit gets a
    page-dependent suffix digit swapped in, which keeps the column widths and
    the overall look while making every string unique.
    """
    varied = []
    for index, row in enumerate(rows):
        cells = [row[0]]
        for cell in row[1:]:
            # Replace the final digit with one derived from the page and row.
            digits = [c for c in cell if c.isdigit()]
            if digits:
                last = digits[-1]
                cell = cell[::-1].replace(last, str((page + index) % 10), 1)[::-1]
            cells.append(cell)
        varied.append(cells)
    return varied


def register_japanese_font() -> None:
    """Register the first usable candidate under both font names.

    There is no separate bold face available, so headings rely on size and
    colour instead; FONT_BOLD is registered against the same outlines to keep
    the drawing code readable.
    """
    for path, index in FONT_CANDIDATES:
        if not Path(path).exists():
            continue
        try:
            pdfmetrics.registerFont(TTFont(FONT_REGULAR, path, subfontIndex=index))
            pdfmetrics.registerFont(TTFont(FONT_BOLD, path, subfontIndex=index))
        except Exception:
            continue
        print(f"font: {Path(path).name} #{index}")
        return
    raise SystemExit("no usable Japanese TrueType font found; see FONT_CANDIDATES")


def build(path: Path, title: str, kind: str, logo: ImageReader,
          charts: list[ImageReader], japanese: bool,
          table_headers: list[str], table_rows: list[list[str]]) -> None:
    """Write one 8-page document.

    Every page carries the same furniture (logo, organisation name, footer,
    rules) but different body content, so the repeated-object list the app
    produces reflects a real document rather than the generator's laziness.
    """
    doc = DemoDocument(path, title, logo, japanese)

    # Page 1: title + opening copy + the first chart.
    doc.new_page()
    y = doc.heading(PAGE_H - 130, title)
    y = doc.paragraph(y, body_for_page(kind, japanese, 1))
    doc.canvas.drawImage(charts[0], MARGIN, y - 210, width=300, height=173, mask=None)

    # Pages 2-8: alternating table and chart pages.
    for page in range(2, 9):
        doc.new_page()
        y = doc.heading(PAGE_H - 130,
                        doc.text(f"第 {page - 1} 節", f"Section {page - 1}"))
        y = doc.paragraph(y, body_for_page(kind, japanese, page))
        if page % 2 == 0:
            y = doc.table(y, table_headers, rows_for_page(table_rows, page))
        else:
            doc.canvas.drawImage(charts[page % len(charts)], MARGIN, y - 210,
                                 width=300, height=173, mask=None)

    doc.save()


def main() -> None:
    if len(sys.argv) != 2:
        raise SystemExit("usage: generate-demo-pdfs.py <output-directory>")
    out = Path(sys.argv[1])
    out.mkdir(parents=True, exist_ok=True)

    register_japanese_font()

    # One logo, shared by every document: the same PNG bytes go into all three
    # files, so their stream hashes match and the app groups them as one row.
    logo_png = BytesIO()
    make_logo().save(logo_png, format="PNG")
    logo = ImageReader(BytesIO(logo_png.getvalue()))

    # Charts are per-file, giving each document rows of its own.
    charts = {seed: ImageReader(BytesIO(chart_bytes(seed)))
              for seed in range(1, 7)}

    documents = [
        ("四半期報告書_2026Q1.pdf", "2026年度 第1四半期 研究開発報告書", "quarterly", True,
         ["部門", "試験件数", "前年同期比", "稼働率"],
         [["材料解析", "1,284", "+18.2 %", "92.4 %"],
          ["計測技術", "  876", "+ 4.1 %", "88.1 %"],
          ["データ処理", "  512", "- 2.6 %", "79.5 %"],
          ["環境試験", "  349", "+ 9.8 %", "85.0 %"],
          ["品質保証", "  221", "+ 1.4 %", "90.2 %"]]),
        ("サービスカタログ_2026.pdf", "受託測定・解析サービス カタログ", "catalog", True,
         ["サービス", "標準納期", "試料数", "参考価格"],
         [["表面形状測定", "5 営業日", "1〜10", "¥48,000"],
          ["組成分析", "7 営業日", "1〜5", "¥96,000"],
          ["熱特性評価", "10 営業日", "1〜3", "¥132,000"],
          ["耐久試験", "20 営業日", "1〜2", "¥280,000"],
          ["振動解析", "7 営業日", "1〜8", "¥74,000"]]),
        ("技術レポート_信号処理.pdf", "走査型計測装置における信号対雑音比の改善", "technical", True,
         ["条件", "取得時間", "S/N 比", "改善量"],
         [["従来手法", "120 s", "38.2 dB", "—"],
          ["帯域制限 A", "120 s", "40.1 dB", "+1.9 dB"],
          ["帯域制限 B", "120 s", "40.6 dB", "+2.4 dB"],
          ["帯域制限 C", "120 s", "39.4 dB", "+1.2 dB"],
          ["参考（長時間）", "480 s", "41.0 dB", "+2.8 dB"]]),
    ]
    english = [
        ("Quarterly-Report-2026Q1.pdf", "Research & Development Report, Q1 FY2026", "quarterly", False,
         ["Division", "Tests", "YoY", "Uptime"],
         [["Materials analysis", "1,284", "+18.2 %", "92.4 %"],
          ["Measurement tech", "  876", "+ 4.1 %", "88.1 %"],
          ["Data processing", "  512", "- 2.6 %", "79.5 %"],
          ["Environmental", "  349", "+ 9.8 %", "85.0 %"],
          ["Quality assurance", "  221", "+ 1.4 %", "90.2 %"]]),
        ("Service-Catalogue-2026.pdf", "Measurement & Analysis Services Catalogue", "catalog", False,
         ["Service", "Lead time", "Samples", "Indicative price"],
         [["Surface profiling", "5 days", "1-10", "48,000 JPY"],
          ["Composition analysis", "7 days", "1-5", "96,000 JPY"],
          ["Thermal evaluation", "10 days", "1-3", "132,000 JPY"],
          ["Durability testing", "20 days", "1-2", "280,000 JPY"],
          ["Vibration analysis", "7 days", "1-8", "74,000 JPY"]]),
        ("Technical-Report-Signal.pdf", "Improving Signal-to-Noise Ratio in Scanning Equipment", "technical", False,
         ["Condition", "Acquisition", "S/N", "Gain"],
         [["Conventional", "120 s", "38.2 dB", "-"],
          ["Band limit A", "120 s", "40.1 dB", "+1.9 dB"],
          ["Band limit B", "120 s", "40.6 dB", "+2.4 dB"],
          ["Band limit C", "120 s", "39.4 dB", "+1.2 dB"],
          ["Reference (long)", "480 s", "41.0 dB", "+2.8 dB"]]),
    ]

    for group, folder in ((documents, out / "ja"), (english, out / "en")):
        folder.mkdir(exist_ok=True)
        for index, (name, title, kind, japanese, headers, rows) in enumerate(group):
            chart_set = [charts[index * 2 + 1], charts[index * 2 + 2],
                         charts[(index * 2) % 6 + 1]]
            build(folder / name, title, kind, logo, chart_set, japanese, headers, rows)
            size_kb = (folder / name).stat().st_size // 1024
            print(f"{folder.name}/{name}  ({size_kb} KB)")


def chart_bytes(seed: int) -> bytes:
    buffer = BytesIO()
    make_chart("bars" if seed % 2 else "line", seed).save(buffer, format="PNG")
    return buffer.getvalue()


if __name__ == "__main__":
    main()
