using System.Collections.ObjectModel;
using System.Windows;
using MEmuScriptStudio.App.ViewModels;
using MEmuScriptStudio.Core.MEmu;
using MEmuScriptStudio.Core.Models;

namespace MEmuScriptStudio.App.Services;

public interface IApplicationPickerService
{
    Task<MemuApplicationInfo?> SelectAsync(string memucPath, int instanceIndex, CancellationToken cancellationToken);
}

public sealed class ApplicationPickerService(
    IMemuApplicationService applicationService,
    IMemuForegroundApplicationService foregroundApplicationService,
    ISettingsStore settingsStore,
    IFileDialogService fileDialogService,
    IApplicationNameTransferService applicationNameTransferService,
    IApplicationNameImportConflictService importConflictService) : IApplicationPickerService
{
    public async Task<MemuApplicationInfo?> SelectAsync(
        string memucPath,
        int instanceIndex,
        CancellationToken cancellationToken)
    {
        var settings = await settingsStore.LoadAsync(cancellationToken);
        var viewModel = new ApplicationPickerViewModel(
            applicationService,
            memucPath,
            instanceIndex,
            foregroundApplicationService,
            settings.ApplicationDisplayNames,
            settings,
            settingsStore,
            fileDialogService,
            applicationNameTransferService,
            importConflictService);
        var window = new ApplicationPickerWindow(viewModel)
        {
            Owner = Application.Current?.MainWindow
        };
        await viewModel.RefreshAsync(cancellationToken);
        if (window.ShowDialog() != true) return null;
        return viewModel.CreateSelection();
    }
}

