using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Channels;
using MEmuScriptStudio.Core.MEmu;
using MEmuScriptStudio.Core.Processes;

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

public sealed class ApplicationMemuHealthDiagnosticLogger : IMemuHealthDiagnosticLogger
{
    public void Write(MemuHealthDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        var message = new StringBuilder("MEMU_CORE_HEALTH")
            .Append(" CheckedAt=").Append(diagnostic.Timestamp.ToString("O"))
            .Append(" Checkpoint=").Append(Sanitize(diagnostic.Checkpoint))
            .Append(" InstanceIndex=").Append(diagnostic.InstanceIndex)
            .Append(" InstanceName=").Append(Sanitize(diagnostic.InstanceName))
            .Append(" HostPid=").Append(diagnostic.HostProcessId?.ToString() ?? "n/a")
            .Append(" CandidateCount=").Append(diagnostic.CandidateCoreCount)
            .Append(" MatchedPid=").Append(diagnostic.MatchedCoreProcessId?.ToString() ?? "n/a")
            .Append(" CreationTime=").Append(diagnostic.CoreCreationTimeUtcFileTime?.ToString() ?? "n/a")
            .Append(" Source=").Append(Sanitize(diagnostic.ResolverSource))
            .Append(" Result=").Append(diagnostic.Result)
            .Append(" Reason=").Append(Sanitize(diagnostic.ReasonCode));
        if (!string.IsNullOrWhiteSpace(diagnostic.Detail))
            message.Append(" Detail=").Append(Sanitize(diagnostic.Detail));
        ApplicationLifecycleLogger.Write(message.ToString());
    }

    private static string Sanitize(string value)
    {
        var sanitized = value.Replace('\r', ' ').Replace('\n', ' ').Replace('|', '/').Trim();
        return sanitized.Length <= 240 ? sanitized : sanitized[..240];
    }
}

public sealed class ApplicationProcessLifecycleLogger : IProcessLifecycleLogger, IDisposable
{
    private const int MaximumSnapshotProcessesPerName = 32;
    private readonly Channel<ProcessLifecycleDiagnostic> diagnostics = Channel.CreateBounded<ProcessLifecycleDiagnostic>(
        new BoundedChannelOptions(128)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
    private readonly Task writerTask;

    public ApplicationProcessLifecycleLogger()
    {
        writerTask = Task.Run(WriteQueuedDiagnosticsAsync);
    }

    public void Write(ProcessLifecycleDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        diagnostics.Writer.TryWrite(diagnostic);
    }

    public void Dispose()
    {
        diagnostics.Writer.TryComplete();
        try { writerTask.Wait(TimeSpan.FromSeconds(1)); }
        catch (Exception) { }
    }

    private async Task WriteQueuedDiagnosticsAsync()
    {
        await foreach (var diagnostic in diagnostics.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            WriteCore(diagnostic);
        }
    }

    private static void WriteCore(ProcessLifecycleDiagnostic diagnostic)
    {

        var message = new StringBuilder("MEMUC_LIFECYCLE")
            .Append(" Event=").Append(diagnostic.EventKind)
            .Append(" EventAt=").Append(diagnostic.Timestamp.ToString("O"))
            .Append(" DurationMs=").Append(Math.Max(0, diagnostic.Duration.TotalMilliseconds).ToString("F0"))
            .Append(" ProcessPid=").Append(diagnostic.ProcessId)
            .Append(" Instance=").Append(diagnostic.InstanceIndex?.ToString() ?? "n/a")
            .Append(" Command=").Append(Sanitize(diagnostic.CommandCategory));

        if (diagnostic.ExitCode is not null)
            message.Append(" ExitCode=").Append(diagnostic.ExitCode.Value);
        if (!string.IsNullOrWhiteSpace(diagnostic.Marker))
            message.Append(" Marker=").Append(Sanitize(diagnostic.Marker));
        if (diagnostic.EventKind is ProcessLifecycleEventKind.UserCancellationRequested or
            ProcessLifecycleEventKind.UserCancellationNaturalExit)
        {
            message.Append(" ProcessSnapshot=").Append(CreateReadOnlyProcessSnapshot());
        }

        ApplicationLifecycleLogger.Write(message.ToString());
    }

    private static string CreateReadOnlyProcessSnapshot() => string.Join(
        ';',
        new[] { "memuc", "MEmu", "MEmuHeadless" }.Select(name => $"{name}:[{GetProcessIds(name)}]"));

    private static string GetProcessIds(string processName)
    {
        Process[] processes = [];
        try
        {
            processes = Process.GetProcessesByName(processName);
            return string.Join(',', processes
                .Select(process => TryGetProcessId(process))
                .Where(processId => processId is not null)
                .Select(processId => processId!.Value)
                .Order()
                .Take(MaximumSnapshotProcessesPerName));
        }
        catch (Exception)
        {
            return "unavailable";
        }
        finally
        {
            foreach (var process in processes) process.Dispose();
        }
    }

    private static int? TryGetProcessId(Process process)
    {
        try { return process.Id; }
        catch (Exception) { return null; }
    }

    private static string Sanitize(string value)
    {
        var sanitized = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return sanitized.Length <= 80 ? sanitized : sanitized[..80];
    }
}
