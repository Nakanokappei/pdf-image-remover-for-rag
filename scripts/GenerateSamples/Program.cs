// GenerateSamples — console entry point that regenerates the spec §8.2
// sample PDFs into ./samples (or a directory given as the first argument).
// All generation logic lives in SamplePdfWriter, which is shared with the
// Infrastructure integration-test fixture.

using PdfImageRemoverForRag.Scripts.GenerateSamples;

// Resolve output directory. Default: repo/samples, relative to the built binary.
var outDir = args.Length > 0
    ? args[0]
    : Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples");
outDir = Path.GetFullPath(outDir);
Console.WriteLine($"[GenerateSamples] output = {outDir}");

foreach (var path in SamplePdfWriter.WriteAll(outDir))
{
    Console.WriteLine($"  wrote {Path.GetFileName(path)}  ({new FileInfo(path).Length} bytes)");
}

Console.WriteLine("[GenerateSamples] done");
return 0;
