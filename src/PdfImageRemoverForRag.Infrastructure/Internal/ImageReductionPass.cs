using PdfImageRemoverForRag.Core.Abstractions;
using PdfImageRemoverForRag.Core.Models;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.Filters;

namespace PdfImageRemoverForRag.Infrastructure.Internal;

/// <summary>
/// Redraws the document's images at the size the user asked them to be.
///
/// Two things are at stake and they pull against each other. A picture with
/// more pixels than anyone will read costs file size and nothing else, and file
/// size is what stops a document reaching a RAG pipeline at all - the
/// customer's upload limit is 15 MB and a manual full of screenshots passes it
/// easily. But a page reduced too far stops being readable BY THE PIPELINE:
/// its characters run together, and the retrieval reads the wrong word with no
/// sign that it did. <see cref="ImageReduction"/> holds where the user put that
/// line; this pass applies it.
///
/// It changes no appearance. A PDF draws an image into a rectangle the page
/// decides, scaling whatever resolution the image happens to have, so the same
/// picture with fewer pixels lands in exactly the same place at the same size.
///
/// Nothing here can make an image LARGER. The scale is clamped at 1, so an
/// image already under the ceiling keeps every pixel it had; and a rewrite that
/// would take more bytes than the stream it replaces is thrown away.
///
/// A JPEG that already fits is written again when it was saved ABOVE the
/// quality ceiling, at the same size. A screenshot-heavy manual exported at
/// quality 95 is under the size ceiling already, so nothing above would touch
/// it, and the quality alone is worth several megabytes.
///
/// What is left alone: anything whose bytes this cannot read back (JPEG 2000,
/// JBIG2, fax), anything but 8 bits a component, indexed and separation colors,
/// and anything already small enough. Leaving an image as it was is always safe;
/// writing one back wrongly is not.
/// </summary>
internal static class ImageReductionPass
{
    /// <summary>
    /// Reduce every image in the document, and answer with the stream hashes of
    /// those that changed. The caller needs them because a resized image is no
    /// longer the stream it was: anything checking that a retained image is
    /// still present has to look for the new bytes, not the old.
    /// </summary>
    public static IReadOnlyList<string> Apply(
        PdfDocument document,
        IImageResampler resampler,
        ImageReduction reduction,
        CancellationToken ct)
    {
        // The ceiling follows the page, so an image drawn on more than one page
        // has to be measured against all of them and take the largest. Sizing it
        // for the smallest page would leave it short on the biggest, and a
        // picture cannot be un-blurred afterwards. One image can be named by
        // several pages; the object is the thing to visit once, not the name.
        var ceilings = new Dictionary<string, Ceiling>(StringComparer.Ordinal);

        // By index: the page collection's own enumerator does not answer
        // PdfPage, so a filtered foreach over it silently visits nothing.
        for (int i = 0; i < document.PageCount; i++)
        {
            var page = document.Pages[i];
            var (width, height) = reduction.CeilingFor(page.Width.Point, page.Height.Point);

            foreach (var entry in ImageXObjectCollector.EnumerateImageEntries(page.Resources))
            {
                ct.ThrowIfCancellationRequested();
                var id = entry.Dictionary.Internals.ObjectID.ToString();

                ceilings[id] = ceilings.TryGetValue(id, out var seen)
                    ? seen with
                    {
                        Width = Math.Max(seen.Width, width),
                        Height = Math.Max(seen.Height, height),
                    }
                    : new Ceiling(entry.Dictionary, width, height);
            }
        }

        var resized = new List<string>();
        foreach (var ceiling in ceilings.Values)
        {
            ct.ThrowIfCancellationRequested();

            // Hashed before the fit, because the fit is what changes the bytes.
            var hash = ImageXObjectCollector.ComputeStreamHash(ceiling.Image);
            if (Fit(ceiling.Image, resampler, ceiling.Width, ceiling.Height, reduction.JpegQuality))
            {
                resized.Add(hash);
            }
        }
        return resized;
    }

    /// <summary>One image and the largest ceiling any page put on it.</summary>
    readonly record struct Ceiling(PdfDictionary Image, int Width, int Height);

