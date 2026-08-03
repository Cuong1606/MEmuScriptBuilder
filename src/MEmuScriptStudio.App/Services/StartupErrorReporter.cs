using System.IO;
using System.Windows;

namespace MEmuScriptStudio.App.Services;

public interface IStartupIssueLogger
{
    void Report(Exception exception);
}

public sealed class StartupIssueLogger : IStartupIssueLogger
{
    public void Report(Exception exception) => StartupErrorReporter.Report(exception, showDialog: false);
}

public static class StartupErrorReporter
{
    public static string? Report(Exception exception, bool showDialog = true)
    {
        ArgumentNullException.ThrowIfNull(exception);
        string? writtenLogPath = null;
        try
        {
            var logDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MEmuScriptStudio",
                "logs");
            Directory.CreateDirectory(logDirectory);
            var logPath = Path.Combine(logDirectory, "startup-error.log");
            File.AppendAllText(logPath, $"[{DateTimeOffset.Now:O}]{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
            writtenLogPath = logPath;
        }
        catch
        {
            // The original startup exception remains visible even if local logging is unavailable.
        }

        if (!showDialog) return writtenLogPath;

        var logHint = writtenLogPath is null ? string.Empty : $"\n\nChi tiết đã được ghi tại:\n{writtenLogPath}";
        try
        {
            MessageBox.Show(
                $"MEmu Script Studio không thể khởi động.\n\n{exception.Message}{logHint}",
                "Lỗi khởi động",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch
        {
            // Startup reporting must never replace the original exception or prevent shutdown.
        }
        return writtenLogPath;
    }
}
