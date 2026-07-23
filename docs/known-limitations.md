# Known Limitations (PDF Image Remover for RAG)

Limitations of the current version. Read together with the README.

## PDF structure

| Limitation | Behavior |
| --- | --- |
| **Images inside Form XObjects** | Listed, but marked "not removable" and cannot be checked. Rewriting a shared Form could affect other pages, so the tool errs on the safe side. |
| **Inline images (`BI`…`EI`)** | Not handled. Only Image XObjects are processed. |
| **Logos, rules, or text drawn as vector paths** | Handled as *shapes*, not as images or text. |
| **Full-page images in scanned PDFs** | Removable, but flagged with a warning. Deleting one removes everything visible on that page. Partial removal inside an image (e.g. erasing just a logo) is not supported. |
| **Encrypted PDFs** | Cannot be opened — there is no password prompt. An error dialog explains this. |
| **JPEG (`/DCTDecode`) thumbnails** | Supported. PdfPig's `TryGetPng` always returns false for JPEG (a documented limitation), so the raw JPEG bytes are passed through for display. |
| **JPEG 2000 (`/JPXDecode`), CCITT, and JBIG2 thumbnails** | Listing and removal work, but no thumbnail is produced (a placeholder icon is shown). |

## Saved PDFs

| Limitation | Description |
| --- | --- |
| **Digital signatures** | Removing content invalidates any existing signature. Signed PDFs are not supported in any guaranteed way. |
| **PDF/A conformance** | Not guaranteed to survive editing. |
| **How draw calls are removed** | Only the target image's `Do` operator is removed; the surrounding `q`/`cm`/`Q` operators remain as harmless no-ops. The image object itself may remain in `/Resources`, but with zero usages it no longer appears when the file is re-analyzed. |
| **Visual confirmation** | The automatic post-save verification is basic (the file opens, page count matches, removed objects are gone, retained objects remain). Final visual confirmation is left to the user. |

## Text removal

| Limitation | Description |
| --- | --- |
| **Scope** | Strings drawn by `Tj`/`TJ`/`'`/`"` directly in the page content stream, **2+ characters** long and shown **2+ times within one file** (repeated noise: headers, footers, watermarks). |
| **How "2+ characters" is measured** | On the **actual decoded characters**, using the font's `/ToUnicode` CMap. Identity-H (CID) text such as Japanese decodes correctly, so the threshold behaves as expected. Fonts without a ToUnicode map are matched on raw values (WinAnsi and similar are readable as-is). |
| **Garbled text** | Composite fonts (Type0 / Identity-H) store 2-byte code sequences; these are decoded via the `/ToUnicode` CMap, so the list, thumbnails, and removal keys all show and match real characters. Fonts with no ToUnicode CMap cannot be decoded and are handled on raw values (matching and removal still work, but the display may be garbled). |
| **Text inside Form XObjects** | Not detected and not removed (shared Forms are never rewritten — the same safety policy as for images). |
| **Same-value collisions** | Two different text runs showing the same string in different fonts may both be removed (rare). |
| **Layout** | Only the text-showing operators are removed; positioning operators (`Td`/`Tm`/`Tf`) remain as harmless no-ops. PDFs that depend heavily on relative positioning could see subsequent text shift. |
| **Post-save verification** | For text, the automatic check is basic (the saved PDF opens and the page count matches). Strict "is the text gone" verification is covered by the unit tests. |

## Shape (vector) removal

