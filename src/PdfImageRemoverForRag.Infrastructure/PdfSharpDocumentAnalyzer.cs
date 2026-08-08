using PdfImageRemoverForRag.Core.Abstractions;
using PdfImageRemoverForRag.Core.Errors;
using PdfImageRemoverForRag.Core.Grouping;
using PdfImageRemoverForRag.Core.Hashing;
using PdfImageRemoverForRag.Core.Models;
using PdfImageRemoverForRag.Core.Validation;
using PdfImageRemoverForRag.Infrastructure.Internal;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Content;
using PdfSharp.Pdf.IO;

namespace PdfImageRemoverForRag.Infrastructure;

/// <summary>
/// PDFsharp-backed implementation of <see cref="IPdfDocumentAnalyzer"/>.
/// Walks every page, collects direct Image XObjects and Form XObjects,
/// resolves Form-embedded images (marking them unsafe to delete per §14.3),
/// merges thumbnails from <see cref="IThumbnailProvider"/>, then hands the
/// results to <see cref="ObjectGroupBuilder"/> in Core for grouping.
/// </summary>
public sealed class PdfSharpDocumentAnalyzer : IPdfDocumentAnalyzer
{
    readonly IThumbnailProvider _thumbnailProvider;

    public PdfSharpDocumentAnalyzer(IThumbnailProvider thumbnailProvider)
    {
        _thumbnailProvider = thumbnailProvider;
    }

