using System.Collections.Frozen;
using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.CompilerServices;
using MEmuScriptStudio.App.Services;
using MEmuScriptStudio.Core.Android;
using MEmuScriptStudio.Core.Execution;
using MEmuScriptStudio.Core.Formatting;
using MEmuScriptStudio.Core.MEmu;
using MEmuScriptStudio.Core.Models;
using MEmuScriptStudio.Core.Scripts;
using LaunchSpacingModeValue = MEmuScriptStudio.Core.Models.LaunchSpacingMode;
using ScriptAssignmentModeValue = MEmuScriptStudio.Core.Models.ScriptAssignmentMode;

namespace MEmuScriptStudio.App.ViewModels;

public sealed partial class MainViewModel
{
    private const int LatestRunMessageLimit = 240;
    private const int LatestRunDescriptionLimit = 240;
    private const int RunDescriptionScriptNameLimit = 48;
    private const int RunDescriptionVisibleScriptLimit = 3;
    private readonly IMultiInstanceExecutionScheduler executionScheduler;
    private readonly Dictionary<Guid, MultiInstanceExecutionSession> executionSessions = [];
    private readonly Dictionary<string, Guid> activeInstanceGroups = new(StringComparer.Ordinal);
    private readonly Dictionary<(Guid LaunchGroupId, string TargetKey), InstanceRunItemViewModel> instanceRunsByKey = [];
    private readonly RangeObservableCollection<InstanceRunItemViewModel> activeInstanceRuns = [];
    private readonly HashSet<string> dynamicSessionUniverse = new(StringComparer.Ordinal);
    private readonly HashSet<string> dynamicSessionAdmitted = new(StringComparer.Ordinal);
    private readonly SynchronizationContext? editorSynchronizationContext = SynchronizationContext.Current;
    private TaskCompletionSource? executionTerminalCompletion;
    private int launchGroupSequence;
    private int runningInstanceCount;
    private int waitingInstanceCount;
    private bool isExecuting;
    private bool isSafeShutdownRequested;
    private LatestRunResultViewModel? latestRunResult;
    private LaunchSpacingModeValue launchSpacingMode = LaunchSpacingModeValue.Fixed;
    private int fixedSpacingMilliseconds;
    private int randomMinimumSpacingMilliseconds;
    private int randomMaximumSpacingMilliseconds;
    private bool isFixedSpacingInputValid = true;
    private bool isRandomMinimumSpacingInputValid = true;
    private bool isRandomMaximumSpacingInputValid = true;
    private bool stopAllOnInvalidTarget;
    private string testStepFeedback = string.Empty;
    private long testStepFeedbackVersion;

    public string TestStepFeedback
    {
        get => testStepFeedback;
        private set => SetProperty(ref testStepFeedback, value);
    }

    private IReadOnlyList<IExecutionTarget> ResolveSelectedTargetCandidates() =>
        FilteredRunTargets.Where(item => item.IsSelected && item.CanSelectForRun).Select(item => item.Model).ToList();

    private IReadOnlyList<IExecutionTarget> ResolveRequestedTargets() =>
        FilteredRunTargets
            .Where(item => item.IsSelected && item.IsRunning && !activeInstanceGroups.ContainsKey(item.TargetKey))
            .Select(item => item.Model)
            .ToList();

    private string? ValidateRunConfiguration(IReadOnlyList<IExecutionTarget>? requestedTargets = null)
    {
        if (HasBlockingExecutionDraft)
            return "Hãy lưu hoặc hủy thay đổi trong editor trước khi chạy.";
        if (LaunchSpacingMode == LaunchSpacingModeValue.Fixed)
        {
            if (!IsFixedSpacingInputValid) return "Khoảng cách cố định đang có giá trị không hợp lệ.";
            if (FixedSpacingMilliseconds < 0) return "Khoảng cách cố định không được âm.";
        }
        if (LaunchSpacingMode == LaunchSpacingModeValue.Random)
        {
            if (!IsRandomMinimumSpacingInputValid || !IsRandomMaximumSpacingInputValid)
                return "Khoảng cách ngẫu nhiên đang có giá trị không hợp lệ.";
            if (RandomMinimumSpacingMilliseconds < 0 || RandomMaximumSpacingMilliseconds < 0)
                return "Khoảng cách ngẫu nhiên không được âm.";
            if (RandomMinimumSpacingMilliseconds > RandomMaximumSpacingMilliseconds)
                return "Khoảng cách ngẫu nhiên tối thiểu không được lớn hơn tối đa.";
        }
        return ValidateScriptAssignments(requestedTargets);
    }

    private void UpdateRunConfigurationState()
    {
        OnPropertyChanged(nameof(SelectedRunTargetCount));
        OnPropertyChanged(nameof(RunTargetSelectionSummary));
        OnPropertyChanged(nameof(RunConfigurationError));
        NotifyRunTargetViewStateChanged();
        RaiseCommandStates();
    }

    private static string BuildCompletionMessage(MultiInstanceExecutionResult result)
    {
        var succeeded = result.Instances.Count(item => item.Status == InstanceExecutionStatus.Succeeded);
        var failed = result.Instances.Count(item => item.Status == InstanceExecutionStatus.Failed);
        var unavailable = result.Instances.Count(item => item.Status == InstanceExecutionStatus.Unavailable);
        var cancelled = result.Instances.Count(item => item.Status == InstanceExecutionStatus.Cancelled);
        var prefix = result.WasStoppedByInvalidTargetPolicy
            ? "Đã dừng toàn bộ tại preflight."
            : result.WasCancelled
                ? "Đã dừng phiên chạy."
                : "Đã hoàn tất phiên chạy.";
        return $"{prefix} Thành công: {succeeded}; thất bại: {failed}; không khả dụng/bỏ qua: {unavailable}; đã hủy: {cancelled}.";
    }

