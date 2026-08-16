using System.Globalization;
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
        var commandLine = CommandLine.Parse(args);
        if (commandLine.Error is not null)
        {
            WriteToTheCallingConsole(commandLine.Error + Environment.NewLine
                                     + ScreenshotViews.Describe());
            return;
        }
        if (commandLine.ListViews)
        {
            WriteToTheCallingConsole(ScreenshotViews.Describe());
            return;
        }

        // Before anything reads a translated string: L10n resolves the language
        // once, on first use, and never again.
        if (commandLine.Language is not null)
        {
            CultureInfo.CurrentUICulture = commandLine.Language;
            CultureInfo.DefaultThreadCurrentUICulture = commandLine.Language;
        }

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
            // The cleaner needs a rasterizer to flatten an overlap into an
            // image; the only one available without shipping a native binary is
            // the operating system's own, which is why it is injected from here
            // rather than built inside Infrastructure (that layer keeps
            // building and testing on macOS).
            new PdfSharpDocumentCleaner(new WindowsPageRasterizer(), new WindowsImageResampler()),
            new PdfSharpDocumentVerifier(),
            thumbnailStore,
            logger)
        {
            // What the user last chose in the settings window, or the defaults
            // for a first run. Read here rather than inside the workflow so the
            // one place that talks to disk for preferences stays the App shell.
            ImageReduction = SettingsStore.Load(),
        };

        try
        {
            Application.Run(new MainForm(
                workflow, thumbnailStore, logger,
                commandLine.PdfPaths, commandLine.Screenshot));
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
    /// Print to the console that started this process, if there was one.
    ///
    /// A desktop application has no console of its own: its output goes
    /// nowhere, which for <c>--list-views</c> means the answer is lost. So it
    /// borrows the caller's — and if there is no caller (started from Explorer),
    /// nothing is printed and nothing breaks.
    /// </summary>
    static void WriteToTheCallingConsole(string text)
    {
        const int ParentProcess = -1;
        if (!AttachConsole(ParentProcess)) return;

        try
        {
            using var output = Console.OpenStandardOutput();
            var bytes = System.Text.Encoding.UTF8.GetBytes(Environment.NewLine + text);
            output.Write(bytes, 0, bytes.Length);
        }
        finally
        {
            FreeConsole();
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool AttachConsole(int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool FreeConsole();

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
