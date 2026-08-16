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
    private readonly IStartupIssueLogger? startupIssueLogger;
    private string memucPath = string.Empty;
    private string adbPath = string.Empty;
    private bool isInitializing = true;
    private string? initializationErrorMessage;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        IsInitializing = true;
        InitializationErrorMessage = null;
        StatusMessage = "Đang khởi tạo...";
        try
        {
            await InitializeMemuAsync(cancellationToken);
            try
            {
                await scriptSaveGate.WaitAsync(cancellationToken);
                try
                {
                    IReadOnlyList<ScriptDefinition> loaded;
                    try { loaded = await scriptStore.LoadAsync(cancellationToken); }
                    catch (ScriptDataRecoveryRequiredException exception)
                    {
                        LogInitializationIssue(exception);
                        SetScriptPersistenceBlocked(true);
                        var recover = confirmationService.Confirm(
                            $"Dữ liệu kịch bản bị lỗi đã được sao lưu tại:\n{exception.BackupPath}\n\n" +
                            "Khôi phục thư viện về trạng thái an toàn trống? Dữ liệu lỗi trong bản sao lưu sẽ được giữ nguyên.",
                            "Phục hồi dữ liệu kịch bản");
                        if (!recover)
                        {
                            StatusMessage = $"{StatusMessage} Thư viện bị khóa để bảo vệ dữ liệu lỗi tại '{exception.BackupPath}'. Khởi động lại và xác nhận phục hồi để tiếp tục chỉnh sửa.";
                            return;
                        }

                        await scriptStore.RecoverAsync(cancellationToken);
                        SetScriptPersistenceBlocked(false);
                        loaded = [];
                        StatusMessage = $"{StatusMessage} Đã phục hồi thư viện; dữ liệu lỗi vẫn được giữ tại '{exception.BackupPath}'.";
                    }
                    if (loaded.Count == 0)
                    {
                        var template = ScriptTemplateFactory.CreateRestartChrome();
                        var templateItem = new ScriptItemViewModel(template);
                        Scripts.Add(templateItem);
                        SelectedScript = templateItem;
                        try { await scriptStore.SaveAsync([template], cancellationToken); }
                        catch (Exception exception)
                        {
                            LogInitializationIssue(exception);
                            StatusMessage = $"{StatusMessage} Template đã được tạo trong phiên này nhưng không thể lưu ({exception.Message}).";
                        }
                    }
                    else
                    {
                        foreach (var script in loaded) Scripts.Add(new ScriptItemViewModel(script));
                    }
                    SelectedScript ??= Scripts.FirstOrDefault();
                    RefreshScriptCollections();
                    CommonRunScript = Scripts.FirstOrDefault(item => item.Id == configuredCommonScriptId) ?? SelectedScript;
                    ControlCenterSelectedScript ??= CommonRunScript ?? SelectedScript;
                }
                finally { scriptSaveGate.Release(); }
                if (StatusMessage == "Đã tìm thấy memuc.exe.") StatusMessage = "Sẵn sàng.";
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                LogInitializationIssue(exception);
                SetScriptPersistenceBlocked(scriptStore.IsWriteBlocked);
                StatusMessage = $"{StatusMessage} Không thể đọc kịch bản đã lưu ({exception.Message}).";
            }
        }
        finally
        {
            IsInitializing = false;
        }
    }

    private async Task InitializeMemuAsync(CancellationToken cancellationToken)
    {
        ApplicationSettings settings;
        string? warning = null;
        try
        {
            settings = await settingsStore.LoadAsync(cancellationToken);
            warning = settingsStore.RecoveryNotice;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            LogInitializationIssue(exception);
            settings = new ApplicationSettings();
            warning = $"Không thể đọc cấu hình đã lưu ({exception.Message}).";
        }

        applicationSettings = settings;
        ControlCenterLayout = ControlCenterLayoutSettings.Normalize(settings.ControlCenterLayout);
        ApplyRunSettings(settings.MultiInstanceRun);
        MemucPath = pathDiscovery.IsValidMemucPath(settings.MemucPath) ? settings.MemucPath! : pathDiscovery.FindMemucPath() ?? string.Empty;
        AdbPath = adbPathDiscovery?.IsValidAdbPath(settings.AdbPath) == true
            ? settings.AdbPath!
            : adbPathDiscovery?.FindAdbPath(MemucPath) ?? string.Empty;
        var discovery = adbPathDiscovery is null
            ? (IsPathValid ? "Đã tìm thấy memuc.exe." : "Chưa tìm thấy memuc.exe. Hãy chọn file thủ công.")
            : $"{(IsPathValid ? "Đã tìm thấy memuc.exe." : "Chưa tìm thấy memuc.exe.")} " +
              $"{(IsAdbPathValid ? "Đã tìm thấy adb.exe." : "Chưa tìm thấy adb.exe.")}";
        StatusMessage = warning is null ? discovery : $"{warning} {discovery}";
        if ((IsPathValid && !string.Equals(settings.MemucPath, MemucPath, StringComparison.OrdinalIgnoreCase)) ||
            (IsAdbPathValid && !string.Equals(settings.AdbPath, AdbPath, StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                await UpdateApplicationSettingsAsync(
                    current =>
                    {
                        if (IsPathValid) current.MemucPath = MemucPath;
                        if (IsAdbPathValid) current.AdbPath = AdbPath;
                    },
                    cancellationToken);
            }
            catch (Exception exception)
            {
                LogInitializationIssue(exception);
                StatusMessage = $"{StatusMessage} Không thể lưu đường dẫn ({exception.Message}).";
            }
        }
    }

    private void ApplyRunSettings(MultiInstanceRunSettings settings)
    {
        launchSpacingMode = settings.LaunchSpacingMode;
        fixedSpacingMilliseconds = settings.FixedSpacingMilliseconds;
        randomMinimumSpacingMilliseconds = settings.RandomMinimumSpacingMilliseconds;
        randomMaximumSpacingMilliseconds = settings.RandomMaximumSpacingMilliseconds;
        stopAllOnInvalidTarget = settings.StopAllOnInvalidTarget;
        scriptAssignmentMode = settings.ScriptAssignmentMode;
        configuredCommonScriptId = settings.CommonScriptId;
        OnPropertyChanged(nameof(LaunchSpacingMode));
        OnPropertyChanged(nameof(IsFixedSpacing));
        OnPropertyChanged(nameof(IsRandomSpacing));
        OnPropertyChanged(nameof(FixedSpacingMilliseconds));
        OnPropertyChanged(nameof(RandomMinimumSpacingMilliseconds));
        OnPropertyChanged(nameof(RandomMaximumSpacingMilliseconds));
        OnPropertyChanged(nameof(StopAllOnInvalidTarget));
        OnPropertyChanged(nameof(ScriptAssignmentMode));
        OnPropertyChanged(nameof(IsOneScriptForAll));
        OnPropertyChanged(nameof(IsPerInstanceScript));
        UpdateRunConfigurationState();
    }

    private async Task<string?> PersistRunSettingsAsync(
        string memucPath,
        string adbPath,
        MultiInstanceRunSettings snapshot)
    {
        try
        {
            await UpdateApplicationSettingsAsync(settings =>
            {
                settings.MemucPath = memucPath;
                settings.AdbPath = adbPath;
                var runSettings = settings.MultiInstanceRun;
                runSettings.LaunchSpacingMode = snapshot.LaunchSpacingMode;
                runSettings.FixedSpacingMilliseconds = snapshot.FixedSpacingMilliseconds;
                runSettings.RandomMinimumSpacingMilliseconds = snapshot.RandomMinimumSpacingMilliseconds;
                runSettings.RandomMaximumSpacingMilliseconds = snapshot.RandomMaximumSpacingMilliseconds;
                runSettings.StopAllOnInvalidTarget = snapshot.StopAllOnInvalidTarget;
                runSettings.ScriptAssignmentMode = snapshot.ScriptAssignmentMode;
                runSettings.CommonScriptId = snapshot.CommonScriptId;
                runSettings.ScriptAssignments.Clear();
                foreach (var pair in snapshot.ScriptAssignments) runSettings.ScriptAssignments[pair.Key] = pair.Value;
                runSettings.TargetScriptAssignments.Clear();
                foreach (var pair in snapshot.TargetScriptAssignments) runSettings.TargetScriptAssignments[pair.Key] = pair.Value;
            }, CancellationToken.None);
            return null;
        }
        catch (Exception exception) { return $"Không thể lưu cấu hình chạy ({exception.Message})."; }
    }

    private async Task UpdateApplicationSettingsAsync(
        Action<ApplicationSettings> update,
        CancellationToken cancellationToken)
    {
        applicationSettings = await settingsStore.UpdateAsync(update, cancellationToken);
    }

    public void ReportUnexpectedError(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var logPath = ApplicationErrorReporter.Report(exception, "CommandFailure");
        var logHint = string.IsNullOrWhiteSpace(logPath) ? string.Empty : $" Chi tiết: {logPath}";
        StatusMessage = $"Thao tác không hoàn tất ({exception.Message}). Hãy kiểm tra dữ liệu hoặc quyền truy cập.{logHint}";
    }

    public void ReportInitializationError(Exception exception, string? logPath)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var logHint = string.IsNullOrWhiteSpace(logPath) ? string.Empty : $" Chi tiết: {logPath}";
        InitializationErrorMessage = $"Không thể khởi tạo đầy đủ ({exception.Message}). Giao diện vẫn dùng được; hãy chọn lại memuc.exe sau khi kiểm tra cấu hình và quyền truy cập.{logHint}";
        StatusMessage = InitializationErrorMessage;
        IsInitializing = false;
    }

    private void LogInitializationIssue(Exception exception)
    {
        try { startupIssueLogger?.Report(exception); }
        catch { }
    }
}