    private async Task TestCurrentStepAsync()
    {
        if (!CanTestCurrentStep() || !TryGetRunnableEditorTestTarget(out var target)) return;
        var feedbackVersion = Interlocked.Increment(ref testStepFeedbackVersion);
        var draft = CreateStep(SelectedStep?.Id);
        stepCommandBuilder.Validate(draft);
        if (draft is AndroidShellStep && !confirmationService.Confirm(
                "Bước chạy thử là lệnh Android shell thô. Chỉ tiếp tục nếu bạn tin cậy lệnh này.",
                "Cảnh báo lệnh shell thô"))
        {
            StatusMessage = "Đã hủy chạy thử vì lệnh shell thô chưa được xác nhận.";
            SetTestStepFeedback(feedbackVersion, "Đã hủy");
            return;
        }

        if (!TryGetRunnableEditorTestTarget(out var confirmedTarget) ||
            !string.Equals(confirmedTarget.TargetKey, target.TargetKey, StringComparison.Ordinal))
        {
            StatusMessage = "Không thể chạy thử: thiết bị đã được dùng hoặc không còn khả dụng.";
            SetTestStepFeedback(feedbackVersion, "Lỗi");
            return;
        }
        target = confirmedTarget;

        var transientScript = new ScriptDefinition
        {
            Id = Guid.NewGuid(),
            Name = $"Chạy thử: {draft.Name}",
            Steps = [draft]
        };
        ScriptLibraryValidator.Validate([transientScript]);
        var librarySnapshot = ExecutionScriptLibrarySnapshot.Create([transientScript]);
        var scriptSnapshot = librarySnapshot.CreateScriptCopy(transientScript.Id);
        IReadOnlyDictionary<string, ScriptDefinition> scriptsByTarget =
            new Dictionary<string, ScriptDefinition>(StringComparer.Ordinal)
            {
                [target.TargetKey] = scriptSnapshot
            };
        var executionRequest = new MultiInstanceExecutionRequest
        {
            LaunchGroupId = Guid.NewGuid(),
            Script = scriptSnapshot,
            ScriptsByTarget = scriptsByTarget,
            ScriptLibrarySnapshot = librarySnapshot,
            MemucPath = MemucPath,
            AdbPath = AdbPath,
            Targets = [target],
            LaunchSpacingMode = LaunchSpacingModeValue.Fixed,
            FixedSpacing = TimeSpan.Zero,
            StopAllOnInvalidTarget = false
        };
        SetTestStepFeedback(feedbackVersion, "Đang chạy…");
        try
        {
            await StartPreparedLaunchGroupAsync(
                executionRequest,
                scriptsByTarget,
                CompactRunDescription($"Chạy thử bước · {draft.Name}"),
                $"Đang chạy thử bước '{draft.Name}' trên thiết bị '{target.Name}'…",
                markDynamicSessionAdmitted: false,
                () => Task.FromResult<string?>(null),
                result => BuildTestStepCompletionMessage(result, draft.Name, target.Name),
                "Chạy thử bước gặp lỗi",
                (result, error) => SetTestStepFeedback(feedbackVersion, error is not null
                    ? "Lỗi"
                    : result?.Instances.SingleOrDefault()?.Status switch
                    {
                        InstanceExecutionStatus.Succeeded => "Thành công",
                        InstanceExecutionStatus.Cancelled => "Đã hủy",
                        _ => "Lỗi"
                    }));
        }
        catch
        {
            SetTestStepFeedback(feedbackVersion, "Lỗi");
            throw;
        }
    }

    private void SetTestStepFeedback(long version, string value)
    {
        if (Volatile.Read(ref testStepFeedbackVersion) == version)
            TestStepFeedback = value;
    }

    private bool TryGetRunnableEditorTestTarget(out IExecutionTarget target)
    {
        target = null!;
        var selected = SelectedEditorTarget;
        if (selected is null || !EditorTargets.Contains(selected) || !selected.IsAvailable ||
            activeInstanceGroups.ContainsKey(selected.TargetKey)) return false;
        target = selected.Model;
        return target switch
        {
            MemuInstance { IsRunning: true } => IsPathValid,
            AndroidAdbDevice { ConnectionState: AndroidConnectionState.Device } => IsAdbPathValid,
            _ => false
        };
    }

    private static string BuildTestStepCompletionMessage(
        MultiInstanceExecutionResult result,
        string stepName,
        string targetName) => result.Instances.SingleOrDefault()?.Status switch
    {
        InstanceExecutionStatus.Succeeded => $"Chạy thử bước '{stepName}' thành công trên thiết bị '{targetName}'.",
        InstanceExecutionStatus.Cancelled => $"Đã hủy chạy thử bước '{stepName}' trên thiết bị '{targetName}'.",
        InstanceExecutionStatus.Unavailable => $"Không thể chạy thử bước '{stepName}': thiết bị '{targetName}' không khả dụng.",
        _ => $"Chạy thử bước '{stepName}' gặp lỗi trên thiết bị '{targetName}'."
    };

    private async Task RunAsync()
    {
        await StartLaunchGroupAsync(ResolveSelectedTargetCandidates());
    }

