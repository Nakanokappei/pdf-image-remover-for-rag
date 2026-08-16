# PDF Image Remover for RAG

A Windows 11 desktop tool that takes images, repeated text, shapes, drawings, and shadows out of a PDF before you feed it to a RAG pipeline.

Company logos, headers, footers, watermarks, and page rules degrade retrieval quality and inflate preprocessing cost once a PDF lands in a RAG / Dify pipeline. This tool lists every removable object in your PDFs, lets you check what you want gone, and writes new PDFs without them. A removed image is taken out of the file itself, not merely stopped from being painted — otherwise a pipeline that reads a PDF by enumerating its objects still finds it. **The original files are never overwritten.**

Some text cannot simply be deleted — a chart's axis labels, a caption over a photograph — because the picture breaks without it. For those, the **Graphics objects** panel flattens the overlapping objects into a single image: the page looks exactly the same, and the text in that place stops being text.

Everything runs locally — files never leave your PC, and no data is collected.

**Manual: [English](docs/manual.en.md) · [Japanese](docs/manual.ja.md)** — also reachable from the app's Help menu.

## What it removes

| Kind | What is listed | Notes |
| --- | --- | --- |
| **Images** | Every drawn Image XObject | The same logo on 50 pages — and across every open file — is one row |
| **Text** | Strings with at least one visible character, shown 2+ times in a file | Headers, footers, watermarks — a one-letter confidentiality marking counts. Whitespace-only strings are never listed. CJK/composite fonts are decoded via `/ToUnicode` |
| **Shapes** | Every drawn line, rectangle, and curve | Identity is shape + line width + color; position is ignored |
| **Drawings** | Artwork a Form XObject paints, listed as one object | A silhouette with a speech bubble is one row, not one row per path. Identity is the form's stream hash, so one form on eleven pages is one row |
| **Shadows** | Images holding one flat color, shaped by a soft mask | What a drop shadow becomes when a document is exported to PDF. Barely visible on the page, but a reader that walks the file's objects writes the color out and drops the mask, so each one arrives as a solid black rectangle |

## Features

- **Open several PDFs at once.** Identical objects merge into one row across files, so one check box removes a shared logo from every file.
- **Two views.** A spreadsheet-style table, sortable on any column, and a thumbnail tile view, always in the same order.
- **A thumbnail for everything.** Images are decoded, text is drawn as text, and shapes and drawings are rendered from their real paths in their real colors.
- **Flatten overlaps into an image.** Select a row and the Graphics objects panel lists every place that object is drawn, laid out like an image editor's layers panel. Where objects overlap you can replace the place with a picture of exactly what was there, so the page keeps its appearance while the text in it leaves the text layer. Flattening takes effect when you press it and can be undone.
- **Remove one drawing of an object.** Closing an object's eye in that panel drops that single drawing at the next save, on that page only, and leaves the other pages alone.
- **Shrink the images on the way out.** Tools → Settings picks the resolution a saved PDF's images are redrawn at: 92 dpi for the smallest file, 140 / 200 / 300 dpi for text that has to survive being read back by a pipeline, or 400 dpi for print. The page keeps its appearance and nothing is ever enlarged. The three RAG figures were measured rather than chosen: text was rendered at each, put through the app's own resize step and read back, and 200 dpi is where 9 pt Japanese survives without a character error while 300 dpi is where 6 pt does. [`scripts/measure-legible-resolution.ps1`](scripts/measure-legible-resolution.ps1) is that measurement, for seven writing systems.
- **See what the setting costs before you save.** The same window shows a figure and a photograph side by side and puts them through the code the save runs. Resolution, quality and magnification all redraw them, and each caption reports the result in bytes and in pixels. The figure carries a line of text at 6, 8, 10 and 12 pt in the language the app is running in, because text is what breaks first and how small is too small depends on the writing system.
- **Filter by kind** — check boxes on the toolbar, or View → Shown Types — to work on one kind at a time.
- **Every save is verified.** The file is written to a temp file and checked before it becomes the final `_cleaned.pdf`: it re-opens, the page count matches, removed objects are gone from both the content streams and the page resources, and the objects you kept are still there.
- **16 UI languages**, following the Windows display language: English, Japanese, Simplified and Traditional Chinese, Korean, German, French, Spanish, Italian, Portuguese, Russian, Indonesian, Malay, Hindi, Turkish, Vietnamese. There is no in-app switch, and the manual exists in English and Japanese only.
- **Handles large documents.** A 31 MB, 176-page file with 2,015 removable objects opens in seconds. Thumbnails are cached on disk and only the ones on screen are held in memory, so opening many large PDFs costs disk rather than RAM.
- **High-DPI aware** (PerMonitorV2), verified at 200% scaling.

