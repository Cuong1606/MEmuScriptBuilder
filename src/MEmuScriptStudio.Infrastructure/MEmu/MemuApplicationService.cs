using MEmuScriptStudio.Core.MEmu;
using MEmuScriptStudio.Core.Models;
using MEmuScriptStudio.Core.Processes;

namespace MEmuScriptStudio.Infrastructure.MEmu;

public sealed class MemuApplicationService(
    IProcessRunner processRunner,
    MemuCommandBuilder commandBuilder,
    AndroidLauncherActivityParser activityParser,
    AndroidApplicationLabelParser labelParser) : IMemuApplicationService
{
    private const string QueryLauncherActivities =
        "cmd package query-activities --brief --components --user 0 -a android.intent.action.MAIN -c android.intent.category.LAUNCHER";
    private const string QueryLauncherMetadata =
        "cmd package query-activities --user 0 -a android.intent.action.MAIN -c android.intent.category.LAUNCHER";

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

    private Task<ProcessResult> RunAsync(MemuCommand command, CancellationToken cancellationToken) =>
        processRunner.RunAsync(
            new ProcessRequest(command.ExecutablePath, command.Arguments, TimeSpan.FromSeconds(30)),
            cancellationToken);
}