    private async Task RunAllRemainingAsync()
    {
        EnsureDynamicSession();
        var requestedTargets = ResolveAllRemainingTargetCandidates();
        if (requestedTargets.Count == 0)
        {
            StatusMessage = "Không còn thiết bị nào trong phiên hiện tại để chạy.";
            return;
        }
        await StartLaunchGroupAsync(requestedTargets);
    }

    private async Task StartLaunchGroupAsync(IReadOnlyList<IExecutionTarget> requestedTargets)
    {
        if (isSafeShutdownRequested || requestedTargets.Any(target => target.Kind == DeviceKind.MEmu && !IsPathValid) ||
            requestedTargets.Any(target => target.Kind == DeviceKind.AndroidAdb && !IsAdbPathValid)) return;
        if (isSafeShutdownRequested) return;
        EnsureDynamicSession();
        var skippedActive = requestedTargets.Where(target => activeInstanceGroups.ContainsKey(target.TargetKey)).ToList();
        requestedTargets = requestedTargets
            .Where(target => !activeInstanceGroups.ContainsKey(target.TargetKey))
            .ToList();
        var configurationError = ValidateRunConfiguration(requestedTargets);
        if (requestedTargets.Count == 0 || configurationError is not null)
        {
            StatusMessage = configurationError ?? (skippedActive.Count > 0
                ? $"Đã bỏ qua {skippedActive.Count} thiết bị đang hoạt động."
                : "Hãy chọn ít nhất một thiết bị để chạy.");
            return;
        }

        if (SelectedScript is not null) SyncStepsToModel();
        var assignedScripts = ResolveAssignedScripts(requestedTargets);
        if (assignedScripts is null)
        {
            StatusMessage = ValidateScriptAssignments() ?? "Hãy gán kịch bản cho mọi thiết bị sẽ chạy.";
            return;
        }
        var libraryModels = Scripts.Select(item => item.Model).ToList();
        ScriptLibraryValidator.Validate(libraryModels);
        var libraryById = libraryModels.ToDictionary(script => script.Id);
        var rawStepCount = requestedTargets
            .Where(target => target.Kind == DeviceKind.MEmu)
            .Sum(target => CountRawShellSteps(assignedScripts[target.TargetKey], libraryById));
        if (rawStepCount > 0 && !confirmationService.Confirm(
                $"Các kịch bản đã gán có tổng cộng {rawStepCount} lệnh Android shell thô trên {requestedTargets.Count} lượt chạy. Chỉ tiếp tục nếu bạn tin cậy các lệnh này.",
                "Cảnh báo lệnh shell thô"))
        {
            StatusMessage = "Đã hủy chạy vì lệnh shell thô chưa được xác nhận.";
            return;
        }

        var scriptLibrarySnapshot = ExecutionScriptLibrarySnapshot.Create(libraryModels);
        var scriptSnapshots = assignedScripts.ToFrozenDictionary(
            pair => pair.Key,
            pair => scriptLibrarySnapshot.CreateScriptCopy(pair.Value.Id));
        var defaultScriptSnapshot = scriptSnapshots[requestedTargets[0].TargetKey];
        var memucPathSnapshot = MemucPath;
        var adbPathSnapshot = AdbPath;
        var runSettingsSnapshot = new MultiInstanceRunSettings
        {
            LaunchSpacingMode = LaunchSpacingMode,
            FixedSpacingMilliseconds = FixedSpacingMilliseconds,
            RandomMinimumSpacingMilliseconds = RandomMinimumSpacingMilliseconds,
            RandomMaximumSpacingMilliseconds = RandomMaximumSpacingMilliseconds,
            StopAllOnInvalidTarget = StopAllOnInvalidTarget,
            ScriptAssignmentMode = ScriptAssignmentMode,
            CommonScriptId = CommonRunScript?.Id
        };
        foreach (var pair in applicationSettings.MultiInstanceRun.TargetScriptAssignments)
            runSettingsSnapshot.TargetScriptAssignments[pair.Key] = pair.Value;
        foreach (var pair in applicationSettings.MultiInstanceRun.ScriptAssignments)
            runSettingsSnapshot.ScriptAssignments[pair.Key] = pair.Value;
        foreach (var target in RunTargets)
        {
            runSettingsSnapshot.TargetScriptAssignments.Remove(target.TargetKey);
            if (target.AssignedScriptId is Guid assignedScriptId)
                runSettingsSnapshot.TargetScriptAssignments[target.TargetKey] = assignedScriptId;
            if (target.DeviceKind != DeviceKind.MEmu) continue;
            runSettingsSnapshot.ScriptAssignments.Remove(target.Index);
            if (target.AssignedScriptId is Guid assignedMemuScriptId)
                runSettingsSnapshot.ScriptAssignments[target.Index] = assignedMemuScriptId;
        }
        var executionRequest = new MultiInstanceExecutionRequest
        {
            LaunchGroupId = Guid.NewGuid(),
            Script = defaultScriptSnapshot,
            ScriptsByTarget = scriptSnapshots,
            ScriptLibrarySnapshot = scriptLibrarySnapshot,
            MemucPath = memucPathSnapshot,
            AdbPath = adbPathSnapshot,
            Targets = requestedTargets,
            LaunchSpacingMode = runSettingsSnapshot.LaunchSpacingMode,
            FixedSpacing = TimeSpan.FromMilliseconds(runSettingsSnapshot.FixedSpacingMilliseconds),
            RandomMinimumSpacing = TimeSpan.FromMilliseconds(runSettingsSnapshot.RandomMinimumSpacingMilliseconds),
            RandomMaximumSpacing = TimeSpan.FromMilliseconds(runSettingsSnapshot.RandomMaximumSpacingMilliseconds),
            StopAllOnInvalidTarget = runSettingsSnapshot.StopAllOnInvalidTarget
        };
        var runDescription = BuildRunDescription(runSettingsSnapshot.ScriptAssignmentMode, defaultScriptSnapshot, scriptSnapshots);
        var startingMessage = ScriptAssignmentMode == ScriptAssignmentModeValue.OneScriptForAll
            ? $"Đang chạy '{defaultScriptSnapshot.Name}' trên {requestedTargets.Count} thiết bị…"
            : $"Đang chạy kịch bản đã gán trên {requestedTargets.Count} thiết bị…";
        if (skippedActive.Count > 0)
            startingMessage += $" Đã bỏ qua {skippedActive.Count} thiết bị đang hoạt động.";
        await StartPreparedLaunchGroupAsync(
            executionRequest,
            scriptSnapshots,
            runDescription,
            startingMessage,
            markDynamicSessionAdmitted: true,
            () => PersistRunSettingsAsync(memucPathSnapshot, adbPathSnapshot, runSettingsSnapshot));
    }

