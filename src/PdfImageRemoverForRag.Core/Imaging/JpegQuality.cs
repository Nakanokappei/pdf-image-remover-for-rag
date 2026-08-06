namespace PdfImageRemoverForRag.Core.Imaging;

/// <summary>
/// What quality a JPEG was written at, read back from the file.
///
/// Needed because re-encoding at a fixed quality can make a file BIGGER: a
/// photo saved at 60 and written out again at 85 grows, and nothing is gained —
/// the detail it lost is not coming back. So a picture is only ever encoded at
/// the lower of its own quality and the ceiling.
///
/// The quantization table is the record of it. An encoder builds that table by
/// scaling a standard one by a factor derived from the quality number, so the
/// factor — and with it the quality — can be recovered by comparing the two.
/// The comparison here is of their SUMS rather than entry by entry: the sum
/// moves with the scale factor just as monotonically, and it does not depend on
/// the order the entries are stored in.
///
/// Approximate for a file some other encoder wrote with tables of its own. It is
/// used to answer "is this above the ceiling", where being a point or two out
/// changes nothing.
/// </summary>
public static class JpegQuality
{
    /// <summary>
    /// The luminance quantization table the JPEG standard suggests, which is
    /// what encoders scale. Only its total is ever used.
    /// </summary>
    static readonly int[] StandardLuminanceTable =
    {
        16, 11, 10, 16, 24, 40, 51, 61,
        12, 12, 14, 19, 26, 58, 60, 55,
        14, 13, 16, 24, 40, 57, 69, 56,
        14, 17, 22, 29, 51, 87, 80, 62,
        18, 22, 37, 56, 68, 109, 103, 77,
        24, 35, 55, 64, 81, 104, 113, 92,
        49, 64, 78, 87, 103, 121, 120, 101,
        72, 92, 95, 98, 112, 100, 103, 99,
    };

    /// <summary>
    /// The quality <paramref name="jpeg"/> was written at, or null when the
    /// bytes are not a JPEG this can read. A caller that gets null should leave
    /// the image alone rather than guess.
    /// </summary>
    public static int? Estimate(byte[] jpeg)
    {
        var table = FirstQuantizationTable(jpeg);
        if (table is null) return null;

        long total = table.Sum();
        if (total <= 0) return null;

        // The quality whose table this is closest to. A hundred candidates is a
        // small enough search to write plainly.
        int best = 1;
        long bestDistance = long.MaxValue;
        for (int quality = 1; quality <= 100; quality++)
        {
            long distance = Math.Abs(TotalAtQuality(quality) - total);
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            best = quality;
        }
        return best;
    }

    /// <summary>What the standard table adds up to once scaled for a quality.</summary>
    static long TotalAtQuality(int quality)
    {
        // The scaling every IJG-derived encoder uses.
        int scale = quality < 50 ? 5000 / quality : 200 - (quality * 2);
        long total = 0;
        foreach (int entry in StandardLuminanceTable)
        {
            total += Math.Clamp(((entry * scale) + 50) / 100, 1, 255);
        }
        return total;
    }

    /// <summary>
    /// The first quantization table in the file: 64 entries, in whatever order
    /// they are stored, which is all the sum needs.
    /// </summary>
    static int[]? FirstQuantizationTable(byte[] jpeg)
    {
        // A JPEG is a chain of marker segments after the start-of-image marker.
        if (jpeg.Length < 4 || jpeg[0] != 0xFF || jpeg[1] != 0xD8) return null;

        int position = 2;
        while (position + 4 <= jpeg.Length)
        {
            // Markers may be padded with fill bytes; skip to the identifier.
            if (jpeg[position] != 0xFF) { position++; continue; }
            byte marker = jpeg[position + 1];
            position += 2;
            if (marker == 0xFF) { position--; continue; }

            // Standalone markers carry no length, and the scan is the end of
            // anything this needs to read.
            if (marker == 0xD8 || (marker >= 0xD0 && marker <= 0xD7)) continue;
            if (marker == 0xDA || marker == 0xD9) return null;

            if (position + 2 > jpeg.Length) return null;
            int length = (jpeg[position] << 8) | jpeg[position + 1];
            if (length < 2 || position + length > jpeg.Length) return null;

            if (marker == 0xDB)
            {
                var table = ReadQuantizationTable(jpeg, position + 2, position + length);
                if (table is not null) return table;
            }
            position += length;
        }
        return null;
    }

    /// <summary>
    /// One table out of a DQT segment. Its first byte says which table it is and
    /// whether the entries are one byte or two; the entries follow.
    /// </summary>
    static int[]? ReadQuantizationTable(byte[] jpeg, int start, int end)
    {
        if (start >= end) return null;
        bool sixteenBit = (jpeg[start] >> 4) != 0;
        int at = start + 1;
        int needed = sixteenBit ? 128 : 64;
        if (at + needed > end) return null;

        var table = new int[64];
        for (int i = 0; i < 64; i++)
        {
            table[i] = sixteenBit
                ? (jpeg[at + (i * 2)] << 8) | jpeg[at + (i * 2) + 1]
                : jpeg[at + i];
        }
        return table;
    }
}