    /// <summary>
    /// Fit one image, answering whether it was changed. Every reason to leave it
    /// alone answers false, and there are many — this is a best-effort saving,
    /// not a promise about every image in every file.
    /// </summary>
    static bool Fit(
        PdfDictionary image, IImageResampler resampler,
        int maximumWidth, int maximumHeight, int jpegQuality)
    {
        int width = image.Elements.GetInteger("/Width");
        int height = image.Elements.GetInteger("/Height");
        if (width < 1 || height < 1) return false;

        // Clamped at 1 LAST, so the clamp reads as what it is: this pass never
        // enlarges an image, whatever ceiling it is given.
        double scale = Math.Min(
            1.0, Math.Min(maximumWidth / (double)width, maximumHeight / (double)height));

        // An image that already fits is left alone unless it is a JPEG written
        // above the ceiling, which is worth writing again at the ceiling — that
        // is the whole saving on a document of screenshots taken at quality 95.
        // Asked before the pixels are read: unpacking a Flate image that is
        // going to be left alone is the expensive way to decide nothing.
        bool resizing = scale < 1.0;
        if (!resizing && FilterNameOf(image) != "/DCTDecode") return false;

        var stored = Read(image, width, height);
        if (stored is null) return false;

        if (!resizing
            && (Core.Imaging.JpegQuality.Estimate(stored.Data) is not { } quality
                || quality <= jpegQuality))
        {
            return false;
        }

        // At scale 1 this asks for the size it already is, which is a re-encode
        // and nothing else — the one path that reaches here without resizing.
        int fittedWidth = Math.Max(1, (int)Math.Round(width * scale));
        int fittedHeight = Math.Max(1, (int)Math.Round(height * scale));
        var fitted = resampler.Resize(stored, fittedWidth, fittedHeight, jpegQuality);
        if (fitted is null) return false;

        // Compared as they will be STORED, not as they were handed back: raw
        // samples are only meaningful against the stream once compressed. And a
        // "smaller" image that takes more bytes is not a saving — a drawing of
        // three flat colors compresses better whole than resampled, and there
        // is nothing to gain by making it worse.
        var stream = fitted.Encoding == StoredImageEncoding.Jpeg
            ? fitted.Data
            : Filtering.FlateDecode.Encode(fitted.Data, PdfFlateEncodeMode.Default);
        if (stream.Length >= image.Stream.Value.Length) return false;

        Write(image, fitted, stream);
        return true;
    }

    /// <summary>
    /// The image's pixels, or null when they are not in a form that can be read
    /// back and written again.
    /// </summary>
    static StoredImage? Read(PdfDictionary image, int width, int height)
    {
        // An image mask is one bit a pixel painting the current color, not a
        // picture; and a decode array or predictor means the samples are not
        // simply what the stream holds.
        if (image.Elements.GetBoolean("/ImageMask")) return null;
        if (image.Elements.ContainsKey("/Decode")) return null;
        if (image.Elements.ContainsKey("/DecodeParms")) return null;

        var filter = FilterNameOf(image);
        if (filter is null) return null;

        try
        {
            if (filter == "/DCTDecode")
            {
                // The stream IS a JPEG file, so it goes to the resampler whole.
                return new StoredImage(
                    width, height, image.Stream.Value, StoredImageEncoding.Jpeg,
                    Components: 3, SoftMask: ReadMask(image));
            }

            if (filter != "/FlateDecode") return null;
            int components = ComponentsOf(image);
            if (components == 0 || image.Elements.GetInteger("/BitsPerComponent") != 8) return null;

            return new StoredImage(
                width, height, image.Stream.UnfilteredValue, StoredImageEncoding.Samples,
                components, ReadMask(image));
        }
        catch (Exception)
        {
            // A stream that will not unfilter is one to leave alone.
            return null;
        }
    }

