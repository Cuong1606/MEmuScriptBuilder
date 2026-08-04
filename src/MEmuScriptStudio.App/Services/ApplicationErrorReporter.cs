using System.IO;

namespace MEmuScriptStudio.App.Services;

public static class ApplicationErrorReporter
{
    public static string? Report(Exception exception, string context)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentException.ThrowIfNullOrWhiteSpace(context);

        try
        {
            var logDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MEmuScriptStudio",
                "logs");
            Directory.CreateDirectory(logDirectory);
            var logPath = Path.Combine(logDirectory, "application-error.log");
            File.AppendAllText(
                logPath,
                $"[{DateTimeOffset.Now:O}] Context={context}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
            return logPath;
        }
        catch
        {
            // Error reporting must not replace the original application exception.
            return null;
        }
    }
}

public static class ApplicationLifecycleLogger
{
    private static readonly object SyncRoot = new();

    public static void Write(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        Append($"[{DateTimeOffset.Now:O}] PID={Environment.ProcessId} {message}{Environment.NewLine}");
    }

    public static void WriteException(string eventName, Exception exception)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
        ArgumentNullException.ThrowIfNull(exception);
        Append(
            $"[{DateTimeOffset.Now:O}] PID={Environment.ProcessId} {eventName}{Environment.NewLine}" +
            $"{exception}{Environment.NewLine}{Environment.NewLine}");
    }

    private static void Append(string entry)
    {
        try
        {
            var logDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MEmuScriptStudio",
                "logs");
            Directory.CreateDirectory(logDirectory);
            lock (SyncRoot)
            {
                File.AppendAllText(Path.Combine(logDirectory, "application-lifecycle.log"), entry);
            }
        }
        catch
        {
            // Lifecycle diagnostics must never change application behavior.
        }
    }
}
