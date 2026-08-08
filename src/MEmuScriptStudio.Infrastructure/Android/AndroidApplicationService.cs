using MEmuScriptStudio.Core.Android;
using MEmuScriptStudio.Core.MEmu;
using MEmuScriptStudio.Core.Processes;

namespace MEmuScriptStudio.Infrastructure.Android;

public sealed class AndroidApplicationService(
    IProcessRunner processRunner,
    AdbCommandBuilder commandBuilder,
    AndroidLauncherApplicationParser parser,
    AndroidApplicationLabelParser labelParser,
    AndroidForegroundActivityParser foregroundParser)
    : IAndroidApplicationService, IAndroidForegroundApplicationService
{
    public async Task<IReadOnlyList<AndroidApplicationInfo>> GetApplicationsAsync(
        string adbPath,
        string serial,
        CancellationToken cancellationToken)
    {
        var command = commandBuilder.BuildQueryLauncherActivities(adbPath, serial);
        var result = await processRunner.RunAsync(
            new ProcessRequest(
                command.ExecutablePath,
                command.Arguments,
                TimeSpan.FromSeconds(30),
                ProcessCancellationPolicy.WaitForNaturalExit,
                ProcessTimeoutPolicy.DirectProcessOnly,
                new ProcessDiagnosticContext(null, "AndroidApplications:query-launchers")),
            cancellationToken).ConfigureAwait(false);

        if (result.ExitCode != 0)
            throw new InvalidOperationException(
                $"Không thể lấy danh sách ứng dụng Android / ADB (exit code {result.ExitCode}): {result.StandardError.Trim()}");

        var applications = parser.Parse(result.StandardOutput);
        if (applications.Count == 0) return applications;

        ProcessResult metadataResult;
        try
        {
            var metadataCommand = commandBuilder.BuildQueryLauncherActivityMetadata(adbPath, serial);
            metadataResult = await processRunner.RunAsync(
                new ProcessRequest(
                    metadataCommand.ExecutablePath,
                    metadataCommand.Arguments,
                    TimeSpan.FromSeconds(30),
                    ProcessCancellationPolicy.WaitForNaturalExit,
                    ProcessTimeoutPolicy.DirectProcessOnly,
                    new ProcessDiagnosticContext(null, "AndroidApplications:query-launcher-metadata")),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return applications;
        }
        if (metadataResult.ExitCode != 0) return applications;

        var labels = labelParser.Parse(metadataResult.StandardOutput);
        return applications.Select(application =>
                labels.TryGetValue(application.PackageName, out var label)
                    ? application with { ApplicationLabel = label }
                    : application)
            .ToList();
    }

    public async Task<AndroidApplicationInfo> GetForegroundApplicationAsync(
        string adbPath,
        string serial,
        CancellationToken cancellationToken)
    {
        var activityCommand = commandBuilder.BuildQueryForegroundActivity(adbPath, serial);
        var activityResult = await RunAsync(
            activityCommand,
            "AndroidApplications:foreground-activity",
            cancellationToken).ConfigureAwait(false);
        if (activityResult.ExitCode == 0)
        {
            var activity = foregroundParser.ParseActivityManager(activityResult.StandardOutput);
            if (activity is not null) return activity;
        }

        var windowCommand = commandBuilder.BuildQueryForegroundWindow(adbPath, serial);
        var windowResult = await RunAsync(
            windowCommand,
            "AndroidApplications:foreground-window",
            cancellationToken).ConfigureAwait(false);
        if (windowResult.ExitCode == 0)
        {
            var window = foregroundParser.ParseWindowManager(windowResult.StandardOutput);
            if (window is not null) return window;
            throw new InvalidOperationException("Không xác định được ứng dụng Android đang mở.");
        }

        throw new AndroidAdbDeviceUnavailableException(
            $"Android / ADB {serial} không khả dụng (exit code {windowResult.ExitCode}): {windowResult.StandardError.Trim()}");
    }

    private Task<ProcessResult> RunAsync(
        MemuCommand command,
        string diagnosticCategory,
        CancellationToken cancellationToken) =>
        processRunner.RunAsync(
            new ProcessRequest(
                command.ExecutablePath,
                command.Arguments,
                TimeSpan.FromSeconds(30),
                ProcessCancellationPolicy.WaitForNaturalExit,
                ProcessTimeoutPolicy.DirectProcessOnly,
                new ProcessDiagnosticContext(null, diagnosticCategory)),
            cancellationToken);
}