public sealed class ApplicationPickerViewModel(
    IMemuApplicationService applicationService,
    string memucPath,
    int instanceIndex,
    IMemuForegroundApplicationService? foregroundApplicationService = null,
    IReadOnlyDictionary<string, string>? displayNameOverrides = null,
    ApplicationSettings? settings = null,
    ISettingsStore? settingsStore = null,
    IFileDialogService? fileDialogService = null,
    IApplicationNameTransferService? applicationNameTransferService = null,
    IApplicationNameImportConflictService? importConflictService = null) : ObservableObject
{
    private IReadOnlyList<MemuApplicationInfo> allApplications = [];
    private string searchText = string.Empty;
    private bool isBusy;
    private string statusMessage = "Đang tải danh sách ứng dụng…";
    private MemuApplicationInfo? selectedApplication;
    private string manualDisplayName = string.Empty;
    private readonly Dictionary<string, string> displayNameOverrides = new(
        displayNameOverrides ?? settings?.ApplicationDisplayNames ?? new Dictionary<string, string>(),
        StringComparer.Ordinal);
    private readonly ApplicationSettings? applicationSettings = settings;

    public ObservableCollection<MemuApplicationInfo> Applications { get; } = [];
    public string SearchText
    {
        get => searchText;
        set { if (SetProperty(ref searchText, value)) ApplyFilter(); }
    }
    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (!SetProperty(ref isBusy, value)) return;
            OnPropertyChanged(nameof(CanRefresh));
            OnPropertyChanged(nameof(CanSaveName));
            OnPropertyChanged(nameof(CanDeleteSavedName));
        }
    }
    public bool CanRefresh => !IsBusy;
    public bool CanSaveName => !IsBusy && SelectedApplication is not null && !string.IsNullOrWhiteSpace(ManualDisplayName);
    public bool CanDeleteSavedName => !IsBusy && SelectedApplication is not null &&
        displayNameOverrides.ContainsKey(SelectedApplication.PackageName);
    public string StatusMessage { get => statusMessage; private set => SetProperty(ref statusMessage, value); }
    public MemuApplicationInfo? SelectedApplication
    {
        get => selectedApplication;
        set
        {
            if (!SetProperty(ref selectedApplication, value)) return;
            ManualDisplayName = value is not null && displayNameOverrides.TryGetValue(value.PackageName, out var label)
                ? label
                : string.Empty;
            OnPropertyChanged(nameof(CanSaveName));
            OnPropertyChanged(nameof(CanDeleteSavedName));
        }
    }
    public string ManualDisplayName
    {
        get => manualDisplayName;
        set
        {
            if (SetProperty(ref manualDisplayName, value)) OnPropertyChanged(nameof(CanSaveName));
        }
    }

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusMessage = "Đang tải danh sách ứng dụng…";
        try
        {
            allApplications = (await applicationService.GetApplicationsAsync(memucPath, instanceIndex, cancellationToken)).ToList();
            ApplyFilter();
            if (allApplications.Count == 0)
            {
                StatusMessage = "Không tìm thấy ứng dụng có launcher Activity.";
            }
            else
            {
                var unknownLabelCount = allApplications
                    .Select(ApplyDisplayNameOverride)
                    .Count(application => !application.HasResolvedApplicationLabel);
                StatusMessage = unknownLabelCount == 0
                    ? $"Đã tải {allApplications.Count} ứng dụng."
                    : $"Đã tải {allApplications.Count} ứng dụng; {unknownLabelCount} ứng dụng chưa xác định được tên.";
            }
        }
        finally { IsBusy = false; }
    }

    public async Task UseForegroundApplicationAsync(CancellationToken cancellationToken)
    {
        if (IsBusy) return;
        if (foregroundApplicationService is null)
            throw new InvalidOperationException("Dịch vụ nhận ứng dụng đang mở chưa sẵn sàng.");

        IsBusy = true;
        StatusMessage = "Đang nhận ứng dụng đang mở…";
        try
        {
            var foreground = await foregroundApplicationService.GetForegroundApplicationAsync(
                memucPath, instanceIndex, cancellationToken);
            var existing = allApplications.FirstOrDefault(application =>
                string.Equals(application.PackageName, foreground.PackageName, StringComparison.Ordinal));
            if (existing is null)
            {
                allApplications = allApplications.Append(foreground)
                    .OrderBy(application => application.PackageName, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            else
            {
                foreground = existing with { ActivityName = foreground.ActivityName };
                var updated = allApplications.ToList();
                updated[updated.IndexOf(existing)] = foreground;
                allApplications = updated;
            }
            SearchText = string.Empty;
            ApplyFilter();
            SelectedApplication = Applications.First(application =>
                string.Equals(application.PackageName, foreground.PackageName, StringComparison.Ordinal));
            StatusMessage = $"Đã nhận {foreground.PackageName}/{foreground.ActivityName}.";
        }
        finally { IsBusy = false; }
    }

    public MemuApplicationInfo? CreateSelection()
    {
        if (SelectedApplication is null) return null;
        return string.IsNullOrWhiteSpace(ManualDisplayName)
            ? SelectedApplication
            : SelectedApplication with { ApplicationLabel = ManualDisplayName.Trim() };
    }

    public async Task SaveNameAsync(CancellationToken cancellationToken)
    {
        if (IsBusy) return;
        var selected = SelectedApplication ?? throw new InvalidOperationException("Hãy chọn ứng dụng trước khi lưu tên.");
        var displayName = ManualDisplayName.Trim();
        if (displayName.Length == 0) throw new InvalidOperationException("Tên ứng dụng không được để trống.");

        IsBusy = true;
        try
        {
            var candidate = new Dictionary<string, string>(displayNameOverrides, StringComparer.Ordinal)
            {
                [selected.PackageName] = displayName
            };
            await PersistOverridesAsync(candidate, cancellationToken);
            RefreshApplicationDisplay(selected.PackageName, selected.ActivityName);
            StatusMessage = $"Đã lưu tên '{displayName}' cho {selected.PackageName}.";
        }
        finally { IsBusy = false; }
    }

    public async Task DeleteSavedNameAsync(CancellationToken cancellationToken)
    {
        if (IsBusy) return;
        var selected = SelectedApplication ?? throw new InvalidOperationException("Hãy chọn ứng dụng trước khi xóa tên.");
        if (!displayNameOverrides.ContainsKey(selected.PackageName))
        {
            StatusMessage = $"{selected.PackageName} chưa có tên đã lưu.";
            return;
        }

        IsBusy = true;
        try
        {
            var candidate = new Dictionary<string, string>(displayNameOverrides, StringComparer.Ordinal);
            candidate.Remove(selected.PackageName);
            await PersistOverridesAsync(candidate, cancellationToken);
            RefreshApplicationDisplay(selected.PackageName, selected.ActivityName);
            StatusMessage = $"Đã xóa tên đã lưu cho {selected.PackageName}.";
        }
        finally { IsBusy = false; }
    }

    public async Task ExportNamesAsync(CancellationToken cancellationToken)
    {
        if (IsBusy) return;
        EnsureLibraryServices();
        var path = fileDialogService!.SelectApplicationNameExportPath("thu-vien-ten-ung-dung.memuappnames");
        if (path is null) return;

        IsBusy = true;
        try
        {
            await applicationNameTransferService!.ExportAsync(path, displayNameOverrides, cancellationToken);
            StatusMessage = $"Đã xuất {displayNameOverrides.Count} tên ứng dụng.";
        }
        finally { IsBusy = false; }
    }

    public async Task ImportNamesAsync(CancellationToken cancellationToken)
    {
        if (IsBusy) return;
        EnsureLibraryServices();
        var path = fileDialogService!.SelectApplicationNameImportPath();
        if (path is null) return;

        IsBusy = true;
        try
        {
            var imported = await applicationNameTransferService!.ImportAsync(path, cancellationToken);
            var candidate = new Dictionary<string, string>(displayNameOverrides, StringComparer.Ordinal);
            var added = 0;
            var overwritten = 0;
            var skipped = 0;
            foreach (var pair in imported.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                if (!candidate.TryGetValue(pair.Key, out var current))
                {
                    candidate[pair.Key] = pair.Value;
                    added++;
                    continue;
                }

                if (string.Equals(current, pair.Value, StringComparison.Ordinal))
                {
                    skipped++;
                    continue;
                }

                var resolution = importConflictService!.Resolve(pair.Key, current, pair.Value);
                if (resolution == ApplicationNameImportConflictResolution.Cancel)
                {
                    StatusMessage = "Đã hủy nhập thư viện tên; không có thay đổi nào được lưu.";
                    return;
                }

                if (resolution == ApplicationNameImportConflictResolution.Skip)
                {
                    skipped++;
                    continue;
                }

                candidate[pair.Key] = pair.Value;
                overwritten++;
            }

            if (added > 0 || overwritten > 0)
            {
                var selectedPackage = SelectedApplication?.PackageName;
                var selectedActivity = SelectedApplication?.ActivityName;
                await PersistOverridesAsync(candidate, cancellationToken);
                RefreshApplicationDisplay(selectedPackage, selectedActivity);
            }

            StatusMessage = $"Đã nhập {added} tên mới, ghi đè {overwritten}, bỏ qua {skipped}.";
        }
        finally { IsBusy = false; }
    }

    private void ApplyFilter()
    {
        var filter = SearchText.Trim();
        var displayedApplications = allApplications.Select(ApplyDisplayNameOverride).ToList();
        var filtered = string.IsNullOrEmpty(filter)
            ? displayedApplications
            : displayedApplications.Where(application =>
                application.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                application.PackageName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                application.ActivityName.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
        Applications.Clear();
        foreach (var application in filtered) Applications.Add(application);
        SelectedApplication = Applications.FirstOrDefault();
    }

    private MemuApplicationInfo ApplyDisplayNameOverride(MemuApplicationInfo application) =>
        displayNameOverrides.TryGetValue(application.PackageName, out var label) && !string.IsNullOrWhiteSpace(label)
            ? application with { ApplicationLabel = label.Trim() }
            : application;

    private async Task PersistOverridesAsync(
        IReadOnlyDictionary<string, string> candidate,
        CancellationToken cancellationToken)
    {
        if (applicationSettings is null || settingsStore is null)
            throw new InvalidOperationException("Dịch vụ lưu thư viện tên ứng dụng chưa sẵn sàng.");

        var updatedSettings = new ApplicationSettings { MemucPath = applicationSettings.MemucPath };
        foreach (var pair in candidate) updatedSettings.ApplicationDisplayNames[pair.Key] = pair.Value;
        await settingsStore.SaveAsync(updatedSettings, cancellationToken);

        displayNameOverrides.Clear();
        applicationSettings.ApplicationDisplayNames.Clear();
        foreach (var pair in candidate)
        {
            displayNameOverrides[pair.Key] = pair.Value;
            applicationSettings.ApplicationDisplayNames[pair.Key] = pair.Value;
        }
        OnPropertyChanged(nameof(CanDeleteSavedName));
    }

    private void RefreshApplicationDisplay(string? packageName, string? activityName)
    {
        ApplyFilter();
        if (packageName is null) return;
        SelectedApplication = Applications.FirstOrDefault(application =>
                string.Equals(application.PackageName, packageName, StringComparison.Ordinal) &&
                string.Equals(application.ActivityName, activityName, StringComparison.Ordinal))
            ?? Applications.FirstOrDefault(application =>
                string.Equals(application.PackageName, packageName, StringComparison.Ordinal));
    }

    private void EnsureLibraryServices()
    {
        if (fileDialogService is null || applicationNameTransferService is null || importConflictService is null)
            throw new InvalidOperationException("Dịch vụ trao đổi thư viện tên ứng dụng chưa sẵn sàng.");
    }
}
