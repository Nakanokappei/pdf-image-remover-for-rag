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
/// results to <see cref="ImageGroupBuilder"/> in Core for grouping.
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
                "選択されたファイルは PDF ではありません。");
        }

        // The PDFsharp calls themselves are synchronous; wrap in Task.Run so
        // the caller (UI thread) never blocks on IO or hashing (spec §18).
        var (discoveries, pageDimensions, isEncrypted, pageCount) = await Task.Run(
            () => SweepPdfsharp(pdfFilePath, progress, ct), ct).ConfigureAwait(false);

        // Ask the thumbnail provider off-thread as well. Missing keys or an
        // outright failure produce a null thumbnail, never an exception.
        IReadOnlyDictionary<string, byte[]> thumbnails;
        try
        {
            thumbnails = await _thumbnailProvider.ExtractThumbnailsAsync(
                pdfFilePath, thumbnailMaxWidth, thumbnailMaxHeight, progress, ct)
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
        var withThumbs = new List<ImageDiscovery>(discoveries.Count);
        foreach (var d in discoveries)
        {
            var thumb = thumbnails.TryGetValue(d.StreamHash, out var png) ? png : null;
            withThumbs.Add(d with { ThumbnailBytes = thumb });
        }

        // Group in Core so grouping stays PDF-library-agnostic.
        progress?.Report(new AnalysisProgress(AnalysisPhase.Grouping, 0, 0));
        var detector = new FullPageImageDetector(pageDimensions);
        var builder = new ImageGroupBuilder(detector);
        var groups = builder.Build(withThumbs);

        return new PdfDocumentInfo(
            FilePath: pdfFilePath,
            FileSize: new FileInfo(pdfFilePath).Length,
            PageCount: pageCount,
            IsEncrypted: isEncrypted,
            ImageGroups: groups);
    }

    static (List<ImageDiscovery> Discoveries,
            List<PageDimensions> PageDimensions,
            bool IsEncrypted,
            int PageCount) SweepPdfsharp(
        string path, IProgress<AnalysisProgress>? progress, CancellationToken ct)
    {
        try
        {
            using var doc = PdfReader.Open(path, PdfDocumentOpenMode.Import);
            var accumulators = new Dictionary<string, DiscoveryAccumulator>(StringComparer.Ordinal);
            var pageDims = new List<PageDimensions>(doc.PageCount);
            // Text value → the pages it is shown on (one entry per showing).
            var textPagesByValue = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            // Shape signature → the pages it is drawn on + one bounding box.
            var shapesBySignature = new Dictionary<string, ShapeAccumulator>(StringComparer.Ordinal);

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
                var textDecoder = new PdfTextDecoder(page.Resources);
                foreach (var text in ContentStreamWalker.FindShownTexts(sequence, textDecoder))
                {
                    if (text.Length < MinTextLength) continue;
                    if (!textPagesByValue.TryGetValue(text, out var pages))
                    {
                        pages = new List<int>();
                        textPagesByValue[text] = pages;
                    }
                    pages.Add(pageNumber);
                }

                // Vector shapes: record every paintable path, grouped by the
                // page-space signature. No occurrence-count filter (like images).
                foreach (var shape in ContentStreamWalker.FindShapes(sequence))
                {
                    if (!shapesBySignature.TryGetValue(shape.Signature, out var acc))
                    {
                        acc = new ShapeAccumulator(shape.Width, shape.Height, shape.Geometry);
                        shapesBySignature[shape.Signature] = acc;
                    }
                    acc.Pages.Add(pageNumber);
                }

                // Direct image XObjects — every Do call for that name becomes an occurrence.
                foreach (var img in directImages)
                {
                    var accumulator = GetOrCreate(accumulators, img.Dictionary, img.ObjectId);
                    foreach (var call in drawCalls)
                    {
                        if (call.ResourceName != img.ResourceName) continue;
                        accumulator.Occurrences.Add(new PdfImageOccurrence(
                            pageNumber, img.ObjectId, img.ResourceName,
                            call.X, call.Y, call.Width, call.Height));
                    }
                }

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
                            "複雑なPDF構造のため、この画像は安全に削除できません。");
                        foreach (var call in formCalls)
                        {
                            accumulator.Occurrences.Add(new PdfImageOccurrence(
                                pageNumber, image.ObjectId, image.ResourceName,
                                call.X, call.Y, call.Width, call.Height));
                        }
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
            foreach (var (value, pages) in textPagesByValue)
            {
                if (pages.Count < MinTextOccurrences) continue;
                discoveries.Add(BuildTextDiscovery(value, pages));
            }

            // Shape discoveries: every drawn path (no occurrence-count filter,
            // like images). The user selects which to remove.
            foreach (var (signature, acc) in shapesBySignature)
            {
                discoveries.Add(BuildShapeDiscovery(signature, acc));
            }

            return (discoveries, pageDims, doc.SecuritySettings.IsEncrypted, doc.PageCount);
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
            throw PdfsharpExceptionMapper.Map(ex, "PDF 解析");
        }
    }

    /// <summary>Minimum characters for a text object to be removable (§ user request).</summary>
    const int MinTextLength = 2;

    /// <summary>Minimum showings within one file before a text is treated as noise.</summary>
    const int MinTextOccurrences = 2;

    /// <summary>Mutable staging for a shape during the sweep.</summary>
    sealed class ShapeAccumulator
    {
        public ShapeAccumulator(double width, double height, ShapeGeometry geometry)
        {
            Width = width;
            Height = height;
            Geometry = geometry;
        }

        public List<int> Pages { get; } = new();
        public double Width { get; }
        public double Height { get; }
        public ShapeGeometry Geometry { get; }
    }

    /// <summary>
    /// Build a shape discovery. Groups by the page-space path signature (never
    /// collides with image/text hashes); the bounding box gives the displayed
    /// size in points. The signature is stored in <c>TextValue</c> as the
    /// cleaner's match key.
    /// </summary>
    static ImageDiscovery BuildShapeDiscovery(string signature, ShapeAccumulator acc)
    {
        var occurrences = acc.Pages
            .Select(page => new PdfImageOccurrence(page, string.Empty, string.Empty, 0, 0, 0, 0))
            .ToArray();
        return new ImageDiscovery(
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
    /// Build a text discovery. The stream hash is derived from the string so
    /// it groups by value and never collides with an image's raw-stream hash;
    /// occurrences carry only the page number (no on-page rectangle).
    /// </summary>
    static ImageDiscovery BuildTextDiscovery(string value, IReadOnlyList<int> pages)
    {
        var occurrences = pages
            .Select(page => new PdfImageOccurrence(page, string.Empty, string.Empty, 0, 0, 0, 0))
            .ToArray();
        return new ImageDiscovery(
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
    /// baked into an immutable <see cref="ImageDiscovery"/> at the end.
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
        public List<PdfImageOccurrence> Occurrences { get; } = new();
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
        }

        public void MarkUnsafe(string reason)
        {
            _isSafelyRemovable = false;
            // Keep the first reason encountered — later reasons are usually
            // the same message, and swapping them would be noisy.
            _unsafeReason ??= reason;
        }

        public ImageDiscovery ToDiscovery() => new(
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
            Occurrences: Occurrences.ToArray());

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
