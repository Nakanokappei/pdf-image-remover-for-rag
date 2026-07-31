# PDF Image Remover for RAG — Online Manual

How to use **PDF Image Remover for RAG**. Japanese version: [manual.ja.md](manual.ja.md)

---

## 1. What this app does

Before you feed a PDF into a RAG (retrieval-augmented generation) pipeline, this app **strips the objects that get in the way of retrieval.**

Company logos, headers, footers, watermarks, and ruling lines are ingested along with your body text — they hurt retrieval quality and inflate preprocessing cost. This app lists the objects inside your PDFs and saves **new PDFs** with the ones you check removed.

- **Your original PDFs are never modified.** Output always goes to a separate file.
- **Everything runs locally on your PC.** No file leaves the machine, and no data is collected.

### Three kinds of removable objects

| Type | What gets listed | Notes |
| --- | --- | --- |
| **Image** | Every drawn image | The same logo on 50 pages collapses into one row |
| **Text** | Strings of **2+ characters shown 2+ times** in a file | For headers, footers, watermarks. CJK / double-byte text is decoded correctly |
| **Shape** | Every drawn line, rectangle, and curve | Same **shape + line width + color** = one row (position is ignored) |

Only **Text** has an occurrence filter. Images and shapes are listed in full.

### Two ways to go about it

The window is split left and right, and **both halves are visible at once.**

| | Delete | Flatten |
| --- | --- | --- |
| Where | The **left** of the window (the object list) | The **right** of the window (the Flatten panel) |
| What it does | Takes the objects you pick out of the file | Bakes overlapping objects into a single image |
| How the page looks | That much less on it | **Unchanged** |
| Good for | Logos, headers, watermarks, ruling lines | A chart's axis labels, a caption over a photo — text that **breaks the picture if you delete it, but gets in the way of retrieval if you keep it** |

Flattening is covered in section 5.

---

## 2. Install and launch

Requires Windows 11 (x64 or arm64). The distributed build is self-contained — no .NET installation needed.

1. Extract the distributed ZIP.
2. Double-click `PdfImageRemoverForRag.exe`.

### UI language

The UI language follows your **OS display language**. Sixteen languages are supported:

English / 日本語 / 简体中文 / 繁體中文 / 한국어 / Deutsch / Français / Español / Italiano / Português / Русский / Bahasa Indonesia / Bahasa Melayu / हिन्दी / Türkçe / Tiếng Việt

- Any other language falls back to English.
- **There is no language switch inside the app.** It follows Windows.
- Right-to-left languages such as Arabic are not supported (the whole screen would have to be mirrored).
- **Only Japanese and English have a manual.** In every other language, **Help → Online Manual** opens the English page.

The window position and size are remembered on exit and restored next time (falling back to the default size if your display arrangement has changed).

---

## 3. Basic workflow

### Steps

1. **Open** — click **Open PDF** on the toolbar, or choose **File → Open…**.
   You can **select several files at once** in the dialog. Dragging files onto the window works too,
   as does dropping PDFs onto the app's icon to launch it (zip build only).

   ![The Open PDF dialog with several files selected at once](images/open-dialog-en.png)
2. **Review** — once analysis finishes, the removable objects are listed, sorted by **usage count, descending**. Frequently drawn things like logos and headers come first.

   Thumbnails are produced afterwards, only for what is on screen, so **opening takes about as long as the analysis itself**. Documents with thousands of objects list fine. If analysis takes a while, a progress dialog appears and you can stop it (stopping discards every file in that Open action).
3. **Select** — click the **☑ column** on the rows you want removed. From the keyboard, land on the row and press **Space**.
4. **Save** — click **Remove & Save** on the toolbar, or choose **File → Remove Selected & Save…**.
   - **One** affected file: a save dialog asks for the file name.
   - **Several** affected files: pick a folder; each file is saved as `<name>_cleaned.pdf`.

   ![Choosing the destination folder, with objects marked for removal](images/save-dialog-en.png)
5. **Done** — the status bar reports how many files were saved and how many draw calls were removed. The removed objects disappear from the list.

### Opening replaces your current work

Opening files while others are already open **replaces** the workspace — it does not append. If you have objects checked, a dialog asks whether to save or discard first.

To work on several files together, **select them all in one Open action.**

---

## 4. Reading the screen

### How the window is laid out

Below the toolbar the window is split left and right.

- **Left — the object list.** The main thing this app does. It switches between a table and tiles, and the removal ticks go here.
- **Right — the Flatten panel.** It shows where the object on the **currently selected row** overlaps something else. Drag the divider to change its width; that width survives resizing the window.

- **The toolbar's Select All and Clear Selection act on the object list only.** Flattening is chosen a unit at a time in the panel — there is no one-click "flatten every overlap in the document", because that would make the target of an irreversible operation impossible to predict. The panel has its own **Clear Selection** at its top right for dropping the flatten ticks.
- **Remove & Save is available as soon as either side has something ticked.** One save does both.
- The status bar reports **both** counts, e.g. `3 object(s) selected for removal / 4 object(s) to flatten`.

### Toolbar