| Limitation | Description |
| --- | --- |
| **Scope** | Vector shapes (lines, rectangles, curves) drawn by path-construction operators (`m`/`l`/`c`/`v`/`y`/`re`/`h`) plus a painting operator (`S`/`s`/`f`/`F`/`f*`/`B`/`b`/`n`, …) directly in the page content stream. **No occurrence-count filter** — like images, every drawn shape is listed and the user picks what to remove. Repeats of the same shape collapse into one group with a summed usage count. |
| **Identity** | **Shape + line width + color** (**position is ignored**). Path points are CTM-mapped, then translated so the bounding box starts at the origin; the painting operator, line width (`w`), and stroke/fill color (`RG`/`rg`/`G`/`g`/`K`/`k`) complete the signature. The same shape at different positions is one group; a different width or color splits it. |
| **Clipping paths** | Paths that also set a clip (`W`/`W*`) are never removed (removing them could reshape unrelated clipped content). |
| **Shapes inside Form XObjects** | Not detected and not removed (shared Forms are never rewritten). |
| **Thumbnails** | The actual path is rendered in its actual color (scaled to the bounding box, stroke/fill reproduced). CMYK and grayscale colors are converted to RGB. Shapes brighter than light gray are drawn on a black background so they stay visible. |
| **Side effects** | Only the path-construction-through-painting operators are removed; preceding state settings (`w`, `rg`, `RG`, …) remain. Harmless when nothing follows, but a leftover state setting could in rare cases affect later drawing. |
| **Granularity** | "One shape" = one path paint (construction through painting operator). A logo composed of several paths appears as several objects. |

## Features that are out of scope

- Whole-page preview / after-removal preview
- Removing only specific pages or specific occurrences (removal is per group, all occurrences)
- Batch processing of multiple PDFs from the command line
- OCR, AI-based logo classification, similar-image search
- Settings screen, dark mode, auto-update, installer
- Command-line mode

## Platform

- Runs on Windows 11 (x64 / ARM64). On macOS the solution builds, but the app cannot run.
- Verified on real Windows 11 ARM64 hardware.
- Large PDFs are measured: a 31 MB, 176-page file with 2,015 removable objects
  works at practical speed in both the table and tile views. The x64 binary has
  been published but not yet launch-tested on real hardware.

## Languages

- 16 UI languages (English, Japanese, Simplified Chinese, Traditional Chinese,
  Korean, German, French, Spanish, Italian, Portuguese, Russian, Indonesian,
  Malay, Hindi, Turkish, Vietnamese), following the OS display language.
  **There is no in-app language switch.** Unsupported languages fall back to English.
- **Arabic is not supported.** Right-to-left support would require not just the
  forms' `RightToLeft` settings but mirroring all custom-painted parts (table
  headers, tiles, toolbar icons) — a layout project, not a translation.
- **The manual and Store listing exist in English and Japanese only.** In every
  other language, Help → Online Manual opens the English page and the product
  name stays in English.
- Translations have not been reviewed by native speakers.

## Accessibility

What is covered: full keyboard-only operation, spoken names for the icon-only
buttons and the checkbox column, and contrast. The table view (a standard
Windows `DataGridView`) is fully accessible to every assistive technology,
including Narrator.

- **The tile view is not readable by Narrator** (the Windows built-in screen
  reader). The tiles are painted onto a single control, and exposing each tile
  to UI Automation would require implementing UIA fragment providers — but in
  .NET 8 WinForms the UIA fragment APIs (`FragmentNavigate`, `RuntimeId`,
  `Control.SupportsUiaProviders`, …) are all `internal` and cannot be
  implemented from an external assembly (verified via reflection).
  - **NVDA and JAWS (MSAA-based screen readers) can read the tiles** — a custom
    `AccessibleObject` exposes a List with one CheckButton per tile to the MSAA
    tree, verified on real hardware.
  - **The keyboard operates the tiles fully** (Tab to enter, arrows to move,
    Space to toggle, visible focus rectangle).
  - **Narrator users have a fully accessible alternative: the table view**
    (View → Table). Both views show the same content in the same order, with
    no difference in capability.
  - Making the tiles Narrator-readable would require rebuilding the view as an
    owner-drawn virtual-mode ListView (the only supported route, since the
    framework then provides UIA) — a medium-sized rewrite, kept as a separate
    task for if and when Narrator support becomes a requirement.

## Security posture

- Nothing a PDF asks for is ever executed (JavaScript, Launch/URI actions,
  external references, embedded files). The app reads structure and pixels only.
- Files are checked for a PDF signature at the door, and images declaring
  implausible dimensions are not decoded. What these gates cannot prevent are
  **bugs in PDFsharp, PdfPig, or GDI+ themselves** on input that passes them.
- Password-protected PDFs are not supported (reported as an error).