    /// <summary>The image's soft mask, when it is one this can read.</summary>
    static StoredMask? ReadMask(PdfDictionary image)
    {
        if (image.Elements.GetObject("/SMask") is not PdfDictionary mask) return null;
        if (FilterNameOf(mask) != "/FlateDecode") return null;
        if (mask.Elements.ContainsKey("/DecodeParms")) return null;
        if (mask.Elements.GetInteger("/BitsPerComponent") != 8) return null;

        int width = mask.Elements.GetInteger("/Width");
        int height = mask.Elements.GetInteger("/Height");
        if (width < 1 || height < 1) return null;

        try
        {
            return new StoredMask(width, height, mask.Stream.UnfilteredValue);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Put the redrawn pixels back into the same object, so every page that
    /// names it goes on naming it. Only the bytes and the size change.
    /// </summary>
    static void Write(PdfDictionary image, StoredImage fitted, byte[] stream)
    {
        image.Elements.SetInteger("/Width", fitted.Width);
        image.Elements.SetInteger("/Height", fitted.Height);
        image.Elements.SetInteger("/BitsPerComponent", 8);

        if (fitted.Encoding == StoredImageEncoding.Jpeg)
        {
            image.Elements.SetName("/Filter", "/DCTDecode");
            image.Elements.SetName("/ColorSpace", "/DeviceRGB");
        }
        else
        {
            image.Elements.SetName("/Filter", "/FlateDecode");
            image.Elements.SetName(
                "/ColorSpace", fitted.Components == 1 ? "/DeviceGray" : "/DeviceRGB");
        }
        image.Stream.Value = stream;
        image.Elements.SetInteger("/Length", stream.Length);

        if (fitted.SoftMask is null) return;
        if (image.Elements.GetObject("/SMask") is not PdfDictionary mask) return;

        mask.Elements.SetInteger("/Width", fitted.SoftMask.Width);
        mask.Elements.SetInteger("/Height", fitted.SoftMask.Height);
        mask.Elements.SetInteger("/BitsPerComponent", 8);
        mask.Elements.SetName("/Filter", "/FlateDecode");
        mask.Elements.SetName("/ColorSpace", "/DeviceGray");
        mask.Stream.Value = Filtering.FlateDecode.Encode(
            fitted.SoftMask.Data, PdfFlateEncodeMode.Default);
        mask.Elements.SetInteger("/Length", mask.Stream.Value.Length);
    }

    /// <summary>
    /// The one filter a stream is under, or null when there are none or several
    /// — a chain is a shape this does not attempt to reproduce.
    /// </summary>
    static string? FilterNameOf(PdfDictionary dictionary)
    {
        // Read as an item rather than through GetName: an ARRAY of filters
        // answers the same empty string there as no filter at all, and a chain
        // of two is exactly the case that must not be mistaken for one.
        return Resolve(dictionary, "/Filter") switch
        {
            PdfName name => name.Value,
            PdfArray array when array.Elements.Count == 1 => array.Elements[0] is PdfName only
                ? only.Value
                : null,
            _ => null,
        };
    }

    /// <summary>An entry with any indirect reference followed.</summary>
    static PdfItem? Resolve(PdfDictionary dictionary, string key)
    {
        var item = dictionary.Elements.GetValue(key);
        return item is PdfReference reference ? reference.Value : item;
    }

    /// <summary>
    /// How many samples a pixel has, or zero for a color space whose samples
    /// this cannot interpret — indexed palettes and separations among them.
    /// </summary>
    static int ComponentsOf(PdfDictionary image)
    {
        switch (Resolve(image, "/ColorSpace"))
        {
            case PdfName name:
                return name.Value switch
                {
                    "/DeviceRGB" => 3,
                    "/DeviceGray" => 1,
                    _ => 0,
                };

            // An ICC profile says how many components it describes, and the
            // samples underneath are ordinary gray or RGB.
            case PdfArray array when array.Elements.Count == 2
                                     && array.Elements[0] is PdfName { Value: "/ICCBased" }:
                var profile = array.Elements.GetDictionary(1);
                int components = profile?.Elements.GetInteger("/N") ?? 0;
                return components is 1 or 3 ? components : 0;

            default:
                return 0;
        }
    }
}
