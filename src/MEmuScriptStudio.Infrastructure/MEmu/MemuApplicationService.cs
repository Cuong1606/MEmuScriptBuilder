using MEmuScriptStudio.Core.MEmu;
using MEmuScriptStudio.Core.Models;
using MEmuScriptStudio.Core.Processes;

namespace MEmuScriptStudio.Infrastructure.MEmu;

public sealed class MemuApplicationService(
    IProcessRunner processRunner,
    MemuCommandBuilder commandBuilder,
    AndroidLauncherActivityParser activityParser,
    AndroidApplicationLabelParser labelParser,
    AndroidForegroundApplicationParser foregroundParser) : IMemuApplicationService, IMemuForegroundApplicationService
{
    private const string QueryLauncherActivities =
        "cmd package query-activities --brief --components --user 0 -a android.intent.action.MAIN -c android.intent.category.LAUNCHER";
    private const string QueryLauncherMetadata =
        "cmd package query-activities --user 0 -a android.intent.action.MAIN -c android.intent.category.LAUNCHER";
    private const string QueryForegroundActivity = "dumpsys activity activities";
    private const string QueryForegroundWindow = "dumpsys window windows";

    public async Task<IReadOnlyList<MemuApplicationInfo>> GetApplicationsAsync(
        string memucPath,
        int instanceIndex,
        CancellationToken cancellationToken)
    {
        var direct = commandBuilder.BuildGetAppInfoList(memucPath, instanceIndex);
        var directResult = await RunAsync(direct, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<MemuApplicationInfo> applications = [];
        if (directResult.ExitCode == 0)
        {
            // Only accept unambiguous package/activity component lines. The observed MEmu output was empty,
            // so no undocumented getappinfolist delimiter or field order is assumed here.
            applications = activityParser.Parse(directResult.StandardOutput);
        }

        if (applications.Count == 0)
        {
            var fallback = commandBuilder.BuildAndroidShell(memucPath, instanceIndex, QueryLauncherActivities);
            var fallbackResult = await RunAsync(fallback, cancellationToken).ConfigureAwait(false);
            if (fallbackResult.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Không thể lấy danh sách ứng dụng (exit code {fallbackResult.ExitCode}): {fallbackResult.StandardError.Trim()}");
            }

            applications = activityParser.Parse(fallbackResult.StandardOutput);
        }

        if (applications.Count == 0) return applications;

        // This read-only query is only used to enrich exact package/activity components. Resource IDs and
        // undocumented fields are deliberately ignored instead of being guessed as application names.
        try
        {
            var metadata = commandBuilder.BuildAndroidShell(memucPath, instanceIndex, QueryLauncherMetadata);
            var metadataResult = await RunAsync(metadata, cancellationToken).ConfigureAwait(false);
            if (metadataResult.ExitCode != 0) return applications;

            var labels = labelParser.Parse(metadataResult.StandardOutput);
            return applications
                .Select(application => labels.TryGetValue(application.PackageName, out var label)
                    ? application with { ApplicationLabel = label }
                    : application)
                .ToList();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return applications;
        }
    }

    public async Task<MemuApplicationInfo> GetForegroundApplicationAsync(
        string memucPath,
        int instanceIndex,
        CancellationToken cancellationToken)
    {
        ProcessResult? lastResult = null;
        foreach (var query in new[] { QueryForegroundActivity, QueryForegroundWindow })
        {
            var command = commandBuilder.BuildAndroidShell(memucPath, instanceIndex, query);
            lastResult = await RunAsync(command, cancellationToken).ConfigureAwait(false);
            if (lastResult.ExitCode != 0) continue;
            var application = foregroundParser.Parse(lastResult.StandardOutput);
            if (application is not null) return application;
        }

        var details = lastResult is { ExitCode: not 0 }
            ? $" Exit code {lastResult.ExitCode}: {lastResult.StandardError.Trim()}"
            : string.Empty;
        throw new InvalidOperationException($"Không xác định được ứng dụng đang mở.{details}");
    }

    private Task<ProcessResult> RunAsync(MemuCommand command, CancellationToken cancellationToken) =>
        processRunner.RunAsync(
            new ProcessRequest(command.ExecutablePath, command.Arguments, TimeSpan.FromSeconds(30)),
            cancellationToken);
}
