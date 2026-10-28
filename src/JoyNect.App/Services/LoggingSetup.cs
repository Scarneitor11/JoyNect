using System.IO;
using System.Text;
using Microsoft.Extensions.Logging;

namespace JoyNect.App.Services;

/// <summary>Builds the app's logger: a rolling file, always, and the debugger when attached.</summary>
/// <remarks>
/// Logging to a file is not optional here. A controller that fails to connect is diagnosed from the
/// handshake trace, and a user cannot be asked to run a debugger. The file is capped and rotated so it
/// can never grow without bound.
/// </remarks>
internal static class LoggingSetup
{
    /// <summary>Where the log lives, for the About page and for support.</summary>
    public static string LogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "JoyNect",
        "logs",
        "joynect.log");

    public static ILoggerFactory Create()
    {
        try
        {
            var directory = Path.GetDirectoryName(LogPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }
        catch (Exception)
        {
            // A read-only profile still gets a working app, just without a log file.
        }

        return LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddProvider(new FileLoggerProvider(LogPath));
        });
    }
}

/// <summary>Appends log lines to a file, rotating it once it passes two megabytes.</summary>
internal sealed class FileLoggerProvider : ILoggerProvider
{
    private const long MaxBytes = 2 * 1024 * 1024;

    private readonly string _path;
    private readonly object _gate = new();

    public FileLoggerProvider(string path)
    {
        _path = path;
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void Dispose()
    {
    }

    internal void Write(LogLevel level, string category, string message, Exception? exception)
    {
        var line = new StringBuilder()
            .Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
            .Append(' ')
            .Append(level switch
            {
                LogLevel.Trace => "trace",
                LogLevel.Debug => "debug",
                LogLevel.Information => "info ",
                LogLevel.Warning => "warn ",
                LogLevel.Error => "error",
                LogLevel.Critical => "fatal",
                _ => "none ",
            })
            .Append(' ')
            .Append(category)
            .Append(" · ")
            .Append(message);

        if (exception is not null)
        {
            line.AppendLine().Append(exception);
        }

        lock (_gate)
        {
            try
            {
                Rotate();
                File.AppendAllText(_path, line.AppendLine().ToString());
            }
            catch (Exception)
            {
                // Logging must never take the app down.
            }
        }
    }

    private void Rotate()
    {
        var info = new FileInfo(_path);
        if (!info.Exists || info.Length < MaxBytes)
        {
            return;
        }

        File.Move(_path, _path + ".1", overwrite: true);
    }

    private sealed class FileLogger : ILogger
    {
        private readonly FileLoggerProvider _provider;
        private readonly string _category;

        public FileLogger(FileLoggerProvider provider, string category)
        {
            _provider = provider;
            _category = category;
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            _provider.Write(logLevel, _category, formatter(state, exception), exception);
        }
    }
}
