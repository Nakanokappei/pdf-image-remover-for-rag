using PdfImageRemoverForRag.Core.Grouping;
using PdfImageRemoverForRag.Core.Models;

namespace PdfImageRemoverForRag.Core.Tests;

/// <summary>
/// The <see cref="ImageDiscovery"/> records the grouping tests are built from.
///
/// One home for them because <see cref="ImageDiscovery"/> is a positional
/// record that grows: the run that added drawings appended a parameter, and
/// every hand-written constructor in every test class had to be found again.
/// Here there is one to find.
/// </summary>
internal static class Discoveries
{
    /// <summary>An image, identified by the stream hash it is given.</summary>
    public static ImageDiscovery Image(string hash, int usage = 1)
    {
        var occurrences = Enumerable.Range(1, usage)
            .Select(page => new PdfImageOccurrence(page, "1 0 R", "/Im1", 0, 0, 100, 60)).ToArray();
        return new ImageDiscovery("1 0 R", hash, 100, 60, "/DeviceRGB", 8, "/FlateDecode",
            1000, false, true, null, null, occurrences);
    }

    /// <summary>A shown string. Its hash is derived from the value, as the analyzer does.</summary>
    public static ImageDiscovery Text(string value, int usage = 2)
    {
        var occurrences = Enumerable.Range(1, usage)
            .Select(page => new PdfImageOccurrence(page, "", "", 0, 0, 0, 0)).ToArray();
        return new ImageDiscovery("", "TEXT:" + value, 0, 0, "Text", 0, "Text",
            value.Length, false, true, null, null, occurrences,
            RemovableKind.Text, value);
    }

    /// <summary>A vector path, identified by its position-independent signature.</summary>
    public static ImageDiscovery Shape(string signature, int usage = 1)
    {
        var occurrences = Enumerable.Range(1, usage)
            .Select(page => new PdfImageOccurrence(page, "", "", 40, 80, 460, 680)).ToArray();
        return new ImageDiscovery("", "SHAPE:" + signature, 460, 680, "Shape", 0, "Shape",
            0, false, true, null, null, occurrences,
            RemovableKind.Shape, signature);
    }

    /// <summary>
    /// One placement of a form's artwork. One occurrence per call: merging two
    /// of these is what "the same icon on two pages" has to produce, so the
    /// helper must not do the merging itself.
    /// </summary>
    public static ImageDiscovery Drawing(string formHash, int page, DrawingGeometry? geometry = null)
    {
        var occurrence = new PdfImageOccurrence(page, "217 0 R", "/Meta217", 60, 600, 120, 120);
        return new ImageDiscovery("217 0 R", formHash, 120, 120, "Drawing", 0, "Drawing",
            3824, false, true, null, null, new[] { occurrence },
            RemovableKind.Drawing, null, null, geometry);
    }

    /// <summary>A builder over three A4 pages, which is enough for every case here.</summary>
    public static ImageGroupBuilder NewBuilder() =>
        new(new FullPageImageDetector(new[]
        {
            new PageDimensions(1, 595, 842),
            new PageDimensions(2, 595, 842),
            new PageDimensions(3, 595, 842),
        }));
}
