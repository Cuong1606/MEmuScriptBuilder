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