    private Task StartPreparedLaunchGroupAsync(
        MultiInstanceExecutionRequest executionRequest,
        IReadOnlyDictionary<string, ScriptDefinition> scriptSnapshots,
        string runDescription,
        string startingMessage,
        bool markDynamicSessionAdmitted,
        Func<Task<string?>> settingsTaskFactory,
        Func<MultiInstanceExecutionResult, string>? completionMessageBuilder = null,
        string? completionErrorPrefix = null,
        Action<MultiInstanceExecutionResult?, Exception?>? completionFeedback = null)
    {
        var requestedTargets = executionRequest.Targets;
        var groupId = executionRequest.LaunchGroupId;
        var runItems = new List<InstanceRunItemViewModel>();
        var runTargetRowsByKey = RunTargets.ToDictionary(item => item.TargetKey, StringComparer.Ordinal);
        foreach (var target in requestedTargets)
        {
            activeInstanceGroups[target.TargetKey] = groupId;
            if (markDynamicSessionAdmitted) dynamicSessionAdmitted.Add(target.TargetKey);
            var item = new InstanceRunItemViewModel(groupId, target, scriptSnapshots[target.TargetKey], StopInstance);
            item.SelectionChanged += OnActiveInstanceSelectionChanged;
            runItems.Add(item);
            instanceRunsByKey[(groupId, target.TargetKey)] = item;
            AdjustActiveStatusCount(item.Status, 1);
            if (runTargetRowsByKey.TryGetValue(target.TargetKey, out var row)) row.SetActive(true);
        }
        activeInstanceRuns.AddRange(runItems);
        var group = new LaunchGroupItemViewModel(
            ++launchGroupSequence,
            groupId,
            DateTimeOffset.UtcNow,
            runDescription,
            runItems);
        ActiveLaunchGroups.Add(group);
        SetExecutionAggregateState();
        StatusMessage = startingMessage;
        var progress = new InstanceExecutionProgressPump(
            editorSynchronizationContext,
            ApplyExecutionUpdate);
        try
        {
            var session = executionScheduler.Start(executionRequest, progress);
            executionSessions[groupId] = session;
            SetExecutionAggregateState();
            var settingsTask = settingsTaskFactory();
            _ = ObserveLaunchGroupAsync(
                groupId,
                session,
                settingsTask,
                progress,
                completionMessageBuilder,
                completionErrorPrefix,
                completionFeedback);
        }
        catch
        {
            foreach (var target in requestedTargets)
            {
                if (activeInstanceGroups.GetValueOrDefault(target.TargetKey) == groupId)
                    activeInstanceGroups.Remove(target.TargetKey);
                if (runTargetRowsByKey.TryGetValue(target.TargetKey, out var row)) row.SetActive(false);
            }
            foreach (var item in runItems)
            {
                var previousStatus = item.Status;
                item.Apply(new InstanceExecutionUpdate(groupId, item.Index, item.Name, InstanceExecutionStatus.Failed,
                    Message: "Không thể khởi tạo nhóm chạy.", ScriptId: item.ScriptId, ScriptName: item.ScriptName));
                UpdateActiveStatusCount(previousStatus, item.Status);
            }
            CompleteLaunchGroup(groupId, null, DateTimeOffset.UtcNow);
            SetExecutionAggregateState();
            throw;
        }
        return Task.CompletedTask;
    }

