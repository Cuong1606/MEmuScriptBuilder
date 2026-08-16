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
    private readonly IMemuInstanceService instanceService;
    private readonly IMemucPathDiscovery pathDiscovery;
    private readonly IAndroidAdbDeviceService? androidDeviceService;
    private readonly IAdbPathDiscovery? adbPathDiscovery;
    private readonly AdbCommandBuilder? adbCommandBuilder;
    private readonly IApplicationPickerService applicationPickerService;
    private readonly IAndroidApplicationPickerService? androidApplicationPickerService;
    private readonly IAndroidDeviceAliasDialogService? androidDeviceAliasDialogService;
    private readonly IMemuInputCaptureService inputCaptureService;
    private readonly ITapCaptureOverlayService tapCaptureOverlayService;
    private readonly ISwipeCaptureOverlayService swipeCaptureOverlayService;
    private readonly IAndroidCoordinateCaptureDialogService? androidCoordinateCaptureDialogService;
    private readonly HashSet<string> discoveredTargetKeys = new(StringComparer.Ordinal);
    private bool isBusy;
    private bool isCapturing;
    private MemuInstance? selectedInstance;
    private EditorTargetItemViewModel? selectedEditorTarget;

    private async Task BrowseAsync()
    {
        var selectedPath = fileDialogService.SelectMemucPath(MemucPath);
        if (selectedPath is null) return;
        if (!pathDiscovery.IsValidMemucPath(selectedPath)) { StatusMessage = "File đã chọn không phải memuc.exe hợp lệ."; return; }
        MemucPath = selectedPath;
        InitializationErrorMessage = null;
        Instances.Clear();
        RemoveProviderTargets(DeviceKind.MEmu);
        RemoveEditorProviderTargets(DeviceKind.MEmu);
        try
        {
            await UpdateApplicationSettingsAsync(
                settings => settings.MemucPath = selectedPath,
                CancellationToken.None);
            StatusMessage = "Đã lưu đường dẫn memuc.exe.";
        }
        catch (Exception exception) { StatusMessage = $"Có thể dùng đường dẫn trong phiên này nhưng không thể lưu ({exception.Message})."; }
    }

    private async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            StatusMessage = "Đang đọc danh sách thiết bị MEmu và Android / ADB…";
        var previousEditorTarget = SelectedEditorTarget?.Model;
        var selectedTargets = RunTargets.Where(item => item.IsSelected).Select(item => item.TargetKey)
            .ToHashSet(StringComparer.Ordinal);
        var targets = new List<IExecutionTarget>();
        var messages = new List<string>();
        IReadOnlyList<MemuInstance> memuInstances = [];
        try
        {
            if (IsPathValid)
            {
                memuInstances = await instanceService.GetInstancesAsync(MemucPath, CancellationToken.None);
                targets.AddRange(memuInstances);
                messages.Add($"MEmu: {memuInstances.Count}");
            }
            else messages.Add("MEmu: chưa cấu hình");
        }
        catch (Exception exception) { messages.Add($"MEmu lỗi: {exception.Message}"); }

        try
        {
            if (IsAdbPathValid && androidDeviceService is not null)
            {
                var androidDevices = (await androidDeviceService.GetDevicesAsync(AdbPath, CancellationToken.None))
                    .Select(ApplyAndroidDeviceAlias)
                    .ToList();
                targets.AddRange(androidDevices);
                messages.Add($"Android / ADB: {androidDevices.Count}");
            }
            else messages.Add("Android / ADB: chưa cấu hình");
        }
        catch (Exception exception) { messages.Add($"Android / ADB lỗi: {exception.Message}"); }

        Instances.Clear();
        foreach (var instance in memuInstances) Instances.Add(instance);
        SynchronizeRunTargets(targets, selectedTargets);
        var editorSelectionLost = SynchronizeEditorTargets(targets, previousEditorTarget);
        StatusMessage = string.Join("; ", messages) + ".";
        if (editorSelectionLost)
            StatusMessage += " Thiết bị soạn thảo đã ngắt kết nối; hãy chọn lại sau khi làm mới.";
        }
        finally { IsBusy = false; }
    }

    private void SynchronizeRunTargets(IReadOnlyList<IExecutionTarget> instances, IReadOnlySet<string> selectedTargetKeys)
    {
        var targetsByKey = RunTargets.ToDictionary(item => item.TargetKey, StringComparer.Ordinal);
        var refreshedKeys = instances.Select(item => item.TargetKey).ToHashSet(StringComparer.Ordinal);
        discoveredTargetKeys.Clear();
        discoveredTargetKeys.UnionWith(refreshedKeys);

        foreach (var removed in RunTargets
                     .Where(item => !refreshedKeys.Contains(item.TargetKey) && !activeInstanceGroups.ContainsKey(item.TargetKey))
                     .ToList())
        {
            removed.SelectionChanged -= OnRunTargetSelectionChanged;
            removed.AssignmentChanged -= OnTargetAssignmentChanged;
            RunTargets.Remove(removed);
        }

        foreach (var instance in instances)
        {
            if (targetsByKey.TryGetValue(instance.TargetKey, out var existing))
            {
                existing.ReplaceModel(instance);
                existing.SetActive(activeInstanceGroups.ContainsKey(instance.TargetKey));
                var existingScript = Scripts.FirstOrDefault(item => item.Id == existing.AssignedScriptId);
                existing.SetAssignedScript(existingScript?.Id, existingScript?.Name, existingScript?.Model.Kind);
                continue;
            }

            var target = new InstanceTargetItemViewModel(instance) { IsSelected = selectedTargetKeys.Contains(instance.TargetKey) };
            target.SetActive(activeInstanceGroups.ContainsKey(instance.TargetKey));
            var assignedId = applicationSettings.MultiInstanceRun.TargetScriptAssignments.GetValueOrDefault(instance.TargetKey);
            if (assignedId == Guid.Empty && instance is MemuInstance memu)
                assignedId = applicationSettings.MultiInstanceRun.ScriptAssignments.GetValueOrDefault(memu.Index);
            var assignedScript = Scripts.FirstOrDefault(item => item.Id == assignedId);
            target.SetAssignedScript(assignedScript?.Id, assignedScript?.Name, assignedScript?.Model.Kind);
            target.SelectionChanged += OnRunTargetSelectionChanged;
            target.AssignmentChanged += OnTargetAssignmentChanged;
            RunTargets.Add(target);
        }
        var currentKeys = RunTargets.Select(item => item.TargetKey).ToHashSet(StringComparer.Ordinal);
        dynamicSessionUniverse.IntersectWith(currentKeys);
        dynamicSessionAdmitted.IntersectWith(currentKeys);
        RebuildRunTargetProjection(clearHiddenSelection: false);
        UpdateRunConfigurationState();
        UpdatePreview();
    }

    private void OnRunTargetSelectionChanged(object? sender, EventArgs args) => HandleRunTargetSelectionChanged();

    private bool SynchronizeEditorTargets(
        IReadOnlyList<IExecutionTarget> targets,
        IExecutionTarget? previousSelection)
    {
        var byKey = EditorTargets.ToDictionary(item => item.TargetKey, StringComparer.Ordinal);
        var refreshedKeys = targets.Select(target => target.TargetKey).ToHashSet(StringComparer.Ordinal);
        foreach (var removed in EditorTargets.Where(item => !refreshedKeys.Contains(item.TargetKey)).ToList())
            EditorTargets.Remove(removed);

        foreach (var target in targets)
        {
            if (byKey.TryGetValue(target.TargetKey, out var existing)) existing.ReplaceModel(target);
            else EditorTargets.Add(new EditorTargetItemViewModel(target));
        }

        var restored = previousSelection is null
            ? null
            : EditorTargets.FirstOrDefault(item => item.TargetKey == previousSelection.TargetKey);
        var selectionLost = previousSelection is AndroidAdbDevice && restored is null;
        if (restored is null && !selectionLost)
        {
            restored = EditorTargets.FirstOrDefault(item => item.Model is MemuInstance { IsRunning: true })
                ?? EditorTargets.FirstOrDefault(item => item.IsAvailable)
                ?? EditorTargets.FirstOrDefault();
        }
        SetSelectedEditorTarget(restored);
        return selectionLost;
    }

    private AndroidAdbDevice ApplyAndroidDeviceAlias(AndroidAdbDevice device) =>
        applicationSettings.AndroidDeviceAliases.TryGetValue(device.Serial, out var alias) &&
        !string.IsNullOrWhiteSpace(alias)
            ? device with { Alias = alias.Trim() }
            : device with { Alias = null };

    private bool CanEditAndroidDeviceAlias() =>
        !IsInitializing && !IsBusy && !IsCapturing &&
        SelectedEditorTarget?.Model is AndroidAdbDevice && androidDeviceAliasDialogService is not null;

    private async Task EditAndroidDeviceAliasAsync()
    {
        if (SelectedEditorTarget?.Model is not AndroidAdbDevice selected || androidDeviceAliasDialogService is null)
            return;

        var result = androidDeviceAliasDialogService.Edit(selected.Serial, selected.Alias);
        if (result is null) return;

        var alias = result.RemoveAlias || string.IsNullOrWhiteSpace(result.Alias)
            ? null
            : result.Alias.Trim();
        await UpdateApplicationSettingsAsync(settings =>
        {
            if (alias is null) settings.AndroidDeviceAliases.Remove(selected.Serial);
            else settings.AndroidDeviceAliases[selected.Serial] = alias;
        }, CancellationToken.None);

        foreach (var target in EditorTargets.Where(item => item.TargetKey == selected.TargetKey))
            target.ReplaceModel(selected with { Alias = alias });
        foreach (var target in RunTargets.Where(item => item.TargetKey == selected.TargetKey))
            target.ReplaceModel(selected with { Alias = alias });

        StatusMessage = alias is null
            ? $"Đã xóa alias cho Android {selected.Serial}."
            : $"Đã đổi tên hiển thị Android {selected.Serial} thành '{alias}'.";
        OnPropertyChanged(nameof(ShowAndroidDeviceAliasAction));
        RaiseCommandStates();
    }

    private async Task BrowseAdbAsync()
    {
        var selectedPath = fileDialogService.SelectAdbPath(AdbPath);
        if (selectedPath is null) return;
        if (adbPathDiscovery?.IsValidAdbPath(selectedPath) != true)
        {
            StatusMessage = "File đã chọn không phải adb.exe hợp lệ.";
            return;
        }
        AdbPath = selectedPath;
        InitializationErrorMessage = null;
        RemoveProviderTargets(DeviceKind.AndroidAdb);
        RemoveEditorProviderTargets(DeviceKind.AndroidAdb);
        try
        {
            await UpdateApplicationSettingsAsync(settings => settings.AdbPath = selectedPath, CancellationToken.None);
            StatusMessage = "Đã lưu đường dẫn adb.exe.";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Có thể dùng đường dẫn ADB trong phiên này nhưng không thể lưu ({exception.Message}).";
        }
    }

    private void RemoveProviderTargets(DeviceKind kind)
    {
        foreach (var target in RunTargets
                     .Where(item => item.DeviceKind == kind && !activeInstanceGroups.ContainsKey(item.TargetKey))
                     .ToList())
        {
            target.SelectionChanged -= OnRunTargetSelectionChanged;
            target.AssignmentChanged -= OnTargetAssignmentChanged;
            RunTargets.Remove(target);
            discoveredTargetKeys.Remove(target.TargetKey);
            dynamicSessionUniverse.Remove(target.TargetKey);
            dynamicSessionAdmitted.Remove(target.TargetKey);
        }
        RebuildRunTargetProjection(clearHiddenSelection: false);
        UpdateRunConfigurationState();
        UpdatePreview();
    }

    private void RemoveEditorProviderTargets(DeviceKind kind)
    {
        var selectedWasRemoved = SelectedEditorTarget?.DeviceKind == kind;
        foreach (var target in EditorTargets.Where(item => item.DeviceKind == kind).ToList())
            EditorTargets.Remove(target);
        if (selectedWasRemoved) SetSelectedEditorTarget(null);
    }

    private async Task SelectApplicationAsync()
    {
        if (SelectedEditorTarget?.Model is not { } target) return;
        var targetKind = EditorKind;
        IsCapturing = true;
        StatusMessage = "Đang tải danh sách ứng dụng…";
        try
        {
            switch (target)
            {
                case MemuInstance memu:
                {
                    var selected = await applicationPickerService.SelectAsync(MemucPath, memu.Index, CancellationToken.None);
                    if (selected is null) return;
                    EditorPackageName = selected.PackageName;
                    EditorApplicationDisplayName = selected.HasResolvedApplicationLabel
                        ? selected.DisplayName
                        : selected.PackageName;
                    if (targetKind == ScriptStepKind.OpenApp) EditorActivityName = selected.ActivityName;
                    StatusMessage = $"Đã chọn ứng dụng {selected.PackageName}.";
                    break;
                }
                case AndroidAdbDevice android when androidApplicationPickerService is not null:
                {
                    var currentFriendlyName = NormalizeOptionalDisplayName(EditorApplicationDisplayName);
                    if (string.Equals(currentFriendlyName, EditorPackageName?.Trim(), StringComparison.Ordinal))
                        currentFriendlyName = null;
                    var currentSelection = string.IsNullOrWhiteSpace(EditorPackageName) ||
                                           targetKind == ScriptStepKind.OpenApp && string.IsNullOrWhiteSpace(EditorActivityName)
                        ? null
                        : new AndroidApplicationInfo(
                            EditorPackageName,
                            targetKind == ScriptStepKind.OpenApp ? EditorActivityName : string.Empty,
                            currentFriendlyName);
                    var selected = await androidApplicationPickerService.SelectAsync(
                        AdbPath,
                        android.Serial,
                        currentSelection,
                        CancellationToken.None,
                        (packageName, friendlyName) =>
                        {
                            if (string.Equals(EditorPackageName?.Trim(), packageName, StringComparison.Ordinal))
                                EditorApplicationDisplayName = friendlyName?.Trim() ?? string.Empty;
                        });
                    if (selected is null) return;
                    EditorPackageName = selected.PackageName;
                    EditorApplicationDisplayName = selected.HasResolvedApplicationLabel
                        ? selected.ApplicationLabel!.Trim()
                        : string.Empty;
                    EditorActivityName = targetKind == ScriptStepKind.OpenApp ? selected.ActivityName : string.Empty;
                    StatusMessage = $"Đã chọn ứng dụng Android {selected.PackageName} từ {android.Serial}.";
                    break;
                }
            }
        }
        finally { IsCapturing = false; }
    }

    private bool CanSelectApplication()
    {
        if (IsInitializing || HasInitializationError || IsCapturing ||
            EditorKind is not (ScriptStepKind.ForceStop or ScriptStepKind.OpenApp))
            return false;

        return SelectedEditorTarget?.Model switch
        {
            MemuInstance { IsRunning: true } => IsPathValid,
            AndroidAdbDevice { ConnectionState: AndroidConnectionState.Device } =>
                IsAdbPathValid && androidApplicationPickerService is not null,
            _ => false
        };
    }

    private bool CanCapture(ScriptStepKind kind) =>
        !IsInitializing && !HasInitializationError && !IsCapturing && EditorKind == kind &&
        SelectedEditorTarget?.Model switch
        {
            MemuInstance { IsRunning: true, ProcessId: > 0, WindowHandle: > 0 } => IsPathValid,
            AndroidAdbDevice { ConnectionState: AndroidConnectionState.Device } =>
                IsAdbPathValid && androidCoordinateCaptureDialogService is not null,
            _ => false
        };

    private async Task CaptureTapAsync()
    {
        if (SelectedEditorTarget?.Model is not { } target) return;
        IsCapturing = true;
        try
        {
            CapturedTap? tap;
            if (target is AndroidAdbDevice android)
            {
                StatusMessage = "Đang mở ảnh chụp Android để chọn tọa độ Chạm…";
                tap = (await CaptureAndroidAsync(android, AndroidCoordinateCaptureMode.Tap))?.Tap;
            }
            else
            {
                StatusMessage = "Nhấp để chọn tọa độ Chạm, có thể nhấp lại để điều chỉnh. Nhấn Enter để xác nhận hoặc Esc để hủy.";
                using var overlay = tapCaptureOverlayService.Show();
                tap = await inputCaptureService.CaptureTapAsync(
                    MemucPath, (MemuInstance)target, overlay, CancellationToken.None);
            }
            if (tap is null) { StatusMessage = "Đã hủy lấy tọa độ."; return; }
            EditorX = tap.X;
            EditorY = tap.Y;
            StatusMessage = $"Đã lấy tọa độ chạm: X={tap.X}, Y={tap.Y}.";
        }
        catch (OperationCanceledException) { StatusMessage = "Đã hủy lấy tọa độ."; }
        catch (Exception exception) { StatusMessage = $"Không thể lấy tọa độ chạm: {CompactCaptureError(exception.Message)}"; }
        finally { IsCapturing = false; }
    }

    private async Task CaptureHoldAsync()
    {
        if (SelectedEditorTarget?.Model is not { } target) return;
        IsCapturing = true;
        try
        {
            CapturedTap? tap;
            if (target is AndroidAdbDevice android)
            {
                StatusMessage = "Đang mở ảnh chụp Android để chọn tọa độ Nhấn giữ…";
                tap = (await CaptureAndroidAsync(android, AndroidCoordinateCaptureMode.Hold))?.Tap;
            }
            else
            {
                StatusMessage = "Nhấp để chọn tọa độ Nhấn giữ, có thể nhấp lại để điều chỉnh. Nhấn Enter để xác nhận hoặc Esc để hủy.";
                using var overlay = tapCaptureOverlayService.Show();
                tap = await inputCaptureService.CaptureTapAsync(
                    MemucPath, (MemuInstance)target, overlay, CancellationToken.None);
            }
            if (tap is null) { StatusMessage = "Đã hủy chọn tọa độ nhấn giữ."; return; }
            EditorX = tap.X;
            EditorY = tap.Y;
            StatusMessage = $"Đã chọn tọa độ nhấn giữ: X={tap.X}, Y={tap.Y}.";
        }
        catch (OperationCanceledException) { StatusMessage = "Đã hủy chọn tọa độ nhấn giữ."; }
        catch (Exception exception) { StatusMessage = $"Không thể chọn tọa độ nhấn giữ: {CompactCaptureError(exception.Message)}"; }
        finally { IsCapturing = false; }
    }

    private async Task CaptureSwipeAsync()
    {
        if (SelectedEditorTarget?.Model is not { } target) return;
        IsCapturing = true;
        try
        {
            CapturedSwipe? swipe;
            if (target is AndroidAdbDevice android)
            {
                StatusMessage = "Đang mở ảnh chụp Android để chọn đường Vuốt…";
                swipe = (await CaptureAndroidAsync(android, AndroidCoordinateCaptureMode.Swipe))?.Swipe;
            }
            else
            {
                StatusMessage = "Chuột trái chọn điểm đầu, chuột phải chọn điểm cuối. Nhấn Enter để xác nhận hoặc Esc để hủy.";
                using var overlay = swipeCaptureOverlayService.Show();
                swipe = await inputCaptureService.CaptureSwipeAsync(
                    MemucPath, (MemuInstance)target, overlay, CancellationToken.None);
            }
            if (swipe is null) { StatusMessage = "Đã hủy chọn đường vuốt."; return; }
            EditorX = swipe.X1;
            EditorY = swipe.Y1;
            EditorX2 = swipe.X2;
            EditorY2 = swipe.Y2;
            StatusMessage = $"Đã chọn đường vuốt từ ({swipe.X1}, {swipe.Y1}) đến ({swipe.X2}, {swipe.Y2}).";
        }
        catch (OperationCanceledException) { StatusMessage = "Đã hủy chọn đường vuốt."; }
        catch (Exception exception) { StatusMessage = $"Không thể chọn đường vuốt: {CompactCaptureError(exception.Message)}"; }
        finally { IsCapturing = false; }
    }

    private Task<AndroidCoordinateCaptureResult?> CaptureAndroidAsync(
        AndroidAdbDevice target,
        AndroidCoordinateCaptureMode mode)
    {
        if (androidCoordinateCaptureDialogService is null)
            throw new InvalidOperationException("Dịch vụ lấy tọa độ Android chưa sẵn sàng.");
        if (!EditorTargets.Any(item => item.TargetKey == target.TargetKey && item.IsAvailable) ||
            SelectedEditorTarget?.TargetKey != target.TargetKey)
            throw new InvalidOperationException("Thiết bị Android đã mất khỏi danh sách soạn thảo. Hãy làm mới và chọn lại.");
        return androidCoordinateCaptureDialogService.CaptureAsync(AdbPath, target, mode, CancellationToken.None);
    }

    private static string CompactCaptureError(string value)
    {
        var normalized = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length == 0) normalized = "Thiết bị không khả dụng.";
        return normalized.Length <= 200 ? normalized : $"{normalized[..199]}…";
    }

}
