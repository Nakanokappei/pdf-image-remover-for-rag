using PdfImageRemoverForRag.Core.Abstractions;
using PdfImageRemoverForRag.Core.Models;
using PdfImageRemoverForRag.Scripts.GenerateSamples;

namespace PdfImageRemoverForRag.Infrastructure.Tests;

/// <summary>
/// Renders every region as one flat colour, and records what it was asked for so
/// the request itself can be asserted on.
/// </summary>
/// <remarks>
/// Standing in for the OS renderer is not a shortcut around it — it is the reason
/// <see cref="IPageRasterizer"/> is an interface in Core. What can go wrong in
/// the rewrite (what gets deleted, what gets drawn, where) is answerable on any
/// operating system; whether the pixels look right is a question for the machine
/// that has the renderer.
/// </remarks>
internal sealed class FlatColourRasterizer : IPageRasterizer
{
    readonly bool _succeeds;

    public FlatColourRasterizer(bool succeeds = true)
    {
        _succeeds = succeeds;
    }

    public List<(int PageNumber, PageRegion Region, int Dpi)> Requests { get; } = new();

    /// <summary>The files it was asked to render, in order — flattening renders
    /// from a copy holding only the ticked objects, and that copy is what a
    /// test has to be able to look at.</summary>
    public List<string> RenderedFiles { get; } = new();

    /// <summary>Whether the caller asked for a transparent background.</summary>
    public List<bool> Transparency { get; } = new();

    /// <summary>
    /// What the page it was pointed at actually draws, read at the moment of
    /// the call — the caller deletes its copy straight afterwards, so a test
    /// cannot go back and look.
    /// </summary>
    public List<(string Text, int Images)> RenderedContent { get; } = new();

    static (string Text, int Images) ReadContent(string path, int pageNumber)
    {
        try
        {
            using var document = UglyToad.PdfPig.PdfDocument.Open(path);
            var page = document.GetPage(pageNumber);
            return (page.Text, page.GetImages().Count());
        }
        catch
        {
            return (string.Empty, 0);
        }
    }

    public Task<byte[]?> RenderRegionAsync(
        string pdfFilePath, int pageNumber, PageRegion region, int targetDpi,
        bool transparentBackground = false,
        CancellationToken ct = default)
    {
        Requests.Add((pageNumber, region, targetDpi));
        RenderedFiles.Add(pdfFilePath);
        Transparency.Add(transparentBackground);
        RenderedContent.Add(ReadContent(pdfFilePath, pageNumber));
        if (!_succeeds) return Task.FromResult<byte[]?>(null);

        // Pixel dimensions in proportion to the region, so a wrong aspect ratio
        // in the caller would show up as a distorted placement.
        int width = Math.Max(1, (int)Math.Round(region.Width));
        int height = Math.Max(1, (int)Math.Round(region.Height));
        var rgb = new byte[width * height * 3];
        for (int i = 0; i < rgb.Length; i += 3)
        {
            rgb[i] = 0x40;
            rgb[i + 1] = 0x80;
            rgb[i + 2] = 0xC0;
        }
        return Task.FromResult<byte[]?>(MinimalPng.EncodeRgb(width, height, rgb));
    }
}
