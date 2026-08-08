using PdfImageRemoverForRag.Core.Grouping;
using PdfImageRemoverForRag.Core.Models;

namespace PdfImageRemoverForRag.Core.Tests;

/// <summary>
/// The <see cref="ObjectDiscovery"/> records the grouping tests are built from.
///
/// One home for them because <see cref="ObjectDiscovery"/> is a positional
/// record that grows: the run that added drawings appended a parameter, and
/// every hand-written constructor in every test class had to be found again.
/// Here there is one to find.
/// </summary>
internal static class Discoveries
{
    /// <summary>An image, identified by the stream hash it is given.</summary>
    public static ObjectDiscovery Image(string hash, int usage = 1)
    {
        var occurrences = Enumerable.Range(1, usage)
            .Select(page => new ObjectOccurrence(page, "1 0 R", "/Im1", 0, 0, 100, 60)).ToArray();
        return new ObjectDiscovery("1 0 R", hash, 100, 60, "/DeviceRGB", 8, "/FlateDecode",
            1000, false, true, null, null, occurrences);
    }

    /// <summary>
    /// A shadow layer. An Image XObject like <see cref="Image"/> — same stream
    /// hash identity, same removal path — so the only thing that differs here
    /// is the kind the analyzer stamped on it.
    /// </summary>
    public static ObjectDiscovery Shadow(string hash, int usage = 1)
    {
        var occurrences = Enumerable.Range(1, usage)
            .Select(page => new ObjectOccurrence(page, "1 0 R", "/ImShadow", 0, 0, 120, 80)).ToArray();
        return new ObjectDiscovery("1 0 R", hash, 244, 152, "/DeviceRGB", 8, "/FlateDecode",
            420, false, true, null, null, occurrences,
            RemovableKind.Shadow);
    }

    /// <summary>A shown string. Its hash is derived from the value, as the analyzer does.</summary>
    public static ObjectDiscovery Text(string value, int usage = 2)
    {
        var occurrences = Enumerable.Range(1, usage)
            .Select(page => new ObjectOccurrence(page, "", "", 0, 0, 0, 0)).ToArray();
        return new ObjectDiscovery("", "TEXT:" + value, 0, 0, "Text", 0, "Text",
            value.Length, false, true, null, null, occurrences,
            RemovableKind.Text, value);
    }

    /// <summary>A vector path, identified by its position-independent signature.</summary>
    public static ObjectDiscovery Shape(string signature, int usage = 1)
    {
        var occurrences = Enumerable.Range(1, usage)
            .Select(page => new ObjectOccurrence(page, "", "", 40, 80, 460, 680)).ToArray();
        return new ObjectDiscovery("", "SHAPE:" + signature, 460, 680, "Shape", 0, "Shape",
            0, false, true, null, null, occurrences,
            RemovableKind.Shape, signature);
    }

    /// <summary>
    /// One placement of a form's artwork. One occurrence per call: merging two
    /// of these is what "the same icon on two pages" has to produce, so the
    /// helper must not do the merging itself.
    /// </summary>
    public static ObjectDiscovery Drawing(string formHash, int page, DrawingGeometry? geometry = null)
    {
        var occurrence = new ObjectOccurrence(page, "217 0 R", "/Meta217", 60, 600, 120, 120);
        return new ObjectDiscovery("217 0 R", formHash, 120, 120, "Drawing", 0, "Drawing",
            3824, false, true, null, null, new[] { occurrence },
            RemovableKind.Drawing, null, null, geometry);
    }

    /// <summary>A builder over three A4 pages, which is enough for every case here.</summary>
    public static ObjectGroupBuilder NewBuilder() =>
        new(new FullPageImageDetector(new[]
        {
            new PageDimensions(1, 595, 842),
            new PageDimensions(2, 595, 842),
            new PageDimensions(3, 595, 842),
        }));
}
