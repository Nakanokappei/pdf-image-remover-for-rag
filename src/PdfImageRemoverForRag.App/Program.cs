using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using PdfImageRemoverForRag.Infrastructure;

namespace PdfImageRemoverForRag.App;

/// <summary>
/// Composition root: wires logging (spec §19) and the Infrastructure
/// implementations into the workflow, then runs the main form.
/// </summary>
internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        var logFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PdfImageRemoverForRag", "logs", "PdfImageRemoverForRag.log");
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddProvider(new FileLoggerProvider(logFilePath));
        });
        var logger = loggerFactory.CreateLogger("PdfImageRemoverForRag");
        LogEnvironment(logger);

        // Cache folders from runs that died without cleaning up hold full-size
        // image data, so they are swept before this run starts adding its own.
        ThumbnailStore.RemoveAbandonedSessions();
        using var thumbnailStore = new ThumbnailStore();
        logger.LogInformation("thumbnail store: folder={Folder}", thumbnailStore.Folder);

        var workflow = new PdfCleaningWorkflow(
            new PdfSharpDocumentAnalyzer(new PdfPigThumbnailProvider()),
            new PdfSharpDocumentCleaner(),
            new PdfSharpDocumentVerifier(),
            thumbnailStore,
            logger);

        try
        {
            Application.Run(new MainForm(workflow, thumbnailStore, logger, PdfPathsFrom(args)));
        }
        catch (Exception ex)
        {
            // Last-resort handler: log before the process dies so the crash
            // is diagnosable from the log file alone.
            logger.LogCritical(ex, "unhandled exception — application terminating");
            throw;
        }
    }

    /// <summary>
    /// The PDF paths to open at startup, taken from the command line.
    ///
    /// This is what makes "drop a PDF onto the app's icon" work: Explorer
    /// launches the exe with the dropped paths as arguments. The app
    /// deliberately does NOT declare a file-type association — it is not a PDF
    /// viewer, so it should not compete to become the default handler for .pdf.
    ///
    /// Anything that is not an existing .pdf file is dropped silently: the user
    /// dropped it on the wrong app, and an error dialog before the window has
    /// even appeared would be worse than simply starting empty.
    /// </summary>
    static IReadOnlyList<string> PdfPathsFrom(string[] args) => args
        .Where(a => string.Equals(Path.GetExtension(a), ".pdf", StringComparison.OrdinalIgnoreCase))
        .Where(File.Exists)
        .ToArray();

    /// <summary>Log the §19 environment block once per session.</summary>
    static void LogEnvironment(ILogger logger)
    {
        logger.LogInformation(
            "startup: appVersion={AppVersion} os={Os} dotnet={Dotnet} cpuArch={CpuArch}",
            AppVersion.Display,
            RuntimeInformation.OSDescription,
            Environment.Version,
            RuntimeInformation.ProcessArchitecture);
    }
}