| Button | What it does |
| --- | --- |
| Open PDF | Choose and open PDFs (multi-select allowed) |
| Remove & Save | Save PDFs with the checked objects removed and the checked overlaps flattened |
| Select All | Check every visible object in the object list |
| Clear Selection | Uncheck everything in the object list |

### Table columns

![The table view — one row per removable object](images/table-en.png)

| Column | Content |
| --- | --- |
| Row number | Leftmost header column; renumbered from the top on every rebuild |
| ☑ | Marked for removal |
| Thumbnail | Images render as a thumbnail, text as the actual string, shapes as their real path and color |
| Object ID | `IMG_001` (image) / `TXT_001` (text) / `SHP_001` (shape) |
| Type | Image / Text / Shape |
| Size | Pixel dimensions for images, character count for text, bounding box in pt for shapes |
| Usage | How many times the object is drawn (summed across all open files) |
| Compression | Image compression method; `N/A` for non-images |
| Est. Size | Estimated bytes saved by removing it |
| Warning | "Not removable" / "Full page?" (→ section 7) |

### Working with the table

- **Sort** — click any column header. Clicking again toggles ascending / descending (∧ = ascending, ∨ = descending).
- **Resize columns** — drag a column divider. **Double-click** a divider to auto-fit the column to its left.
- **Bulk check** — click one row's ☑, then **Shift+click** another row's ☑ to check/uncheck everything in between.
- The ☑ cell toggles wherever you click inside it — you don't have to hit the checkbox precisely.
- **The keyboard alone is enough** — **Space** toggles the current row's ☑, and **Shift+Space** extends from the row you last toggled. It works whichever column the cursor is in.
- **Thumbnails are built for the rows you are looking at** — pause scrolling for about half a second and the pictures for the rows on screen appear. Until then the thumbnail cell is blank. A cell that stays blank holds an image format the app cannot decode (JPEG 2000, CCITT, JBIG2); a placeholder icon is shown instead.

### Tile view

**View → Tiles** switches to large thumbnails. The order always matches the table.

![The tile view; the badge in the corner is the usage count](images/tiles-en.png)

- Clicking a tile **presses it in**, meaning it is marked for removal.
- The badge in the top-right corner is the usage count.
- Dimmed, unclickable tiles are "Not removable" objects.
- A tile whose picture is not ready yet says **"Building thumbnail…"** in words. As in the table, pausing your scrolling for about half a second fills in what is on screen.

**View → Table** returns to the table.

### Filtering by kind

**View → Shown Types** toggles Images / Shapes / Text. Handy when you want to clear out all shapes at once.

At least one kind must stay visible, so the last remaining check cannot be turned off. **Select All** applies only to the kinds currently shown.

---

## 5. Flattening an overlap into an image (the Flatten panel)

### What it is for

A chart's axis labels, a caption sitting on a photograph, a stamp over a ruling line — this kind of text **breaks the picture if you delete it, and gets in the way of retrieval if you keep it.**

Flattening bakes the place where they overlap into **a single image of exactly what was there.** The page looks the same. What changes is underneath: **the text in that place stops being text.** A RAG pipeline reads the text layer, so that is where it disappears from.

Three things separate it from deleting:

| | Delete | Flatten |
| --- | --- | --- |
| What goes | The objects you pick, **from every file** | The objects you pick, **from that one place** (the same string on other pages survives) |
| How the page looks | That much less on it | Unchanged |
| Number of images | Fewer | The same, or one more |

> **Deleting first makes flattening impossible** — the material to bake is gone. Within a single save this is handled for you: **flattening runs before removal**, so ticking things on both sides and saving once is fine.

### What counts as an overlap

Only places where objects of *different* kinds overlap are listed. That gives four combinations:

**image + text / image + text + shape / image + shape / text + shape**

- **Text over text is excluded.** Baking it would turn words into a picture and gain nothing.
- "Overlap" covers both containment and partial overlap.
- **A shape that only strokes its path** (a frame, a rule, an outline) joins in only when it lies **entirely inside** the other object. A border around the page crosses every paragraph on it, but that is furniture, not an overlap. A filled shape — a shaded heading band, say — joins as soon as it touches.
- **A shape covering the whole page** — a slide background, say — counts as the page itself and joins nothing. It is filled, so it would otherwise touch every object on the page and make one unit out of the whole sheet. **Images are not affected**: text over a scan or a full-bleed photograph still flattens.
- Contents of shared drawing components (Form XObjects) are excluded, for the same reason they are "Not removable" in the object list.

### How to use it

![The Flatten panel; ticking a unit ticks the objects under it, and the preview below shows the area that becomes a picture](images/flatten-en.png)

1. **Select the row of the object you want to look at, in the list on the left.** The Flatten panel then shows **only the units that object takes part in**.
   - An object that overlaps nothing gets "This object does not overlap anything." Most objects in a document are like that.
   - With no row selected the panel is empty.