    private async Task ObserveLaunchGroupAsync(
        Guid groupId,
        MultiInstanceExecutionSession session,
        Task<string?> settingsTask,
        InstanceExecutionProgressPump progress,
        Func<MultiInstanceExecutionResult, string>? completionMessageBuilder = null,
        string? completionErrorPrefix = null,
        Action<MultiInstanceExecutionResult?, Exception?>? completionFeedback = null)
    {
        MultiInstanceExecutionResult? completedResult = null;
        Exception? completionError = null;
        try
        {
            completedResult = await session.Completion;
        }
        catch (Exception exception)
        {
            completionError = exception;
        }
        finally
        {
            progress.DrainPending();
            foreach (var index in activeInstanceGroups
                         .Where(pair => pair.Value == groupId).Select(pair => pair.Key).ToList())
                activeInstanceGroups.Remove(index);
            executionSessions.Remove(groupId);
            session.Dispose();
            CompleteLaunchGroup(groupId, completedResult, completedResult?.EndedAt ?? DateTimeOffset.UtcNow);
            SetExecutionAggregateState();
        }

        completionFeedback?.Invoke(completedResult, completionError);

        if (completionError is not null)
        {
            completedResult = null;
            session = null!;
            StatusMessage = $"{completionErrorPrefix ?? "Nhóm chạy gặp lỗi"}: {completionError.Message}";
            return;
        }

        var completionMessage = completionMessageBuilder?.Invoke(completedResult!) ?? BuildCompletionMessage(completedResult!);
        completedResult = null;
        session = null!;
        var settingsWarning = await settingsTask;
        StatusMessage = settingsWarning is null
            ? completionMessage
            : $"{completionMessage} {settingsWarning}";
    }

    private void CompleteLaunchGroup(
        Guid groupId,
        MultiInstanceExecutionResult? completedResult,
        DateTimeOffset fallbackEndedAt)
    {
        var group = ActiveLaunchGroups.FirstOrDefault(item => item.LaunchGroupId == groupId);
        if (group is null) return;

        var latestRunResult = CreateLatestRunResult(group, completedResult, fallbackEndedAt);
        group.Detach();
        ActiveLaunchGroups.Remove(group);
        var runTargetsByKey = RunTargets.ToDictionary(item => item.TargetKey, StringComparer.Ordinal);
        var removedTargets = new List<InstanceTargetItemViewModel>();
        foreach (var instance in group.Instances)
        {
            instance.SelectionChanged -= OnActiveInstanceSelectionChanged;
            AdjustActiveStatusCount(instance.Status, -1);
            instanceRunsByKey.Remove((groupId, instance.TargetKey));
            runTargetsByKey.TryGetValue(instance.TargetKey, out var target);
            target?.SetActive(false);
            var stillDiscovered = discoveredTargetKeys.Contains(instance.TargetKey);
            if (target is not null && !stillDiscovered)
            {
                target.SelectionChanged -= OnRunTargetSelectionChanged;
                target.AssignmentChanged -= OnTargetAssignmentChanged;
                removedTargets.Add(target);
                dynamicSessionUniverse.Remove(instance.TargetKey);
                dynamicSessionAdmitted.Remove(instance.TargetKey);
            }
        }
        activeInstanceRuns.RemoveRange(group.Instances);
        runTargets.RemoveRange(removedTargets);

        RebuildRunTargetProjection(clearHiddenSelection: false);
        UpdateRunConfigurationState();
        AddRecentRun(latestRunResult);
        StopSelectedActiveInstancesCommand.RaiseCanExecuteChanged();
    }

    private static LatestRunResultViewModel CreateLatestRunResult(
        LaunchGroupItemViewModel group,
        MultiInstanceExecutionResult? completedResult,
        DateTimeOffset fallbackEndedAt)
    {
        var instanceSnapshots = new List<RecentRunInstanceSnapshotViewModel>();
        var runtimesByTargetKey = new Dictionary<string, InstanceRunItemViewModel>(group.Instances.Count, StringComparer.Ordinal);
        var runtimeStepNamesByKey = new Dictionary<(string TargetKey, Guid StepId), string>();
        var lastRuntimeStepNamesByTargetKey = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var runtime in group.Instances)
        {
            if (!runtimesByTargetKey.TryAdd(runtime.TargetKey, runtime)) continue;
            string? lastRuntimeStepName = null;
            foreach (var step in runtime.Steps)
            {
                runtimeStepNamesByKey.TryAdd((runtime.TargetKey, step.Id), step.Name);
                if (step.Status != StepExecutionStatus.NotRun) lastRuntimeStepName = step.Name;
            }
            if (lastRuntimeStepName is not null)
                lastRuntimeStepNamesByTargetKey.TryAdd(runtime.TargetKey, lastRuntimeStepName);
        }

        IReadOnlyList<InstanceExecutionStatus> statuses;
        if (completedResult is not null)
        {
            var normalizedStatuses = new InstanceExecutionStatus[completedResult.Instances.Count];
            var executionSnapshots = new LatestExecutionSnapshot[completedResult.Instances.Count];
            for (var index = 0; index < completedResult.Instances.Count; index++)
            {
                var result = completedResult.Instances[index];
                normalizedStatuses[index] = NormalizeLatestStatus(result.Status);
                executionSnapshots[index] = CreateLatestExecutionSnapshot(result.Execution);
            }
            statuses = normalizedStatuses;

            for (var index = 0; index < completedResult.Instances.Count; index++)
            {
                var result = completedResult.Instances[index];
                var latestStatus = normalizedStatuses[index];
                runtimesByTargetKey.TryGetValue(result.Target.TargetKey, out var runtime);
                instanceSnapshots.Add(new RecentRunInstanceSnapshotViewModel(
                    result.Target.Index,
                    CompactText(result.Target.Name, 160),
                    CompactText(result.ScriptName ?? runtime?.ScriptName ?? "—", 160),
                    ResolveLastStepName(
                        result.Target.TargetKey,
                        executionSnapshots[index],
                        runtimeStepNamesByKey,
                        lastRuntimeStepNamesByTargetKey),
                    latestStatus,
                    BuildShortRunMessage(latestStatus, result.Message ?? runtime?.Message, executionSnapshots[index]),
                    result.Target.TargetKey,
                    result.Target.Kind,
                    result.Target.Identifier));
            }
        }
        else
        {
            statuses = group.Instances.Select(instance => NormalizeLatestStatus(instance.Status)).ToArray();
            foreach (var instance in group.Instances)
            {
                var latestStatus = NormalizeLatestStatus(instance.Status);
                instanceSnapshots.Add(new RecentRunInstanceSnapshotViewModel(
                    instance.Index,
                    CompactText(instance.Name, 160),
                    CompactText(instance.ScriptName, 160),
                    lastRuntimeStepNamesByTargetKey.GetValueOrDefault(instance.TargetKey, "—"),
                    latestStatus,
                    BuildShortRunMessage(latestStatus, instance.Message, default),
                    instance.TargetKey,
                    instance.Target.Kind,
                    instance.Identifier));
            }
        }