## What it does *not* do

- Does not preserve digital signatures — removing content invalidates any existing signature.
- Does not guarantee PDF/A conformance.
- Does not edit inside a shared Form XObject; those objects are surfaced as **unsafe to delete**.
- Does not remove *parts* of a scanned page. If the whole page is one image, deleting it removes everything visible on that page (flagged with a full-page-image warning).
- No OCR, no similar-image search, no AI-based logo classification.
- Does not execute anything a PDF asks for. JavaScript, launch and URI actions, external references and embedded files are all ignored — the app reads structure and pixels only. Files that are not really PDFs are refused before either parser sees them, and images that declare an implausible size are not decoded.

Details: [docs/known-limitations.md](docs/known-limitations.md).

## Install

Requires Windows 11 (x64 or arm64). Either way the app is self-contained — no .NET runtime installation needed.

- **Microsoft Store (recommended):** [PDF Image Remover for RAG](https://apps.microsoft.com/store/detail/9N3M42716P8K) — signed package, automatic updates.
- **Direct download:** grab the zip for your architecture from [Releases](https://github.com/Nakanokappei/pdf-image-remover-for-rag/releases) (x64 for Intel/AMD, arm64 for Windows on ARM), extract it anywhere, and run `PdfImageRemoverForRag.exe`.
  - The zip binaries are not code-signed, so Windows SmartScreen may warn on first run (More info → Run anyway). The Store version carries no such warning.
  - Only the zip version accepts PDFs dropped onto the .exe icon — the app deliberately registers no `.pdf` file association, so the Store package has no way to receive them.

## Build from source

The app is developed and built on Windows 11 with the **.NET 8 SDK**. The WinForms project needs the
Windows Desktop targets, which that SDK installs.

```powershell
dotnet build PdfImageRemoverForRag.sln -c Release   # 0 warnings
dotnet test PdfImageRemoverForRag.sln -c Release    # 270 tests (145 Core + 125 Infrastructure)
dotnet run --project scripts/GenerateSamples -c Release -- samples/   # regenerate the sample PDFs

# Self-contained binaries, one architecture at a time:
dotnet publish src/PdfImageRemoverForRag.App/PdfImageRemoverForRag.App.csproj -c Release -r win-arm64 --self-contained true -o artifacts/win-arm64
```

Three `CleanedFileNamerTests` cases assert POSIX paths and fail on Windows. That is expected, not a
broken checkout.

Where things live:

```
src/PdfImageRemoverForRag.Core/            net8.0          Models, grouping, formatting, validation, abstractions
src/PdfImageRemoverForRag.Infrastructure/  net8.0          PDFsharp / PdfPig implementations (GDI-free)
src/PdfImageRemoverForRag.App/             net8.0-windows  WinForms UI (all GDI+ drawing lives here)
tests/                                     xunit           unit + integration tests
scripts/GenerateSamples/                   Sample-PDF generator, shared with the test fixture
```

## Libraries

- **PDFsharp 6.2.4** (MIT) — image enumeration, content-stream editing, save.
- **PdfPig 0.1.15** (Apache-2.0) — thumbnail decoding and independent post-save verification.
- iText was evaluated and **rejected** — AGPL v3 is too intrusive for this distribution model.

Bundled-dependency notices: [docs/license-notices.md](docs/license-notices.md).

## License and privacy

MIT — see [LICENSE](LICENSE). Copyright (c) 2026 Nakano Kappei.
Privacy policy: [docs/privacy-policy.md](docs/privacy-policy.md), which is also the Store listing's policy URL.
