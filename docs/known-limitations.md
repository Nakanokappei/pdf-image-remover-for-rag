# Known Limitations (PDF Image Remover for RAG)

Limitations of the current version. Read together with the README.

## PDF structure

| Limitation | Behavior |
| --- | --- |
| **Images inside Form XObjects** | Listed, but marked "not removable" and cannot be checked. Rewriting a shared Form could affect other pages, so the tool errs on the safe side. |
| **Inline images (`BI`…`EI`)** | Not handled. Only Image XObjects are processed. |
| **Logos, rules, or text drawn as vector paths** | Handled as *shapes*, not as images or text — when the page draws them itself. Paths painted inside a Form XObject are a *drawing* instead (see below). |
| **Full-page images in scanned PDFs** | Removable, but flagged with a warning. Deleting one removes everything visible on that page. Partial removal inside an image (e.g. erasing just a logo) is not supported. |
| **Encrypted PDFs** | Cannot be opened — there is no password prompt. An error dialog explains this. |
| **JPEG (`/DCTDecode`) thumbnails** | Supported. PdfPig's `TryGetPng` always returns false for JPEG (a documented limitation), so the raw JPEG bytes are passed through for display. |
| **JPEG 2000 (`/JPXDecode`), CCITT, and JBIG2 thumbnails** | Listing and removal work, but no thumbnail is produced (a placeholder icon is shown). |

## Saved PDFs

| Limitation | Description |
| --- | --- |
| **Digital signatures** | Removing content invalidates any existing signature. Signed PDFs are not supported in any guaranteed way. |
| **PDF/A conformance** | Not guaranteed to survive editing. |
| **How draw calls are removed** | The target image's `Do` operator is removed and the surrounding `q`/`cm`/`Q` operators remain as harmless no-ops. The image itself is then taken out of every page's `/XObject` resources and deleted from the document, together with its `/SMask` or `/Mask` child. Removing the draw call alone was not enough: it stops the image being *painted*, while a reader that enumerates objects instead of rendering pages — which is what a RAG ingestion pipeline does — still found every "removed" image in the file. |
| **Images something else also uses** | The object is deleted only once nothing in the document still points at it. A page is not the only thing that can: annotation appearance streams, tiling patterns, soft-mask groups in an `ExtGState` and Type3 glyph procedures all carry resources of their own, and analysis reads none of them. Such an image is still listed and still removable — its pages stop drawing and listing it — but the object stays, because deleting it would leave a reference pointing at nothing. Rare, and recorded in the log as `imagesKeptBack` on the `cleaned:` line so the case is not silent. |
| **Soft masks of images you keep** | A `/SMask` is an image's alpha channel and belongs to its parent, so it is neither listed as a removable object nor removable on its own — deleting it would strip the transparency from an image you chose to keep. Object-enumerating readers may still surface such masks as spurious images, typically near-black rectangles matching their parents' pixel dimensions. Remove the parent image and its mask goes with it. |
| **Visual confirmation** | The automatic post-save verification is basic (the file opens, page count matches, removed images are absent from both the content streams and the page resources, retained objects remain). Final visual confirmation is left to the user. |

### Where the black rectangles come from

Almost all of them are shadows, which the tool now lists as a kind of their own
(**Shadow**). Filter to that kind, select all, save, and they are gone. What
follows is why they exist, and what is left over that the kind does not cover.

PDF cannot draw a blur, so a producer exporting a drop shadow has to rasterize
it: a picture holding **one flat color**, plus a soft mask holding the blurred
outline. Rendering the page composites the two and you see a shadow. A reader
that enumerates objects rather than rendering pages sees two images, and either
one arrives as a rectangle:

| What such a reader writes out | Why it is dark |
| --- | --- |
| **The picture** | It is one flat color, usually pure black. There is nothing else in it to draw |
| **The mask** | A `/SMask` is one 8-bit sample per pixel, 0 transparent to 255 opaque. Written out as gray levels, **transparency becomes darkness**, so a mostly-transparent mask is a mostly black rectangle |

Both are removed together, because a mask goes with its parent. Measured on the
document this was worked out from: 39 shadows, 50 objects, and the customer
reported 3 black rectangles on a page that holds exactly 3 of them.