2. The panel is laid out **like an image editor's layers panel**: a **unit** is a layer group and the **objects** inside it are its layers, each with a thumbnail, a name and a checkbox. A unit's heading names the file and page it is on, and the triangle at its left folds it away.
3. **Ticking a unit takes everything under it**; you can also tick objects individually. A unit with only some of its objects ticked shows a **dash in its box** — the in-between state.
4. **Only what you tick is baked in**, and the area that becomes an image is **the bounding box of what you ticked**. Tick just the text, for example, and the image underneath keeps being drawn with the new picture over it.
5. The preview underneath shows where you are: **that place keeps its colour and everything else is dimmed.** Select a unit's heading for all its members, or an object's row for just that one.
6. **Saving is the same as for deleting** (**Remove & Save** on the toolbar, or **File → Remove Selected & Save…**). A save with nothing but flattening ticked works fine.

> **Ticks survive moving to another row.** The panel only shows the units for the object you have selected, but ticks made on other objects are still there — the status bar's count is the total. To drop them all, use **Clear Selection at the top right of the panel**; the toolbar's Clear Selection acts on the object list only.

After a save the panel loses only its ticks. What it describes is the source PDF, and the source PDF has not changed. (The object list does the opposite: removed rows leave the list.)

### Worth knowing

- **Text in a flattened area can no longer be selected or copied.** That is the point of the feature. Your original PDF is untouched, so you can always open it again.
- Flattened areas are rendered at 200 dpi, which makes the file larger.
- **A place that cannot be rendered is left exactly as it was.** Deleting the objects and then failing to draw their replacement would punch a hole in the page.
- **When what you have ticked covers nearly the whole page, the panel says so in red.** Flattening it turns that whole page into one image, and none of its text stays text. It is not forbidden — a scan with a caption typed over it is exactly that case, done knowingly.

---

## 6. Processing several PDFs at once

When you open several PDFs, **identical objects across files collapse into a single row** (matched by content hash).

If the same logo appears in five files, you see one row — check it once and it is removed from **all five files**. The usage count is the total across files.

Saving produces one `_cleaned.pdf` per affected file. If a name already exists in the destination folder, a ` (2)` suffix is added.

---

## 7. What the warnings mean

### Not removable

The checkbox is grayed out and cannot be clicked. The object lives **inside a shared drawing component (Form XObject)**, and removing it could break rendering elsewhere — so the app errs on the safe side and blocks it.

### Full page?

The object may be an image that **covers the whole page** — typical of scanned PDFs.

**Removing such a row erases everything visible on that page**, body content included. You cannot keep the text of a scanned page while removing its image. Inspect carefully before checking it.

---

## 8. How saving stays safe

Every save runs this sequence, and **only a verified result becomes the final file:**

1. Write to a temporary file (`.part`).
2. Verify what was written:
   - Does it re-open correctly?
   - Does the page count match the original?
   - Are the removed objects really gone?
   - Are the objects you kept still present?
3. If everything checks out, rename it to the final name. If anything fails, the temporary file is deleted and nothing is written.

The app never writes into your source PDF. Choosing the source path as the destination is rejected with an error.

---

## 9. What it does not do

- **Digital signatures are not preserved.** Changing content invalidates any existing signature.
- **PDF/A conformance is not guaranteed.**
- **Shared components (Form XObjects) are not edited.** Their contents show up as "Not removable".
- **Parts of a scanned page cannot be removed** — the whole page is a single image.
- **No OCR, no similar-image search, no AI logo classification.**
- Text in fonts without a `/ToUnicode` map may display incorrectly.

Details: [known-limitations.md](known-limitations.md)

---

## 10. Troubleshooting

| Symptom | What to do |
| --- | --- |
| "Could not open the PDF" | The file may be password-protected or corrupt. Use **Copy Details** in the error dialog to inspect it |
| "The selected file is not a PDF" | A `.pdf` extension is not enough — the contents must actually be a PDF. Check that it is not an image or text file saved under a PDF name |
| The list is empty | The PDF has no removable objects. Note that text only qualifies at 2+ characters shown 2+ times |
| Cannot save | Check that you are not targeting the source path, and that you have write permission in the destination folder |
| Cannot check a row | That row is "Not removable" (→ section 7) |

**Log location** (operational metrics only — no file paths, no PDF content):

```
%LOCALAPPDATA%\PdfImageRemoverForRag\logs\
```

**Settings location** (window position and size):

```
%LOCALAPPDATA%\PdfImageRemoverForRag\window.json
```

**Temporary file location** (thumbnails):

```
%LOCALAPPDATA%\PdfImageRemoverForRag\cache\
```

Thumbnails live in this folder rather than in memory, so opening many large PDFs costs little RAM — at the price of some disk activity. The folder is deleted when the app exits (and, if a previous run ended abnormally, at the next launch).

Check the version under **Help → About**.

---

## 11. Privacy

- The PDFs you open, their contents, and their file names and paths **never leave your PC.**
- The app makes no network connections.
- No usage data is collected or transmitted. Logs record operational metrics only and stay local.

---

## 12. License

MIT License. Copyright (c) 2026 Nakano Kappei — [LICENSE](../LICENSE)

Libraries used: PDFsharp (MIT) and PdfPig (Apache-2.0) — [license-notices.md](license-notices.md)

Please report bugs and requests at [GitHub Issues](https://github.com/Nakanokappei/pdf-image-remover-for-rag/issues).
