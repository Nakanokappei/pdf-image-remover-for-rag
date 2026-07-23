using PdfImageRemoverForRag.Core.Hashing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;

namespace PdfImageRemoverForRag.Infrastructure.Internal;

/// <summary>
/// Single home for every "/XObject dictionary → Image XObjects" walk in the
/// Infrastructure layer. The analyzer, cleaner, and verifier all resolve
/// resource entries through this class so name resolution, reference
/// dereferencing, and stream hashing behave identically everywhere.
/// </summary>
internal static class ImageXObjectCollector
{
    internal readonly record struct ImageEntry(
        string ResourceName, PdfDictionary Dictionary, string ObjectId);

    internal readonly record struct FormXObject(
        string ResourceName, PdfDictionary Dictionary, string ObjectId);

    internal readonly record struct FormEmbeddedImage(
        string ResourceName, PdfDictionary Dictionary, string ObjectId,
        string ContainingFormObjectId);

    /// <summary>
    /// Enumerate every direct Image XObject reachable from a page's
    /// resources. This is the shared primitive the cleaner and verifier use
    /// to map object ids / stream hashes back to resource names.
    /// </summary>
    public static IEnumerable<ImageEntry> EnumerateImageEntries(PdfResources? resources)
    {
        if (resources is null) yield break;
        var xObjects = resources.Elements.GetDictionary("/XObject");
        if (xObjects is null) yield break;

        foreach (var kv in xObjects.Elements)
        {
            var dict = ResolveDictionary(kv.Value);
            if (dict?.Elements.GetName("/Subtype") != "/Image") continue;
            yield return new ImageEntry(kv.Key, dict, dict.Internals.ObjectID.ToString());
        }
    }

    /// <summary>
    /// SHA-256 (uppercase hex) of the raw filtered stream — the group key
    /// used across the whole app. Centralised here so the analyzer, cleaner,
    /// and verifier can never drift on how a stream is hashed.
    /// </summary>
    public static string ComputeStreamHash(PdfDictionary imageDict) =>
        StreamHasher.Sha256Hex(imageDict.Stream?.Value ?? Array.Empty<byte>());

    /// <summary>
    /// Split a page's /XObject entries into direct images and Form XObjects.
    /// Forms may contain more images that must be surfaced as "unsafe to
    /// delete" (§14.3), which <see cref="CollectImagesInsideForm"/> resolves.
    /// </summary>
    public static (List<ImageEntry> Direct, List<FormXObject> Forms) CollectDirect(PdfResources? resources)
    {
        var images = new List<ImageEntry>();
        var forms = new List<FormXObject>();
        if (resources is null) return (images, forms);
        var xObjects = resources.Elements.GetDictionary("/XObject");
        if (xObjects is null) return (images, forms);

        foreach (var kv in xObjects.Elements)
        {
            var name = kv.Key; // includes leading '/'
            var dict = ResolveDictionary(kv.Value);
            if (dict is null) continue;

            var subtype = dict.Elements.GetName("/Subtype");
            var objectId = dict.Internals.ObjectID.ToString();

            if (subtype == "/Image") images.Add(new ImageEntry(name, dict, objectId));
            else if (subtype == "/Form") forms.Add(new FormXObject(name, dict, objectId));
        }

        return (images, forms);
    }

    /// <summary>
    /// Recursively walk a Form XObject and collect every Image XObject found
    /// inside it. A cycle guard is required because PDFs can build arbitrary
    /// object graphs.
    /// </summary>
    public static List<FormEmbeddedImage> CollectImagesInsideForm(PdfDictionary formDict)
    {
        var sink = new List<FormEmbeddedImage>();
        var visited = new HashSet<PdfObjectID> { formDict.Internals.ObjectID };
        WalkForm(formDict, formDict.Internals.ObjectID.ToString(), sink, visited);
        return sink;
    }

    static void WalkForm(PdfDictionary formDict, string rootFormObjectId,
        List<FormEmbeddedImage> sink, HashSet<PdfObjectID> visited)
    {
        var resources = ResolveDictionary(formDict.Elements["/Resources"]);
        if (resources is null) return;
        var xObjects = resources.Elements.GetDictionary("/XObject");
        if (xObjects is null) return;

        foreach (var kv in xObjects.Elements)
        {
            var dict = ResolveDictionary(kv.Value);
            if (dict is null) continue;
            var subtype = dict.Elements.GetName("/Subtype");

            if (subtype == "/Image")
            {
                sink.Add(new FormEmbeddedImage(
                    kv.Key, dict, dict.Internals.ObjectID.ToString(), rootFormObjectId));
            }
            else if (subtype == "/Form")
            {
                if (!visited.Add(dict.Internals.ObjectID)) continue; // cycle guard
                WalkForm(dict, rootFormObjectId, sink, visited);
            }
        }
    }

    /// <summary>
    /// Follow one level of indirection: /XObject entries are usually
    /// <see cref="PdfReference"/>s to the actual stream dictionary.
    /// </summary>
    static PdfDictionary? ResolveDictionary(PdfItem? item) => item switch
    {
        PdfDictionary d => d,
        PdfReference r => r.Value as PdfDictionary,
        _ => null,
    };
}
