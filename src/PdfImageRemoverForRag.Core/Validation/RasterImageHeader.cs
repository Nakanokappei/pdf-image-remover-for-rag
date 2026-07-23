namespace PdfImageRemoverForRag.Core.Validation;

/// <summary>
/// Reads the pixel dimensions of a PNG or JPEG out of its header, without
/// decoding it.
///
/// This exists to keep a hostile image away from the decoder. The bytes we
/// hand to GDI+ come out of a PDF the user opened, and a "decompression bomb"
/// — a few kilobytes that declare 40000x40000 pixels — costs about 6 GB the
/// moment it is decoded. Catching the resulting out-of-memory afterwards is
/// not a defence: the allocation has already been attempted. So the size is
/// read from the header first and absurd images are simply never decoded.
///
/// Only PNG and JPEG are understood, which is exactly what the thumbnail
/// provider produces. Anything else is reported as unreadable rather than
/// waved through, so an unexpected format cannot slip past this check.
/// </summary>
public static class RasterImageHeader
{
    /// <summary>
    /// The largest image worth decoding, in pixels. A 600 dpi A3 scan is about
    /// 70 megapixels, so this admits any plausible scanned page while capping
    /// the transient decode at roughly 320 MB at 32 bits per pixel.
    /// </summary>
    public const long MaxPixelCount = 80_000_000;

    /// <summary>
    /// Pixel dimensions declared by the header, or null when the bytes are not
    /// a PNG or JPEG we can read.
    /// </summary>
    public static (int Width, int Height)? TryReadDimensions(ReadOnlySpan<byte> bytes) =>
        IsPng(bytes) ? ReadPngDimensions(bytes)
        : IsJpeg(bytes) ? ReadJpegDimensions(bytes)
        : null;

    /// <summary>
    /// True when these bytes are a readable image of a sane size. False for
    /// anything unrecognised, truncated, zero-sized, or over
    /// <see cref="MaxPixelCount"/>.
    /// </summary>
    public static bool IsSafeToDecode(ReadOnlySpan<byte> bytes)
    {
        if (TryReadDimensions(bytes) is not var (width, height)) return false;
        if (width <= 0 || height <= 0) return false;

        // Multiply as long: two int dimensions can overflow an int product,
        // and an overflow that wraps to a small number would pass the check.
        return (long)width * height <= MaxPixelCount;
    }

    static bool IsPng(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 24 && bytes[..8].SequenceEqual(stackalloc byte[]
        {
            0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A,
        });

    static (int Width, int Height)? ReadPngDimensions(ReadOnlySpan<byte> bytes)
    {
        // The IHDR chunk is mandatory and must come first: an 8-byte signature,
        // a 4-byte length, the type "IHDR", then width and height as
        // big-endian 32-bit values.
        if (!bytes.Slice(12, 4).SequenceEqual("IHDR"u8)) return null;
        return (ReadBigEndianInt32(bytes[16..]), ReadBigEndianInt32(bytes[20..]));
    }

    static bool IsJpeg(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xD8;

    static (int Width, int Height)? ReadJpegDimensions(ReadOnlySpan<byte> bytes)
    {
        // Walk the marker segments looking for a start-of-frame, which is the
        // only place the dimensions appear. Everything else is skipped by its
        // declared length.
        int position = 2;
        while (position + 4 <= bytes.Length)
        {
            // Markers are 0xFF followed by a type; padding 0xFF bytes are legal.
            if (bytes[position] != 0xFF) return null;
            byte marker = bytes[position + 1];
            position += 2;
            if (marker == 0xFF) { position--; continue; }

            // Standalone markers carry no payload.
            if (marker is 0x01 or (>= 0xD0 and <= 0xD9)) continue;

            if (position + 2 > bytes.Length) return null;
            int length = (bytes[position] << 8) | bytes[position + 1];
            if (length < 2 || position + length > bytes.Length) return null;

            // SOF0-SOF15 hold the frame size, except DHT (C4), JPG (C8) and
            // DAC (CC), which share the range but are not frame headers.
            if (marker is >= 0xC0 and <= 0xCF && marker is not (0xC4 or 0xC8 or 0xCC))
            {
                // Payload: length(2) precision(1) height(2) width(2).
                if (length < 7) return null;
                int height = (bytes[position + 3] << 8) | bytes[position + 4];
                int width = (bytes[position + 5] << 8) | bytes[position + 6];
                return (width, height);
            }

            position += length;
        }

        return null;
    }

    static int ReadBigEndianInt32(ReadOnlySpan<byte> bytes) =>
        (bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3];
}