Only shadows are exported this way. A glow, soft edges or a reflection is
rasterized **together with the object it belongs to**, so the resulting image
holds real pixels, is listed as an ordinary Image, and extracts as a picture
rather than a rectangle. That was checked by exporting one slide per effect.

**What the Shadow kind does not cover.** An ordinary image you keep may still
have a mask, and a reader that writes masks out will surface that mask as a
near-black rectangle of the parent's dimensions. The mask is not listed on its
own and cannot be removed on its own — deleting it would strip the transparency
from an image you chose to keep. Removing the parent takes it with it, but
that removes the artwork too, so weigh it per page.

## Text removal

| Limitation | Description |
| --- | --- |
| **Scope** | Strings drawn by `Tj`/`TJ`/`'`/`"` directly in the page content stream, holding **at least one visible character** and shown **2+ times within one file** (repeated noise: headers, footers, watermarks). One character is enough, so a single-letter confidentiality marking in the corner of every page can be removed. |
| **Whitespace** | Whitespace and control characters do not count toward that one character, so a string of spaces is never listed. Such a row would show nothing, and removing it would join the words on either side. |
| **How the characters are counted** | On the **actual decoded characters**, using the font's `/ToUnicode` CMap. Identity-H (CID) text such as Japanese decodes correctly, so the threshold behaves as expected. Fonts without a ToUnicode map are matched on raw values (WinAnsi and similar are readable as-is). |
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
| **Shapes inside Form XObjects** | Not listed as shapes. They are listed as one *drawing* instead — see the next section. |
| **Thumbnails** | The actual path is rendered in its actual color (scaled to the bounding box, stroke/fill reproduced). CMYK and grayscale colors are converted to RGB. Shapes brighter than light gray are drawn on a black background so they stay visible. |
| **Side effects** | Only the path-construction-through-painting operators are removed; preceding state settings (`w`, `rg`, `RG`, …) remain. Harmless when nothing follows, but a leftover state setting could in rare cases affect later drawing. |
| **Granularity** | "One shape" = one path paint (construction through painting operator). A logo composed of several paths appears as several objects. |

## Drawing removal

| Limitation | Description |
| --- | --- |
| **Scope** | The vector artwork a Form XObject paints in its own content stream. Listed as **one object per form**, however many paths it holds, because the stream is shared by every page that draws the form. **No occurrence-count filter**, like images and shapes. |
| **Identity** | The form's **stream hash**, the same identity images use. One form placed on eleven pages is one object with eleven placements; two forms holding identical bytes are one object. |
| **Granularity** | A drawing cannot be taken apart. Removing one leaves nothing of it on that page, so a silhouette and the speech bubble beside it go together if one form paints both. |
| **How it is removed** | The page's own `Do` call for the form is deleted, along with the form's entry in that page's `/XObject` resources. The form's content stream is **never rewritten** — that is what keeps other pages intact. The form object itself is deleted once nothing in the document still points at it. |
| **Text inside a form** | Not detected, as a drawing or as text. Only paths are read. |
| **Images inside a form** | Still handled as images (listed, marked not removable), not as part of the drawing. |
| **Thumbnails** | Every path is drawn through one mapping of the drawing's bounding box, so the parts keep the arrangement they have on the page. Each part keeps its own fill or stroke and its own color. |
| **Flattening** | Drawings never join an overlap region. A caption sitting on a form-painted figure is not offered for flattening. |

## Shadow removal

| Limitation | Description |
| --- | --- |
| **Scope** | An Image XObject whose samples are **one flat color** and which carries a soft mask. That is what a producer has to write when it exports a drop shadow, because PDF has no blur operator. **No occurrence-count filter**, like images. |
| **What is not a shadow** | A picture that merely has a mask (it has a picture in it), and a flat color with no mask (a filled rectangle the page shows as itself). Glow, soft edges and reflection are rasterized together with their object, so they are pictures too. |
| **Encodings** | Only samples that can be read plainly are judged — 8 bits per component, DeviceRGB or DeviceGray, unencoded or Flate. A JPEG is never called a shadow; producers generate shadows rather than photograph them, so this has not been seen to matter. |
| **A black silhouette** | An icon drawn as one flat color through a mask is indistinguishable from a shadow by these measurements, and is listed as one. It extracts as a black rectangle downstream just the same, so the classification matches the symptom even where it does not match the intent. |
| **How it is removed** | Exactly as an image: the `Do` call goes, the resource entry goes, and the object and its mask go once nothing still points at them. |

