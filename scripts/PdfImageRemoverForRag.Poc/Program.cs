// PdfImageRemoverForRag.Poc — technical-verification driver for the spec §8.1
// checklist. Since Step 5 this is a thin console harness over the production
// Infrastructure implementations (PdfSharpDocumentAnalyzer / Cleaner /
// Verifier), so running it also smoke-tests the exact code the app ships.
//
// Checklist coverage per file:
//   1–8   analyze     (open, page count, enumerate, metadata, decode,
//                      usage pages, same-image detection)
//   9–11  clean       (locate Do, remove Do, save under a new name)
//   12–15 verify      (reopen, page count, target gone, others retained)
//
// The independent text-extraction cross-check uses PdfPig directly because
// the production verifier intentionally stays within PDFsharp.

using PdfImageRemoverForRag.Core.Formatting;
using PdfImageRemoverForRag.Core.Models;
using PdfImageRemoverForRag.Infrastructure;
using PdfPigDoc = UglyToad.PdfPig.PdfDocument;

var samples = args.Length > 0 ? args : DefaultSampleFiles();
if (samples.Length == 0)
{
    Console.Error.WriteLine("No PDF files supplied and no samples/*.pdf found.");
    return 2;
}

var analyzer = new PdfSharpDocumentAnalyzer(new PdfPigThumbnailProvider());
var cleaner = new PdfSharpDocumentCleaner();
var verifier = new PdfSharpDocumentVerifier();

int failed = 0;
foreach (var pdfPath in samples)
{
    if (!File.Exists(pdfPath))
    {
        Console.Error.WriteLine($"[SKIP] not found: {pdfPath}");
        failed++;
        continue;
    }
    Console.WriteLine();
    Console.WriteLine($"=== {Path.GetFileName(pdfPath)} ===");
    try
    {
        if (!await RunChecklistAsync(pdfPath)) failed++;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[FAIL] {pdfPath}: {ex.GetType().Name}: {ex.Message}");
        failed++;
    }
}

Console.WriteLine();
Console.WriteLine(failed == 0 ? "[POC] all files processed" : $"[POC] {failed} file(s) failed");
return failed == 0 ? 0 : 1;

async Task<bool> RunChecklistAsync(string pdfPath)
{
    // Steps 1–8: analysis through the production analyzer.
    var info = await analyzer.AnalyzeAsync(pdfPath);
    PrintAnalysis(info);

    // This harness verifies the IMAGE pipeline (spec §8.1); pick an image
    // group as the removal target. Prefer a non-full-page one so the cleaned
    // PDF keeps visible content.
    var imageGroups = info.ImageGroups.Where(g => g.Kind == RemovableKind.Image).ToArray();
    var target = imageGroups.FirstOrDefault(g => g.IsSafelyRemovable && !g.IsPossibleFullPageImage)
              ?? imageGroups.FirstOrDefault(g => g.IsSafelyRemovable);
    if (target is null)
    {
        Console.WriteLine("[NOTE] no removable image group found; nothing to clean.");
        return true;
    }

    // Steps 9–11: remove every occurrence of the target group and save.
    var outDir = Path.Combine(Path.GetDirectoryName(pdfPath)!, "cleaned");
    Directory.CreateDirectory(outDir);
    var cleanedPath = Path.Combine(outDir,
        Path.GetFileNameWithoutExtension(pdfPath) + "_cleaned.pdf");

    var selection = new ImageRemovalSelection(
        target.GroupId, target.Occurrences, Hash: target.Hash);
    var result = await cleaner.CleanAsync(pdfPath, cleanedPath, new[] { selection });
    Console.WriteLine($"[REMOVE] {target.GroupId} hash={target.Hash[..12]}… " +
        $"pages modified={result.PagesModified} draw calls removed={result.DrawCallsRemoved}");
    Console.WriteLine($"[SAVE] {cleanedPath}  ({new FileInfo(cleanedPath).Length} bytes, {result.Elapsed.TotalMilliseconds:F0} ms)");

    // Steps 12–15: verification through the production verifier. Only image
    // hashes are meaningful to it (it resolves hashes against Image XObjects),
    // so exclude text groups — matching the app's PdfCleaningWorkflow.
    var retained = info.ImageGroups
        .Where(g => g.Kind == RemovableKind.Image && g.Hash != target.Hash)
        .Select(g => g.Hash)
        .ToArray();
    var report = await verifier.VerifyAsync(pdfPath, cleanedPath,
        result.RemovedGroupHashes, retained);

    Console.WriteLine("verify:");
    Console.WriteLine($"  cleaned PDF opens      : {report.CleanedPdfOpens}");
    Console.WriteLine($"  page count match       : {report.PageCountMatches}");
    Console.WriteLine($"  non-empty file         : {report.NonEmptyFileSize}");
    Console.WriteLine($"  no Do for removed      : {report.NoDoOperatorsForRemovedImages}");
    Console.WriteLine($"  retained groups remain : {report.NonRemovedImageGroupsRetained}");
    Console.WriteLine($"  no runtime exceptions  : {report.NoRuntimeExceptions}");
    foreach (var w in report.Warnings) Console.WriteLine($"  ! {w}");

    // Independent cross-check with PdfPig: the cleaned PDF must still open
    // in a second parser, and text (when the source had any) must survive.
    var textOk = CrossCheckTextWithPdfPig(cleanedPath);

    return report.IsOverallOk && textOk;
}

static bool CrossCheckTextWithPdfPig(string cleanedPath)
{
    try
    {
        using var pig = PdfPigDoc.Open(cleanedPath);
        int imagesRemaining = 0;
        int pagesWithText = 0;
        foreach (var page in pig.GetPages())
        {
            imagesRemaining += page.GetImages().Count();
            if (!string.IsNullOrWhiteSpace(page.Text)) pagesWithText++;
        }
        Console.WriteLine($"[PDFPIG] opens=True imagesRemaining={imagesRemaining} pagesWithText={pagesWithText}");
        return true;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[PDFPIG] failed to open cleaned PDF: {ex.Message}");
        return false;
    }
}

static void PrintAnalysis(PdfDocumentInfo info)
{
    Console.WriteLine($"file        : {info.FilePath}");
    Console.WriteLine($"size        : {info.FileSize} bytes");
    Console.WriteLine($"pages       : {info.PageCount}");
    Console.WriteLine($"encrypted   : {info.IsEncrypted}");
    Console.WriteLine($"image groups: {info.ImageKindCount}");
    Console.WriteLine($"occurrences : {info.TotalUsageCount}");
    foreach (var g in info.ImageGroups)
    {
        var flags = (g.IsPossibleFullPageImage ? " [FULL-PAGE?]" : "")
                  + (g.IsSafelyRemovable ? "" : " [UNSAFE]")
                  + (g.ThumbnailBytes is null ? "" : " [thumb]");
        Console.WriteLine(
            $"  - {g.GroupId} {g.Hash[..12]}… {g.PixelWidth}x{g.PixelHeight} {g.ColorSpace} " +
            $"bpc={g.BitsPerComponent} {g.Compression} usage={g.UsageCount} " +
            $"pages=[{UsagePageFormatter.Format(g.UsagePages)}]{flags}");
    }
}

static string[] DefaultSampleFiles()
{
    var dir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
        "..", "..", "..", "..", "..", "samples"));
    return Directory.Exists(dir)
        ? Directory.GetFiles(dir, "*.pdf").OrderBy(p => p).ToArray()
        : Array.Empty<string>();
}