    public async Task<PdfDocumentInfo> AnalyzeAsync(
        string pdfFilePath,
        int thumbnailMaxWidth = 160,
        int thumbnailMaxHeight = 120,
        IProgress<AnalysisProgress>? progress = null,
        CancellationToken ct = default)
    {
        // Check the header before either parser touches the bytes. The file
        // arrived with nothing but an extension vouching for it, and PDFsharp
        // and PdfPig are large parsers built for well-formed input.
        if (!PdfFileSignature.LooksLikePdf(pdfFilePath))
        {
            throw new PdfCleanerException(PdfCleanerErrorKind.NotAPdf,
                "The selected file is not a PDF.");
        }

        // The PDFsharp calls themselves are synchronous; wrap in Task.Run so
        // the caller (UI thread) never blocks on IO or hashing (spec §18).
        var (discoveries, pageDimensions, isEncrypted, pageCount, overlapRegions) = await Task.Run(
            () => SweepPdfsharp(pdfFilePath, progress, ct), ct).ConfigureAwait(false);

        // Ask the thumbnail provider off-thread as well. Missing keys or an
        // outright failure produce a null thumbnail, never an exception.
        IReadOnlyDictionary<string, byte[]> thumbnails;
        try
        {
            thumbnails = await _thumbnailProvider.ExtractThumbnailsAsync(
                pdfFilePath, thumbnailMaxWidth, thumbnailMaxHeight,
                RenderableImageHashes(discoveries), progress, ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // A thumbnail-provider crash must not fail analysis (spec §12).
            thumbnails = new Dictionary<string, byte[]>();
        }

        // Splice thumbnails back into the discoveries.
        var withThumbs = new List<ObjectDiscovery>(discoveries.Count);
        foreach (var d in discoveries)
        {
            var thumb = thumbnails.TryGetValue(d.StreamHash, out var png) ? png : null;
            withThumbs.Add(d with { ThumbnailBytes = thumb });
        }

        // Group in Core so grouping stays PDF-library-agnostic.
        progress?.Report(new AnalysisProgress(AnalysisPhase.Grouping, 0, 0));
        var detector = new FullPageImageDetector(pageDimensions);
        var builder = new ObjectGroupBuilder(detector);
        var groups = builder.Build(withThumbs);

        return new PdfDocumentInfo(
            FilePath: pdfFilePath,
            FileSize: new FileInfo(pdfFilePath).Length,
            PageCount: pageCount,
            IsEncrypted: isEncrypted,
            ObjectGroups: groups,
            OverlapRegions: overlapRegions,
            Pages: pageDimensions);
    }

    static (List<ObjectDiscovery> Discoveries,
            List<PageDimensions> PageDimensions,
            bool IsEncrypted,
            int PageCount,
            List<OverlapRegion> OverlapRegions) SweepPdfsharp(
        string path, IProgress<AnalysisProgress>? progress, CancellationToken ct)
    {
        try
        {
            using var doc = PdfReader.Open(path, PdfDocumentOpenMode.Import);
            var accumulators = new Dictionary<string, DiscoveryAccumulator>(StringComparer.Ordinal);
            var pageDims = new List<PageDimensions>(doc.PageCount);
            var overlapRegions = new List<OverlapRegion>();
            // Text value → where it is shown (one entry per showing).
            var textPlacementsByValue = new Dictionary<string, List<Placement>>(StringComparer.Ordinal);
            // Shape signature → where it is drawn + one bounding box for the size.
            var shapesBySignature = new Dictionary<string, ShapeAccumulator>(StringComparer.Ordinal);
            // Form object id → the artwork it paints, read once however many
            // pages draw it. The reported document places one form on eleven
            // pages; parsing its stream eleven times would buy nothing.
            var drawingsByForm = new Dictionary<string, FormDrawingReader.FormDrawing?>(StringComparer.Ordinal);
            // Form stream hash → where that drawing is placed.
            var drawingsByHash = new Dictionary<string, DrawingAccumulator>(StringComparer.Ordinal);

            for (int i = 0; i < doc.PageCount; i++)
            {
                ct.ThrowIfCancellationRequested();
                // Reported before the page is read, so the count shown is the
                // page being worked on rather than the last one finished.
                progress?.Report(new AnalysisProgress(
                    AnalysisPhase.ReadingPages, i, doc.PageCount));
                var page = doc.Pages[i];
                int pageNumber = i + 1;
                pageDims.Add(new PageDimensions(pageNumber, page.Width.Point, page.Height.Point));

                var (directImages, forms) = ImageXObjectCollector.CollectDirect(page.Resources);
                var contentBytes = PageContentAccessor.ReadMergedBytes(page);
                var sequence = ContentReader.ReadContent(contentBytes);
                var drawCalls = ContentStreamWalker.FindDrawCalls(sequence);

                // Text objects: record every shown string of 2+ characters,
                // decoded to readable Unicode (Identity-H / CJK fonts need the
                // font's ToUnicode map). The "2+ occurrences" filter is applied
                // after the sweep so a header repeated once per page qualifies
                // while a one-off line does not.
                // Each showing also carries the rectangle it covers, so the
                // usage-locations window can outline a string on the page the
                // same way it outlines an image.
                var textDecoder = new PdfTextDecoder(page.Resources);
                var textMetrics = new PdfFontMetrics(page.Resources);
                var textHits = ContentStreamWalker.FindTexts(sequence, textDecoder, textMetrics);
                foreach (var text in textHits)
                {
                    if (ReadableCharacterCount(text.Value) < MinReadableCharacters) continue;
                    if (!textPlacementsByValue.TryGetValue(text.Value, out var placements))
                    {
                        placements = new List<Placement>();
                        textPlacementsByValue[text.Value] = placements;
                    }
                    placements.Add(new Placement(pageNumber, text.X, text.Y, text.Width, text.Height));
                }

                // Vector shapes: record every paintable path, grouped by the
                // page-space signature. No occurrence-count filter (like images).
                var shapeHits = ContentStreamWalker.FindShapes(sequence);
                foreach (var shape in shapeHits)
                {
                    if (!shapesBySignature.TryGetValue(shape.Signature, out var acc))
                    {
                        acc = new ShapeAccumulator(shape.Width, shape.Height, shape.Geometry);
                        shapesBySignature[shape.Signature] = acc;
                    }
                    acc.Placements.Add(
                        new Placement(pageNumber, shape.X, shape.Y, shape.Width, shape.Height));
                }

                // Direct image XObjects — every Do call for that name becomes an occurrence.
                foreach (var img in directImages)
                {
                    var accumulator = GetOrCreate(accumulators, img.Dictionary, img.ObjectId);
                    foreach (var call in drawCalls)
                    {
                        if (call.ResourceName != img.ResourceName) continue;
                        accumulator.Occurrences.Add(new ObjectOccurrence(
                            pageNumber, img.ObjectId, img.ResourceName,
                            call.X, call.Y, call.Width, call.Height));
                    }
                }

                // Where objects of different kinds overlap on this page. Fed from
                // the hits just gathered, so it costs one pass over lists that
                // are already in hand.
                //
                // Note what goes in: EVERY text hit, not the ones that survive
                // the removable-text filters. A chart's axis labels are usually
                // unique strings one or two characters long, so the filters that
                // make sense for "repeated noise to delete" (2+ characters, shown
                // 2+ times) would hide exactly the text flattening exists for.
                overlapRegions.AddRange(OverlapDetector.Detect(
                    pageDims[^1],
                    PlacedObjectsOf(directImages, drawCalls, textHits, shapeHits, accumulators)));

                // Form XObjects — enumerate the Image XObjects inside them.
                // The image is drawn wherever the Form's Do call is placed,
                // so we approximate the on-page bbox with the Form's bbox.
                foreach (var form in forms)
                {
                    var formCalls = drawCalls.Where(c => c.ResourceName == form.ResourceName).ToArray();
                    if (formCalls.Length == 0) continue; // form is defined but never drawn
                    var embedded = ImageXObjectCollector.CollectImagesInsideForm(form.Dictionary);
                    foreach (var image in embedded)
                    {
                        var accumulator = GetOrCreate(accumulators, image.Dictionary, image.ObjectId);
                        // Any Form-mediated reference makes the image unsafe
                        // to remove — we cannot rewrite the shared Form's
                        // content stream without side effects on other pages.
                        accumulator.MarkUnsafe(
                            "This image cannot be removed safely because of the PDF's complex structure.");
                        foreach (var call in formCalls)
                        {
                            accumulator.Occurrences.Add(new ObjectOccurrence(
                                pageNumber, image.ObjectId, image.ResourceName,
                                call.X, call.Y, call.Width, call.Height));
                        }
                    }

                    // The form's own artwork — the paths it paints itself,
                    // which neither the page's content stream nor the image
                    // walk above can see.
                    if (!drawingsByForm.TryGetValue(form.ObjectId, out var drawing))
                    {
                        drawing = FormDrawingReader.Read(form.Dictionary);
                        drawingsByForm[form.ObjectId] = drawing;
                    }
                    if (drawing is null) continue;

                    if (!drawingsByHash.TryGetValue(drawing.StreamHash, out var drawingAcc))
                    {
                        drawingAcc = new DrawingAccumulator(form.ObjectId, drawing);
                        drawingsByHash[drawing.StreamHash] = drawingAcc;
                    }
                    // One occurrence per Do call: the drawing's box mapped
                    // through the transform in force where the form is placed.
                    foreach (var call in formCalls)
                    {
                        var box = call.Ctm.MapBoundingBox(
                            drawing.BoxX, drawing.BoxY, drawing.BoxWidth, drawing.BoxHeight);
                        drawingAcc.Placements.Add(
                            new Placement(pageNumber, box.X, box.Y, box.W, box.H));
                    }
                }
            }

            // Skip Image XObjects that live in /Resources but are never
            // referenced by a Do operator — these are "orphaned" images left
            // behind after a previous cleaning pass. Reporting them in the
            // UI would confuse users (spec §11 lists drawn images, not
            // dictionary entries).
            var discoveries = accumulators.Values
                .Where(a => a.Occurrences.Count > 0)
                .Select(a => a.ToDiscovery())
                .ToList();

            // Text discoveries: only strings shown 2+ times within this file
            // (the repeated-noise case the feature targets). Each showing is
            // an occurrence so the usage count and pages match the images.
            foreach (var (value, placements) in textPlacementsByValue)
            {
                if (placements.Count < MinTextOccurrences) continue;
                discoveries.Add(BuildTextDiscovery(value, placements));
            }

            // Shape discoveries: every drawn path (no occurrence-count filter,
            // like images). The user selects which to remove.
            foreach (var (signature, acc) in shapesBySignature)
            {
                discoveries.Add(BuildShapeDiscovery(signature, acc));
            }

            // Drawing discoveries: one per form that paints artwork of its own,
            // wherever it is placed. No occurrence-count filter, like shapes.
            foreach (var (hash, acc) in drawingsByHash)
            {
                discoveries.Add(BuildDrawingDiscovery(hash, acc));
            }

            return (discoveries, pageDims, doc.SecuritySettings.IsEncrypted, doc.PageCount, overlapRegions);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (PdfCleanerException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw PdfsharpExceptionMapper.Map(ex, "PDF analysis");
        }
    }

    /// <summary>
    /// The image streams a thumbnail could actually be made from, so extraction
    /// can stop when it has them — and not start at all when there are none.
    ///
    /// The sweep already read every image's filter, and the filter decides:
    /// JPEG 2000, JBIG2 and CCITT are formats neither library here can turn
    /// into pixels, and a document made entirely of them once cost ten seconds
    /// to produce an empty dictionary. Everything else is worth trying; being
    /// wrong in that direction costs a decode that fails, while being wrong the
    /// other way silently loses a thumbnail.
    /// </summary>
    static IReadOnlyCollection<string> RenderableImageHashes(IReadOnlyList<ObjectDiscovery> discoveries)
    {
        var hashes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var discovery in discoveries)
        {
            if (!discovery.Kind.IsImageXObject()) continue;
            if (UndecodableFilters.Any(f => discovery.Compression.Contains(f, StringComparison.Ordinal)))
            {
                continue;
            }
            hashes.Add(discovery.StreamHash);
        }
        return hashes;
    }

    static readonly string[] UndecodableFilters =
        { "JPXDecode", "JBIG2Decode", "CCITTFaxDecode" };

    /// <summary>
    /// Everything drawn on one page, as overlap detection wants it: kind,
    /// the identity the cleaner matches instances on, and the rectangle.
    ///
    /// Images are identified by stream hash (the same key that groups them and
    /// that the cleaner resolves resource names from), text by its shown string,
    /// shapes by their path signature. Images reached through a Form XObject are
    /// left out: their content stream is shared with other pages and cannot be
    /// rewritten, which is the same reason they are not safely removable.
    ///
    /// The kind is read back from the accumulator that was just built for the
    /// object rather than assumed to be <see cref="RemovableKind.Image"/>, so a
    /// shadow that sits in an overlap region is named a shadow in the Flatten
    /// panel too. Deciding it a second time here is what once had that panel
    /// calling a drawing an image.
    /// </summary>
    static List<PlacedObject> PlacedObjectsOf(
        IReadOnlyList<ImageXObjectCollector.ImageEntry> directImages,
        IReadOnlyList<ContentStreamWalker.DrawCall> drawCalls,
        IReadOnlyList<ContentStreamWalker.TextHit> textHits,
        IReadOnlyList<ContentStreamWalker.ShapeHit> shapeHits,
        IReadOnlyDictionary<string, DiscoveryAccumulator> accumulators)
    {
        var placed = new List<PlacedObject>(drawCalls.Count + textHits.Count + shapeHits.Count);

        foreach (var image in directImages)
        {
            string hash = ImageXObjectCollector.ComputeStreamHash(image.Dictionary);
            var kind = accumulators.TryGetValue(image.ObjectId, out var accumulator)
                ? accumulator.Kind
                : RemovableKind.Image;
            foreach (var call in drawCalls)
            {
                if (call.ResourceName != image.ResourceName) continue;
                placed.Add(new PlacedObject(
                    kind, hash, call.X, call.Y, call.Width, call.Height));
            }
        }

        foreach (var text in textHits)
        {
            placed.Add(new PlacedObject(
                RemovableKind.Text, text.Value, text.X, text.Y, text.Width, text.Height));
        }

        foreach (var shape in shapeHits)
        {
            // A stroke-only path hides nothing, and the detector connects it
            // differently for that reason.
            placed.Add(new PlacedObject(
                RemovableKind.Shape, shape.Signature, shape.X, shape.Y, shape.Width, shape.Height,
                HidesWhatIsBehind: shape.Geometry.IsFilled));
        }

        return placed;
    }

    /// <summary>
    /// Minimum READABLE characters for a text object to be removable. One, so
    /// that a single letter repeated across a document qualifies: a
    /// confidentiality marking is often exactly one character ("S" on every
    /// page of a real manual), and at two it could not be removed at all.
    ///
    /// Counting readable characters rather than all of them is what keeps a
    /// string of spaces out of the list. Such a row shows nothing, tells the
    /// user nothing, and joins words together if it is removed. It could
    /// already appear before this count was lowered — two spaces were two
    /// characters — so this closes that as well.
    ///
    /// The repetition filter below is the other half: a lone character shown
    /// once is still not offered.
    /// </summary>
    const int MinReadableCharacters = 1;

    /// <summary>Minimum showings within one file before a text is treated as noise.</summary>
    const int MinTextOccurrences = 2;

    /// <summary>
    /// How many characters of a shown string a reader would actually see.
    /// Whitespace and control characters do not count — they take up room on
    /// the page without putting a mark on it.
    /// </summary>
    static int ReadableCharacterCount(string value)
    {
        var count = 0;
        foreach (var character in value)
        {
            if (!char.IsWhiteSpace(character) && !char.IsControl(character)) count++;
        }
        return count;
    }

    /// <summary>
    /// One place an object was drawn: the page and the rectangle it covers, in
    /// points. Text and shapes group position-independently, so the placements
    /// are collected alongside the group key rather than being part of it.
    /// </summary>
    sealed record Placement(int PageNumber, double X, double Y, double Width, double Height);

    /// <summary>Mutable staging for a shape during the sweep.</summary>
    sealed class ShapeAccumulator
    {
        public ShapeAccumulator(double width, double height, ShapeGeometry geometry)
        {
            Width = width;
            Height = height;
            Geometry = geometry;
        }

        public List<Placement> Placements { get; } = new();
        public double Width { get; }
        public double Height { get; }
        public ShapeGeometry Geometry { get; }
    }

    /// <summary>Mutable staging for one form's artwork during the sweep.</summary>
    sealed class DrawingAccumulator
    {
        public DrawingAccumulator(string objectId, FormDrawingReader.FormDrawing drawing)
        {
            ObjectId = objectId;
            Drawing = drawing;
        }

        public List<Placement> Placements { get; } = new();
        public string ObjectId { get; }
        public FormDrawingReader.FormDrawing Drawing { get; }
    }

    /// <summary>
    /// Build a drawing discovery. Grouped by the form's stream hash — the same
    /// identity an image uses, because a form is a stream the file stores once
    /// too — so one form drawn on many pages is one object with many
    /// placements. The size is the first placement's, in points.
    ///
    /// It is safely removable: what gets deleted is the page's own Do call, not
    /// the shared form, so removing it from one page cannot disturb another.
    /// </summary>
    static ObjectDiscovery BuildDrawingDiscovery(string hash, DrawingAccumulator acc)
    {
        var occurrences = acc.Placements.Select(ToOccurrence).ToArray();
        var first = acc.Placements[0];
        return new ObjectDiscovery(
            ObjectId: acc.ObjectId,
            StreamHash: hash,
            PixelWidth: (int)Math.Round(first.Width),
            PixelHeight: (int)Math.Round(first.Height),
            ColorSpace: "Drawing",
            BitsPerComponent: 0,
            Compression: "Drawing",
            StreamByteCount: 0,
            IsImageMask: false,
            IsSafelyRemovable: true,
            UnsafeReason: null,
            ThumbnailBytes: null,
            Occurrences: occurrences,
            Kind: RemovableKind.Drawing,
            TextValue: null,
            ShapeGeometry: null,
            DrawingGeometry: acc.Drawing.Geometry);
    }

    /// <summary>
    /// Build a shape discovery. Groups by the page-space path signature (never
    /// collides with image/text hashes); the bounding box gives the displayed
    /// size in points. The signature is stored in <c>TextValue</c> as the
    /// cleaner's match key.
    /// </summary>
    static ObjectDiscovery BuildShapeDiscovery(string signature, ShapeAccumulator acc)
    {
        var occurrences = acc.Placements.Select(ToOccurrence).ToArray();
        return new ObjectDiscovery(
            ObjectId: string.Empty,
            StreamHash: "SHAPE:" + StreamHasher.Sha256Hex(System.Text.Encoding.UTF8.GetBytes(signature)),
            PixelWidth: (int)Math.Round(acc.Width),
            PixelHeight: (int)Math.Round(acc.Height),
            ColorSpace: "Shape",
            BitsPerComponent: 0,
            Compression: "Shape",
            StreamByteCount: 0,
            IsImageMask: false,
            IsSafelyRemovable: true,
            UnsafeReason: null,
            ThumbnailBytes: null,
            Occurrences: occurrences,
            Kind: RemovableKind.Shape,
            TextValue: signature,
            ShapeGeometry: acc.Geometry);
    }

    /// <summary>
    /// A placement as an occurrence. Text and shapes have no indirect object or
    /// resource name — those identify an Image XObject — so the id and name are
    /// empty and the rectangle carries the information.
    /// </summary>
    static ObjectOccurrence ToOccurrence(Placement p) =>
        new(p.PageNumber, string.Empty, string.Empty, p.X, p.Y, p.Width, p.Height);

    /// <summary>
    /// Build a text discovery. The stream hash is derived from the string so
    /// it groups by value and never collides with an image's raw-stream hash;
    /// each occurrence carries the rectangle that showing covers.
    /// </summary>
    static ObjectDiscovery BuildTextDiscovery(string value, IReadOnlyList<Placement> placements)
    {
        var occurrences = placements.Select(ToOccurrence).ToArray();
        return new ObjectDiscovery(
            ObjectId: string.Empty,
            StreamHash: "TEXT:" + StreamHasher.Sha256Hex(System.Text.Encoding.UTF8.GetBytes(value)),
            PixelWidth: 0,
            PixelHeight: 0,
            ColorSpace: "Text",
            BitsPerComponent: 0,
            Compression: "Text",
            StreamByteCount: System.Text.Encoding.UTF8.GetByteCount(value),
            IsImageMask: false,
            IsSafelyRemovable: true,
            UnsafeReason: null,
            ThumbnailBytes: null,
            Occurrences: occurrences,
            Kind: RemovableKind.Text,
            TextValue: value);
    }

    static DiscoveryAccumulator GetOrCreate(
        Dictionary<string, DiscoveryAccumulator> map,
        PdfDictionary imageDict,
        string objectId)
    {
        if (map.TryGetValue(objectId, out var existing)) return existing;
        var acc = new DiscoveryAccumulator(imageDict, objectId);
        map[objectId] = acc;
        return acc;
    }

    /// <summary>
    /// Mutable staging record — collected during the PDFsharp sweep and
    /// baked into an immutable <see cref="ObjectDiscovery"/> at the end.
    /// </summary>
    sealed class DiscoveryAccumulator
    {
        readonly string _objectId;
        readonly string _streamHash;
        readonly int _pixelWidth;
        readonly int _pixelHeight;
        readonly string _colorSpace;
        readonly int _bitsPerComponent;
        readonly string _compression;
        readonly long _streamByteCount;
        readonly bool _isImageMask;
        public List<ObjectOccurrence> Occurrences { get; } = new();

        /// <summary>Image or shadow — decided once, from the object's bytes.</summary>
        public RemovableKind Kind { get; }
        bool _isSafelyRemovable = true;
        string? _unsafeReason;

        public DiscoveryAccumulator(PdfDictionary dict, string objectId)
        {
            _objectId = objectId;
            _streamHash = ImageXObjectCollector.ComputeStreamHash(dict);
            _pixelWidth = dict.Elements.GetInteger("/Width");
            _pixelHeight = dict.Elements.GetInteger("/Height");
            _colorSpace = ReadColorSpaceLabel(dict);
            _bitsPerComponent = dict.Elements.GetInteger("/BitsPerComponent");
            _compression = ReadFilterLabel(dict);
            _streamByteCount = dict.Stream?.Length ?? 0;
            _isImageMask = dict.Elements.GetBoolean("/ImageMask");
            // Decided once, here, from the bytes: a shadow layer carries one
            // flat color and gets its shape from a mask. Every placement of
            // the same stream is the same kind, so the question is asked per
            // object and never per occurrence.
            Kind = ShadowLayerDetector.IsShadowLayer(dict)
                ? RemovableKind.Shadow
                : RemovableKind.Image;
        }

        public void MarkUnsafe(string reason)
        {
            _isSafelyRemovable = false;
            // Keep the first reason encountered — later reasons are usually
            // the same message, and swapping them would be noisy.
            _unsafeReason ??= reason;
        }

        public ObjectDiscovery ToDiscovery() => new(
            ObjectId: _objectId,
            StreamHash: _streamHash,
            PixelWidth: _pixelWidth,
            PixelHeight: _pixelHeight,
            ColorSpace: _colorSpace,
            BitsPerComponent: _bitsPerComponent,
            Compression: _compression,
            StreamByteCount: _streamByteCount,
            IsImageMask: _isImageMask,
            IsSafelyRemovable: _isSafelyRemovable,
            UnsafeReason: _unsafeReason,
            ThumbnailBytes: null,
            Occurrences: Occurrences.ToArray(),
            Kind: Kind);

        static string ReadColorSpaceLabel(PdfDictionary dict)
        {
            var el = dict.Elements["/ColorSpace"];
            return el switch
            {
                PdfName n => n.Value,
                PdfArray a when a.Elements.Count > 0 => a.Elements[0].ToString() ?? "?",
                null => dict.Elements.GetBoolean("/ImageMask") ? "ImageMask" : "?",
                _ => el.ToString() ?? "?",
            };
        }

        static string ReadFilterLabel(PdfDictionary dict)
        {
            var el = dict.Elements["/Filter"];
            return el switch
            {
                PdfName n => n.Value,
                PdfArray a => string.Join("+", a.Elements.Select(x => x.ToString() ?? "?")),
                null => "Raw",
                _ => el.ToString() ?? "?",
            };
        }
    }
}