        return new LatestRunResultViewModel(
            group.LaunchGroupId,
            group.DisplayName,
            group.RunDescription,
            completedResult?.StartedAt ?? group.StartedAt,
            completedResult?.EndedAt ?? fallbackEndedAt,
            statuses.Count,
            statuses.Count(status => status == InstanceExecutionStatus.Succeeded),
            statuses.Count(status => status == InstanceExecutionStatus.Failed),
            statuses.Count(status => status == InstanceExecutionStatus.Unavailable),
            statuses.Count(status => status == InstanceExecutionStatus.Cancelled),
            instanceSnapshots.AsReadOnly());
    }

    private static InstanceExecutionStatus NormalizeLatestStatus(InstanceExecutionStatus status) => status switch
    {
        InstanceExecutionStatus.Succeeded => InstanceExecutionStatus.Succeeded,
        InstanceExecutionStatus.Failed => InstanceExecutionStatus.Failed,
        InstanceExecutionStatus.Unavailable => InstanceExecutionStatus.Unavailable,
        InstanceExecutionStatus.Cancelled => InstanceExecutionStatus.Cancelled,
        _ => InstanceExecutionStatus.Failed
    };

    private static LatestExecutionSnapshot CreateLatestExecutionSnapshot(ExecutionResult? execution)
    {
        if (execution is null) return default;

        Guid? lastExecutedStepId = null;
        string? lastExecutedCompositePath = null;
        Guid? lastProblemStepId = null;
        string? lastProblemCompositePath = null;
        string? lastProblemStandardError = null;
        int? lastProblemExitCode = null;
        foreach (var step in execution.Steps)
        {
            if (step.Status != StepExecutionStatus.NotRun)
            {
                lastExecutedStepId = step.StepId;
                lastExecutedCompositePath = step.CompositeContext?.FullDisplayName;
            }
            if (step.Status is not (StepExecutionStatus.Failed or StepExecutionStatus.Cancelled)) continue;
            lastProblemStepId = step.StepId;
            lastProblemCompositePath = step.CompositeContext?.FullDisplayName;
            lastProblemStandardError = step.StandardError;
            lastProblemExitCode = step.ExitCode;
        }

        return new LatestExecutionSnapshot(
            lastExecutedStepId,
            lastExecutedCompositePath,
            lastProblemStepId,
            lastProblemCompositePath,
            lastProblemStandardError,
            lastProblemExitCode);
    }

    private static string ResolveLastStepName(
        string targetKey,
        LatestExecutionSnapshot execution,
        IReadOnlyDictionary<(string TargetKey, Guid StepId), string> runtimeStepNamesByKey,
        IReadOnlyDictionary<string, string> lastRuntimeStepNamesByTargetKey)
    {
        if (!string.IsNullOrWhiteSpace(execution.LastProblemCompositePath))
            return CompactText(execution.LastProblemCompositePath, 160);
        if (execution.LastProblemStepId is Guid problemStepId)
            return runtimeStepNamesByKey.GetValueOrDefault((targetKey, problemStepId), "—");
        if (!string.IsNullOrWhiteSpace(execution.LastExecutedCompositePath))
            return CompactText(execution.LastExecutedCompositePath, 160);
        if (execution.LastExecutedStepId is Guid stepId)
            return runtimeStepNamesByKey.GetValueOrDefault((targetKey, stepId), "—");
        return lastRuntimeStepNamesByTargetKey.GetValueOrDefault(targetKey, "—");
    }

    private static string BuildShortRunMessage(
        InstanceExecutionStatus status,
        string? message,
        LatestExecutionSnapshot execution)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            if (!string.IsNullOrWhiteSpace(execution.LastProblemStandardError))
                message = execution.LastProblemStandardError;
            else if (execution.LastProblemExitCode is int exitCode)
                message = $"Bước cuối trả về exit code {exitCode}.";
        }

        message ??= status switch
        {
            InstanceExecutionStatus.Succeeded => "Hoàn tất.",
            InstanceExecutionStatus.Cancelled => "Đã hủy theo yêu cầu.",
            InstanceExecutionStatus.Unavailable => "Thiết bị không khả dụng.",
            _ => "Kịch bản không hoàn tất."
        };
        var normalized = string.Join(" ", message.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= LatestRunMessageLimit
            ? normalized
            : $"{normalized[..(LatestRunMessageLimit - 1)]}…";
    }

    private readonly record struct LatestExecutionSnapshot(
        Guid? LastExecutedStepId,
        string? LastExecutedCompositePath,
        Guid? LastProblemStepId,
        string? LastProblemCompositePath,
        string? LastProblemStandardError,
        int? LastProblemExitCode);

    private static string BuildRunDescription(
        ScriptAssignmentModeValue assignmentMode,
        ScriptDefinition defaultScript,
        IReadOnlyDictionary<string, ScriptDefinition> scriptsByInstance)
    {
        if (assignmentMode == ScriptAssignmentModeValue.OneScriptForAll)
            return CompactRunDescription($"Một kịch bản cho tất cả · {defaultScript.Name}");

        var distinctNames = scriptsByInstance.Values
            .Select(script => script.Name)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        var visibleNames = distinctNames
            .Take(RunDescriptionVisibleScriptLimit)
            .Select(name => CompactText(name, RunDescriptionScriptNameLimit));
        var description = $"Kịch bản riêng theo thiết bị · {string.Join(", ", visibleNames)}";
        var remainingCount = distinctNames.Count - RunDescriptionVisibleScriptLimit;
        if (remainingCount > 0) description += $" · +{remainingCount} kịch bản khác";
        return CompactRunDescription(description);
    }

    private static string CompactRunDescription(string value) => CompactText(value, LatestRunDescriptionLimit);

    private static string CompactText(string value, int limit)
    {
        var normalized = string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length == 0) return "—";
        return normalized.Length <= limit ? normalized : $"{normalized[..(limit - 1)]}…";
    }

    private void ClearLatestRunResult()
    {
        if (RecentRuns.Count == 0 ||
            !confirmationService.Confirm("Xóa toàn bộ kết quả gần đây?", "Xác nhận xóa kết quả")) return;
        RecentRuns.Clear();
        LatestRunResult = null;
        SelectedRecentRunResult = null;
        OnPropertyChanged(nameof(HasRecentRuns));
        OnPropertyChanged(nameof(HasNoRecentRuns));
        StatusMessage = "Đã xóa kết quả gần đây.";
    }

    private void EnsureDynamicSession()
    {
        if (dynamicSessionUniverse.Count > 0 &&
            (executionSessions.Count > 0 || dynamicSessionAdmitted.Count < dynamicSessionUniverse.Count)) return;
        dynamicSessionUniverse.Clear();
        dynamicSessionAdmitted.Clear();
        foreach (var target in RunTargets.Where(item => item.IsRunning)) dynamicSessionUniverse.Add(target.TargetKey);
    }

    private void Stop()
    {
        StatusMessage = "Đang dừng tất cả nhóm chạy…";
        foreach (var pair in executionSessions.ToList())
        {
            pair.Value.StopAllTargets(targetKey =>
            {
                if (instanceRunsByKey.TryGetValue((pair.Key, targetKey), out var item)) item.RequestStop();
            });
        }
        StopSelectedActiveInstancesCommand.RaiseCanExecuteChanged();
    }

    public async Task StopAllForSafeShutdownAsync()
    {
        if (!isSafeShutdownRequested)
        {
            isSafeShutdownRequested = true;
            RaiseCommandStates();
        }

        if (!IsExecuting) return;

        Stop();
        var terminalCompletion = executionTerminalCompletion ??
            throw new InvalidOperationException("Active execution is missing its terminal completion signal.");
        await terminalCompletion.Task;
    }

    internal void ResumeAfterCancelledSafeShutdown()
    {
        if (!isSafeShutdownRequested) return;
        isSafeShutdownRequested = false;
        RaiseCommandStates();
    }

    private void StopSelectedActiveInstances()
    {
        var selected = ActiveInstanceRuns.Where(item => item.IsSelected && item.CanStop).ToList();
        var acceptedCount = 0;
        foreach (var item in selected)
        {
            if (!executionSessions.TryGetValue(item.LaunchGroupId, out var session) ||
                !session.StopTarget(item.TargetKey, () => item.RequestStop())) continue;
            acceptedCount++;
        }
        StatusMessage = $"Đang dừng {acceptedCount} thiết bị đã chọn…";
    }

    private void OnActiveInstanceSelectionChanged(object? sender, EventArgs args) =>
        StopSelectedActiveInstancesCommand.RaiseCanExecuteChanged();

    private void StopGroup(Guid groupId)
    {
        if (!executionSessions.TryGetValue(groupId, out var session)) return;
        session.StopAllTargets(targetKey =>
        {
            if (instanceRunsByKey.TryGetValue((groupId, targetKey), out var item)) item.RequestStop();
        });
        var groupName = ActiveLaunchGroups.FirstOrDefault(item => item.LaunchGroupId == groupId)?.DisplayName ?? "nhóm đã chọn";
        StatusMessage = $"Đang dừng {groupName}…";
    }

    private bool StopInstance(Guid groupId, string targetKey)
    {
        if (!executionSessions.TryGetValue(groupId, out var session) ||
            !session.StopTarget(targetKey, () =>
            {
                if (instanceRunsByKey.TryGetValue((groupId, targetKey), out var item)) item.RequestStop();
            }))
            return false;
        StatusMessage = $"Đang dừng thiết bị {targetKey}…";
        StopSelectedActiveInstancesCommand.RaiseCanExecuteChanged();
        return true;
    }

    private void ApplyExecutionUpdate(InstanceExecutionUpdate update)
    {
        if (!executionSessions.ContainsKey(update.LaunchGroupId) &&
            !activeInstanceGroups.Values.Contains(update.LaunchGroupId)) return;
        if (!instanceRunsByKey.TryGetValue((update.LaunchGroupId, update.TargetKey), out var instance)) return;
        var previousStatus = instance.Status;
        var changes = instance.ApplyAndGetChanges(update);
        if (changes.StatusChanged)
        {
            var previousRunningCount = runningInstanceCount;
            var previousWaitingCount = waitingInstanceCount;
            UpdateActiveStatusCount(previousStatus, instance.Status);
            if (previousRunningCount != runningInstanceCount)
                OnPropertyChanged(nameof(RunningInstanceCount));
            if (previousWaitingCount != waitingInstanceCount)
                OnPropertyChanged(nameof(WaitingInstanceCount));
        }
        if (changes.CanStopChanged)
            StopSelectedActiveInstancesCommand.RaiseCanExecuteChanged();
    }

    private void UpdateActiveStatusCount(InstanceExecutionStatus previousStatus, InstanceExecutionStatus currentStatus)
    {
        if (previousStatus == currentStatus) return;
        AdjustActiveStatusCount(previousStatus, -1);
        AdjustActiveStatusCount(currentStatus, 1);
    }

    private void AdjustActiveStatusCount(InstanceExecutionStatus status, int delta)
    {
        if (status == InstanceExecutionStatus.Running)
            runningInstanceCount += delta;
        else if (status is InstanceExecutionStatus.Queued or InstanceExecutionStatus.WaitingForLaunch)
            waitingInstanceCount += delta;
    }

    private void SetExecutionAggregateState()
    {
        var hasActiveExecution = executionSessions.Count > 0 || activeInstanceGroups.Count > 0;
        TaskCompletionSource? completed = null;
        if (hasActiveExecution)
        {
            executionTerminalCompletion ??=
                new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        else
        {
            completed = executionTerminalCompletion;
            executionTerminalCompletion = null;
        }

        IsExecuting = hasActiveExecution;
        NotifyRemainingRunTargetStateChanged();
        OnPropertyChanged(nameof(RunningInstanceCount));
        OnPropertyChanged(nameof(WaitingInstanceCount));
        OnPropertyChanged(nameof(ActiveLaunchGroupCount));
        StopGroupCommand.RaiseCanExecuteChanged();
        RaiseCommandStates();
        completed?.TrySetResult();
    }

    private bool CanRun()
    {
        var targets = ResolveRequestedTargets();
        return !isSafeShutdownRequested && CanDiscoverTargets && !IsCapturing && AreCurrentEditorInputsValid &&
            targets.Count > 0 && TargetsHaveConfiguredProviders(targets) &&
            ValidateRunConfiguration() is null && AssignedScriptsHaveSteps();
    }

    private bool CanTestCurrentStep()
    {
        if (isSafeShutdownRequested || IsCapturing || IsEditorPersistenceBusy || isStepMutationBusy ||
            StepEditorMode is not (RegularStepEditorMode.Create or RegularStepEditorMode.Edit) ||
            HasInvalidRegularEditorDraft || !TryGetRunnableEditorTestTarget(out var target)) return false;
        try
        {
            var draft = CreateStep(SelectedStep?.Id);
            if (!draft.IsEnabled || draft is NoteStep) return false;
            stepCommandBuilder.Validate(draft);
            if (target is AndroidAdbDevice android)
            {
                if (adbCommandBuilder is null || !AndroidScriptCapabilities.IsSupported(draft)) return false;
                _ = adbCommandBuilder.BuildPreview(draft, AdbPath, android.Serial);
            }
            else if (target is MemuInstance memu)
                _ = stepCommandBuilder.BuildPreview(draft, MemucPath, memu.Index);
            else return false;
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NotSupportedException)
        {
            return false;
        }
    }

    private bool CanRunAllRemaining()
    {
        if (isSafeShutdownRequested || !CanDiscoverTargets || IsCapturing || !AreCurrentEditorInputsValid ||
            ValidateRunConfiguration() is not null)
            return false;
        var remaining = ResolveAllRemainingTargetCandidates();
        var scripts = ResolveAssignedScripts(remaining);
        return remaining.Count > 0 && TargetsHaveConfiguredProviders(remaining) &&
            scripts is not null && scripts.Values.All(ScriptHasContent);
    }

    private IReadOnlyList<IExecutionTarget> ResolveAllRemainingTargetCandidates()
    {
        var startNewSession = executionSessions.Count == 0 &&
                              (dynamicSessionUniverse.Count == 0 || dynamicSessionAdmitted.Count >= dynamicSessionUniverse.Count);
        return RunTargets
            .Where(item => item.IsRunning && !activeInstanceGroups.ContainsKey(item.TargetKey) &&
                           (startNewSession || dynamicSessionUniverse.Contains(item.TargetKey) &&
                            !dynamicSessionAdmitted.Contains(item.TargetKey)))
            .Select(item => item.Model)
            .ToList();
    }

    private bool TargetsHaveConfiguredProviders(IEnumerable<IExecutionTarget> targets) =>
        targets.All(target => target.Kind switch
        {
            DeviceKind.MEmu => IsPathValid,
            DeviceKind.AndroidAdb => IsAdbPathValid,
            _ => false
        });

    private static int CountRawShellSteps(
        ScriptDefinition script,
        IReadOnlyDictionary<Guid, ScriptDefinition> library) => script.Kind == ScriptKind.Regular
        ? script.Steps.Count(step => step.IsEnabled && step is AndroidShellStep)
        : script.CompositeItems.OfType<ScriptReferenceItem>()
            .Where(reference => reference.IsEnabled && library.ContainsKey(reference.ScriptId))
            .Sum(reference => CountRawShellSteps(library[reference.ScriptId], library));

}