## Flattening overlaps into an image

| Limitation | Description |
| --- | --- |
| **Scope** | Places on one page where objects of **two or more different kinds** overlap: image + text, image + text + shape, image + shape, text + shape. **Text over text is excluded** — rasterizing it would turn words into a picture and gain nothing. "Overlap" covers containment and partial overlap alike. |
| **Connection rule** | A shape that **only strokes** its path joins a region solely when it lies **entirely inside** the other object; a **filled** shape joins as soon as it touches. Without this split, a page border — which crosses every paragraph on the page — pulled 77% × 81% of the paper into a single region. |
| **Page backgrounds** | A **shape** covering 90% or more of the page in both dimensions is treated as the page itself, joins nothing, and is never part of a region. A slide background is such a shape and it is filled, so without this it touched every object on the page: one deck reported a single 118-object "region" per page, which was the page. **Images are not affected** — a scan or a full-bleed photograph with text over it is what flattening is for. |
| **Objects inside Form XObjects** | Never part of a region (shared Forms are never rewritten — the same policy as removal). |
| **Granularity** | The rasterized area is the bounding box of the objects the user **checked**, and only those **instances** are deleted. The same string shown elsewhere in the file survives, which is the whole difference between this and removal. |
| **Rendering** | 200 dpi through the operating system's PDF renderer (`Windows.Data.Pdf`), capped at 4000 px on the long side. The result is a raster image, so text inside a flattened area can no longer be selected, copied, or searched — that is the purpose, not a side effect. File size grows accordingly. |
| **Failure handling** | A region that cannot be rendered, or where nothing matched at the place analysis found it, is left **exactly as it was** and reported as not flattened. Deleting the objects and then failing to draw their replacement would punch a hole in the page. |
| **Rotated pages** | Supported. A `/Rotate` entry turns the page for a viewer but not for its content stream, so finding objects and rewriting them are unaffected. The operating system's renderer *does* turn the page, so the area to rasterize is mapped into its space and the rendering turned back — checked against that renderer at every rotation. Before this was done, flattening a quarter-turned page rasterized the wrong part of the paper. |
| **Whole-page regions** | Warned about, not prevented. When what you have selected covers 90% or more of the page in both dimensions, the panel says so in red: flattening it turns that page into a single image and none of its text stays text. It remains a legitimate thing to ask for — a scan with a caption typed over it is exactly that — so the warning is a warning and the check box still works. |
| **Post-save verification** | The saved file is verified to re-open with a matching page count, and flattened images are deliberately **not** claimed as removed (their bytes are still in the document). That a flattened area has actually left the text layer is covered by the unit tests, not by the automatic check. |

## Features that are out of scope

- After-removal preview (the Flatten panel previews the page **as it is**, not as it would be)
- Removing only specific pages or specific occurrences **from the object list** (removal there is per group, all occurrences; the Flatten side is per instance by design)
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

- **The Flatten panel's layers list has the same ceiling, for the same reason.**
  Its rows are painted onto one control, so it carries the same custom
  `AccessibleObject`: a List with one CheckButton per row, each named after the
  unit or object it stands for, and reporting checked, **mixed** (a unit with
  only some of its objects checked) and expanded / collapsed.
  - **NVDA and JAWS can read and operate it**, verified on real hardware through
    the MSAA tree: taking focus and invoking a row's default action move the
    cursor and check the row, and a partly checked unit reports `STATE_SYSTEM_MIXED`.
  - **The keyboard operates it fully** — Tab to enter, up/down/Home/End/PageUp/
    PageDown to move, Space to check, left/right to fold a unit (left from an
    object row goes up to its unit), with a visible focus rectangle.
  - **Narrator has no equivalent alternative here**, unlike the tile view: the
    Flatten panel is the only way to reach flattening. Removal — what the app is
    named for — remains fully accessible through the table.

## Security posture

- Nothing a PDF asks for is ever executed (JavaScript, Launch/URI actions,
  external references, embedded files). The app reads structure and pixels only.
- Files are checked for a PDF signature at the door, and images declaring
  implausible dimensions are not decoded. What these gates cannot prevent are
  **bugs in PDFsharp, PdfPig, or GDI+ themselves** on input that passes them.
- Password-protected PDFs are not supported (reported as an error).
