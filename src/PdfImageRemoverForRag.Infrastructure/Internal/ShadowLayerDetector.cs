using PdfSharp.Pdf;

namespace PdfImageRemoverForRag.Infrastructure.Internal;

/// <summary>
/// Tells a shadow layer from a picture.
///
/// A shadow has no picture in it. PowerPoint (and every other producer, since
/// PDF has no blur operator) exports one as an Image XObject filled with a
/// single flat colour, plus a soft mask holding the blurred outline. Two
/// measurements were taken from a customer's document and from slides exported
/// on purpose, one per effect:
///
/// <list type="bullet">
///   <item>every shadow layer was one flat colour — 49 of them, all
///   <c>000000</c> — while every real picture used many;</item>
///   <item>glow, soft edges and reflection are NOT separate layers: the
///   producer rasterises them together with the object they belong to, so
///   those images carry real pixels and are not shadows.</item>
/// </list>
///
/// Judging the mask instead is what the first attempt did, and the slides
/// disproved it: a shape's outer shadow reaches full opacity (so "never
/// opaque" missed it) and a reflection is faint everywhere (so "mostly
/// transparent" wrongly caught it). The colour count is the honest signal.
/// </summary>
internal static class ShadowLayerDetector
{
    /// <summary>
    /// Whether this Image XObject is a shadow layer: one flat colour, drawn
    /// through a soft mask. Anything that cannot be read as plain samples is
    /// answered <c>false</c> — an unreadable image is left an ordinary image
    /// rather than guessed at.
    /// </summary>
    public static bool IsShadowLayer(PdfDictionary image)
    {
        // No mask, no shadow: the mask is what gives the flat colour a shape.
        // A flat-colour image without one is a plain filled rectangle, which
        // is content the page shows as itself.
        if (image.Elements.GetDictionary("/SMask") is null) return false;

        var channels = image.Elements.GetName("/ColorSpace") switch
        {
            "/DeviceRGB" => 3,
            "/DeviceGray" => 1,
            _ => 0,
        };
        if (channels == 0 || image.Elements.GetInteger("/BitsPerComponent") != 8) return false;

        // Only encodings that undo to samples can be judged. A JPEG shadow is
        // not a thing producers make (they are generated, not photographed),
        // so nothing real is lost by declining to decode one.
        var filter = image.Elements.GetName("/Filter");
        if (filter is not ("/FlateDecode" or "")) return false;

        var samples = image.Stream?.UnfilteredValue;
        if (samples is null) return false;

        var width = image.Elements.GetInteger("/Width");
        var height = image.Elements.GetInteger("/Height");
        var expected = (long)width * height * channels;
        if (expected <= 0 || samples.LongLength < expected) return false;

        // One flat colour, tested channel by channel and abandoned at the first
        // pixel that differs. A photograph is rejected within a few bytes; only
        // an image that really is uniform is read to the end.
        for (var channel = 0; channel < channels; channel++)
        {
            var value = samples[channel];
            for (long i = channel; i < expected; i += channels)
            {
                if (samples[i] != value) return false;
            }
        }

        return true;
    }
}
