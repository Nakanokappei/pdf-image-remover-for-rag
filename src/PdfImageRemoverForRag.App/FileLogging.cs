using Microsoft.Extensions.Logging;

namespace PdfImageRemoverForRag.App;

/// <summary>
/// Minimal Microsoft.Extensions.Logging provider that appends one line per
/// entry to a single log file (spec §19). Deliberately tiny: no rolling, no
/// buffering — the app writes a handful of lines per session, so open-append-
/// close per write is robust and keeps the file readable even after a crash.
/// </summary>
internal sealed class FileLoggerProvider : ILoggerProvider
{
    readonly string _logFilePath;
    readonly object _writeLock = new();

    public FileLoggerProvider(string logFilePath)
    {
        _logFilePath = logFilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(logFilePath)!);
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void Dispose()
    {
        // Nothing held open between writes.
    }

    void Write(string categoryName, LogLevel level, string message, Exception? exception)
    {
        // Single-line records so the file greps cleanly. Of an exception we
        // record only the type chain: Message and stack text routinely embed
        // file paths ("Could not find file 'C:\...\report.pdf'"), and §19 allows
        // metrics only. The user can still read full details in the error
        // dialog's "Copy Details" button. Enforced here rather than at the call
        // sites so no future logging call can leak a path.
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{Abbreviate(level)}] {categoryName}: {message}";
        if (exception is not null)
        {
            line += $" exception={DescribeTypeChain(exception)}";
        }
        lock (_writeLock)
        {
            try
            {
                File.AppendAllText(_logFilePath, line + Environment.NewLine);
            }
            catch
            {
                // Logging must never take the app down — a full disk or a
                // locked file silently drops the record.
            }
        }
    }

    /// <summary>
    /// Render an exception as its type name followed by the inner-exception
    /// types, e.g. "PdfCleanerException &lt;- IOException". Types alone never
    /// contain user data.
    /// </summary>
    static string DescribeTypeChain(Exception exception)
    {
        var typeNames = new List<string>();
        // Walk the inner-exception chain; a bounded loop is not needed because
        // the framework never builds a cyclic chain.
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            typeNames.Add(current.GetType().Name);
        }
        return string.Join(" <- ", typeNames);
    }

    static string Abbreviate(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Critical => "CRT",
        _ => "???",
    };

    sealed class FileLogger : ILogger
    {
        readonly FileLoggerProvider _provider;
        readonly string _categoryName;

        public FileLogger(FileLoggerProvider provider, string categoryName)
        {
            _provider = provider;
            _categoryName = categoryName;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            _provider.Write(_categoryName, logLevel, formatter(state, exception), exception);
        }
    }
}
