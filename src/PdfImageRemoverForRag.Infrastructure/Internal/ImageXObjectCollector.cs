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
    public static IEnumerable<ImageEntry> EnumerateImageEntries(PdfResources? resources) =>
        EnumerateEntries(resources, "/Image")
            .Select(e => new ImageEntry(e.Name, e.Dictionary, e.ObjectId));

    /// <summary>
    /// The one walk of a page's <c>/XObject</c> dictionary. Both entry kinds go
    /// through it so name resolution and reference dereferencing can only be
    /// got right or wrong once — this class exists to keep that single.
    /// </summary>
    static IEnumerable<(string Name, PdfDictionary Dictionary, string ObjectId)> EnumerateEntries(
        PdfResources? resources, string subtype)
    {
        if (resources is null) yield break;
        var xObjects = resources.Elements.GetDictionary("/XObject");
        if (xObjects is null) yield break;

        foreach (var kv in xObjects.Elements)
        {
            var dict = ResolveDictionary(kv.Value);
            if (dict?.Elements.GetName("/Subtype") != subtype) continue;
            yield return (kv.Key, dict, dict.Internals.ObjectID.ToString());
        }
    }

    /// <summary>
    /// Enumerate every Form XObject named directly in a page's resources — the
    /// counterpart of <see cref="EnumerateImageEntries"/> for the forms whose
    /// artwork is a <see cref="RemovableKind.Drawing"/>. The cleaner and the
    /// verifier both need to get from a selected stream hash back to the name
    /// the page uses, and they must do it the same way.
    /// </summary>
    public static IEnumerable<FormXObject> EnumerateFormEntries(PdfResources? resources) =>
        EnumerateEntries(resources, "/Form")
            .Select(e => new FormXObject(e.Name, e.Dictionary, e.ObjectId));

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
    /// <summary>
    /// Drop entries from a page's <c>/XObject</c> resources by name — the one
    /// write this class offers, kept here with the reads so both sides of the
    /// dictionary are handled the same way.
    ///
    /// The names must be collected before calling: a dictionary cannot be
    /// modified while its entries are being enumerated.
    /// </summary>
    public static void RemoveEntries(PdfResources? resources, IReadOnlyCollection<string> names)
    {
        if (names.Count == 0) return;
        var xObjects = resources?.Elements.GetDictionary("/XObject");
        if (xObjects is null) return;

        foreach (var name in names) xObjects.Elements.Remove(name);
    }

    /// <summary>
    /// Follow an indirect reference to the dictionary behind it. Centralised so
    /// every caller resolves references identically — the reason this class
    /// exists at all.
    /// </summary>
    public static PdfDictionary? ResolveDictionary(PdfItem? item) => item switch
    {
        PdfDictionary d => d,
        PdfReference r => r.Value as PdfDictionary,
        _ => null,
    };
}
