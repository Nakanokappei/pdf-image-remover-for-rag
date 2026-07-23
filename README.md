# PDF Image Remover for RAG

A Windows 11 desktop tool that removes unnecessary images, repeated text, and vector shapes from PDFs before you feed them into a RAG pipeline.

Company logos, headers, footers, watermarks, and page rules degrade retrieval quality and inflate preprocessing cost once a PDF lands in a RAG / Dify pipeline. This tool lists every removable object in your PDFs, lets you check what you want gone, and writes new PDFs with those objects' draw calls removed. **The original files are never overwritten.**

Everything runs locally — files never leave your PC, and no data is collected.

**Manual: [English](docs/manual.en.md) · [Japanese](docs/manual.ja.md)** — also reachable from the app's Help menu.

## What it removes

| Kind | What is listed | Notes |
| --- | --- | --- |
| **Images** | Every drawn Image XObject | The same logo on 50 pages — and across every open file — is one row |
| **Text** | Strings of 2+ characters shown 2+ times in a file | Headers, footers, watermarks. CJK/composite fonts are decoded via `/ToUnicode` |
| **Shapes** | Every drawn line, rectangle, and curve | Identity is shape + line width + color; position is ignored |

## Features

- **Open several PDFs at once.** Identical objects are merged into one row across files — one checkbox removes a shared logo from every file.
- **Two views.** A spreadsheet-style table (sortable on any column, resizable columns) and a thumbnail tile view, always in the same order.
- **Thumbnails for everything.** Images are decoded, text is drawn as text, shapes are rendered from their actual path in their actual color.
- **Filter by kind** (View → Shown Types) to work on images, text, or shapes alone.
- **Safety first.** Saves go through a temp file that is verified (re-opens, page count matches, removed objects gone, kept objects present) before it becomes the final `_cleaned.pdf`. Objects inside a shared Form XObject are marked unremovable; full-page (scanned) images are flagged with a warning.
- **16 UI languages**, following the OS display language: English, Japanese, Simplified Chinese, Traditional Chinese, Korean, German, French, Spanish, Italian, Portuguese, Russian, Indonesian, Malay, Hindi, Turkish, Vietnamese. There is no in-app language switch — it follows Windows. The manual exists in English and Japanese only; every other language opens the English page.
- **Handles large documents.** A 31 MB, 176-page file with 2,015 removable objects opens in seconds. Thumbnails are cached on disk and only the ones on screen are held in memory, so opening many large PDFs costs disk rather than RAM.
- **High-DPI aware** (PerMonitorV2), verified at 200 % scaling.

## What it does *not* do

- Does not preserve digital signatures — removing content invalidates any existing signature.
- Does not guarantee PDF/A conformance.
- Does not edit inside a shared Form XObject; those objects are surfaced as **unsafe to delete**.
- Does not remove *parts* of a scanned page. If the whole page is one image, deleting it removes everything visible on that page (flagged with a full-page-image warning).
- No OCR, no similar-image search, no AI-based logo classification.
- Does not execute anything a PDF asks for. JavaScript, launch and URI actions, external references and embedded files are all ignored — the app reads structure and pixels only. Files that are not really PDFs are refused before either parser sees them, and images that declare an implausible size are not decoded.

Details: [docs/known-limitations.md](docs/known-limitations.md).

## Requirements

Windows 11 (x64 or arm64). The published build is self-contained — no .NET runtime installation needed.

## Build from source

The app is developed on macOS (Apple Silicon) and cross-published for Windows. Building the WinForms
project from macOS requires the **official Microsoft .NET 8 SDK** — the Homebrew source-build lacks
the Windows Desktop targets.

```bash
export DOTNET_ROOT="$HOME/.dotnet"
DOTNET="$HOME/.dotnet/dotnet"

"$DOTNET" build PdfImageRemoverForRag.sln -c Release   # build (0 warnings)
"$DOTNET" test  PdfImageRemoverForRag.sln -c Release   # 106 tests (71 Core + 35 Infrastructure)
"$DOTNET" run --project scripts/GenerateSamples -c Release -- samples/   # regenerate sample PDFs

# Windows binaries from macOS:
"$DOTNET" publish src/PdfImageRemoverForRag.App/PdfImageRemoverForRag.App.csproj \
  -c Release -r win-arm64 --self-contained true -o artifacts/win-arm64
```

The app runs on Windows only — never try to launch it on macOS.

## Repository layout

```
src/PdfImageRemoverForRag.Core/            net8.0          Models, grouping, formatting, validation, abstractions
src/PdfImageRemoverForRag.Infrastructure/  net8.0          PDFsharp / PdfPig implementations (GDI-free)
src/PdfImageRemoverForRag.App/             net8.0-windows  WinForms UI (all GDI+ drawing lives here)
tests/                                     xunit           71 unit + 35 integration tests
scripts/GenerateSamples/                   Sample-PDF generator (shared with the test fixture)
scripts/PdfImageRemoverForRag.Poc/         Technical-verification driver over Infrastructure
```

## Libraries

- **PDFsharp 6.2.4** (MIT) — image enumeration, content-stream editing, save.
- **PdfPig 0.1.15** (Apache-2.0) — thumbnail decoding and independent post-save verification.
- iText was evaluated and **rejected** — AGPL v3 is too intrusive for this distribution model.

Bundled-dependency notices: [docs/license-notices.md](docs/license-notices.md).

## Documentation

- [docs/manual.en.md](docs/manual.en.md) / [docs/manual.ja.md](docs/manual.ja.md) — user manual (also in the app's Help menu).
- [docs/known-limitations.md](docs/known-limitations.md) — what this version does not handle.
- [docs/license-notices.md](docs/license-notices.md) — bundled-dependency notices.
- [docs/privacy-policy.md](docs/privacy-policy.md) — privacy policy (also the Store listing's policy URL).

## License

MIT — see [LICENSE](LICENSE). Copyright (c) 2026 Nakano Kappei.
