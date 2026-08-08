using System.Collections.ObjectModel;
using System.Windows;
using MEmuScriptStudio.App.ViewModels;
using MEmuScriptStudio.Core.Android;
using MEmuScriptStudio.Core.MEmu;
using MEmuScriptStudio.Core.Models;

namespace MEmuScriptStudio.App.Services;

public interface IAndroidApplicationPickerService
{
    Task<AndroidApplicationInfo?> SelectAsync(
        string adbPath,
        string serial,
        AndroidApplicationInfo? currentSelection,
        CancellationToken cancellationToken,
        Action<string, string?>? aliasChanged = null);
}

public sealed class AndroidApplicationPickerService(
    IAndroidApplicationService applicationService,
    IAndroidForegroundApplicationService foregroundApplicationService,
    ISettingsStore settingsStore,
    IFileDialogService fileDialogService,
    IAndroidApplicationLibraryTransferService applicationLibraryTransferService,
    IApplicationNameImportConflictService importConflictService)
    : IAndroidApplicationPickerService
{
    public async Task<AndroidApplicationInfo?> SelectAsync(
        string adbPath,
        string serial,
        AndroidApplicationInfo? currentSelection,
        CancellationToken cancellationToken,
        Action<string, string?>? aliasChanged = null)
    {
        var settings = await settingsStore.LoadAsync(cancellationToken);
        var viewModel = new AndroidApplicationPickerViewModel(
            applicationService,
            adbPath,
            serial,
            currentSelection,
            settings.ApplicationDisplayNames,
            settings,
            settingsStore,
            foregroundApplicationService,
            fileDialogService,
            applicationLibraryTransferService,
            importConflictService,
            aliasChanged);
        var window = new ApplicationPickerWindow(viewModel)
        {
            Owner = Application.Current?.MainWindow
        };
        await viewModel.RefreshAsync(cancellationToken);
        if (window.ShowDialog() != true) return null;
        return viewModel.CreateSelection();
    }
}

public sealed class AndroidApplicationPickerViewModel(
    IAndroidApplicationService applicationService,
    string adbPath,
    string serial,
    AndroidApplicationInfo? currentSelection = null,
    IReadOnlyDictionary<string, string>? savedAliases = null,
    ApplicationSettings? settings = null,
    ISettingsStore? settingsStore = null,
    IAndroidForegroundApplicationService? foregroundApplicationService = null,
    IFileDialogService? fileDialogService = null,
    IAndroidApplicationLibraryTransferService? applicationLibraryTransferService = null,
    IApplicationNameImportConflictService? importConflictService = null,
    Action<string, string?>? aliasChanged = null) : ObservableObject, IApplicationPickerViewModel
{
    private IReadOnlyList<AndroidApplicationInfo> allApplications = [];
    private string searchText = string.Empty;
    private bool isBusy;
    private string statusMessage = "Đang tải danh sách ứng dụng Android…";
    private AndroidApplicationInfo? selectedApplication;
    private string manualDisplayName = currentSelection?.HasResolvedApplicationLabel == true
        ? currentSelection.ApplicationLabel!.Trim()
        : string.Empty;
    private bool shouldRestoreCurrentSelection = currentSelection is not null;
    private bool useCurrentSelectionOverlay = ShouldUseCurrentSelectionOverlay(
        currentSelection,
        savedAliases ?? settings?.ApplicationDisplayNames);
    private readonly Dictionary<string, string> savedAliases = new(
        savedAliases ?? settings?.ApplicationDisplayNames ?? new Dictionary<string, string>(),
        StringComparer.Ordinal);
    private readonly ApplicationSettings? applicationSettings = settings;

    public string WindowTitle => "Chọn ứng dụng Android / ADB";
    public bool ShowForegroundApplication => true;
    public bool ShowSaveNameAction => true;
    public bool ShowNameLibrary => true;
    public bool PersistNameOnSelect => true;
    public bool HasSelection => SelectedApplication is not null;
    public ObservableCollection<AndroidApplicationInfo> Applications { get; } = [];

    public string SearchText
    {
        get => searchText;
        set
        {
            if (SetProperty(ref searchText, value)) ApplyFilter();
        }
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
    public bool CanSaveName => !IsBusy && SelectedApplication is not null;
    public bool CanDeleteSavedName => !IsBusy && SelectedApplication is not null &&
        savedAliases.ContainsKey(SelectedApplication.PackageName);
    public string StatusMessage { get => statusMessage; private set => SetProperty(ref statusMessage, value); }
    public AndroidApplicationInfo? SelectedApplication
    {
        get => selectedApplication;
        set
        {
            var sameApplication = selectedApplication is not null && value is not null &&
                string.Equals(selectedApplication.PackageName, value.PackageName, StringComparison.Ordinal) &&
                string.Equals(selectedApplication.ActivityName, value.ActivityName, StringComparison.Ordinal);
            if (!SetProperty(ref selectedApplication, value)) return;
            if (!sameApplication)
                ManualDisplayName = value?.HasResolvedApplicationLabel == true
                    ? value.ApplicationLabel!.Trim()
                    : string.Empty;
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(CanSaveName));
            OnPropertyChanged(nameof(CanDeleteSavedName));
        }
    }

    public string ManualDisplayName
    {
        get => manualDisplayName;
        set => SetProperty(ref manualDisplayName, value);
    }

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusMessage = $"Đang tải ứng dụng từ Android {serial}…";
        try
        {
            allApplications = (await applicationService.GetApplicationsAsync(adbPath, serial, cancellationToken)).ToList();
            ApplyFilter();
            StatusMessage = allApplications.Count == 0
                ? "Không tìm thấy ứng dụng có launcher Activity."
                : $"Đã tải {allApplications.Count} ứng dụng có launcher Activity từ {serial}.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task UseForegroundApplicationAsync(CancellationToken cancellationToken)
    {
        if (IsBusy) return;
        if (foregroundApplicationService is null)
            throw new InvalidOperationException("Dịch vụ nhận ứng dụng Android đang mở chưa sẵn sàng.");

        IsBusy = true;
        StatusMessage = $"Đang nhận ứng dụng đang mở từ {serial}…";
        try
        {
            var foreground = await foregroundApplicationService.GetForegroundApplicationAsync(
                adbPath, serial, cancellationToken);
            var exact = allApplications.FirstOrDefault(application =>
                SameComponent(application, foreground));
            if (exact is null)
            {
                var packageLabel = allApplications.FirstOrDefault(application =>
                    string.Equals(application.PackageName, foreground.PackageName, StringComparison.Ordinal) &&
                    application.HasResolvedApplicationLabel)?.ApplicationLabel;
                foreground = foreground with { ApplicationLabel = packageLabel };
                allApplications = allApplications.Append(foreground)
                    .OrderBy(application => application.PackageName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(application => application.ActivityName, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            else
            {
                foreground = exact;
            }

            SearchText = string.Empty;
            ApplyFilter();
            SelectedApplication = Applications.First(application => SameComponent(application, foreground));
            ManualDisplayName = SelectedApplication.HasResolvedApplicationLabel
                ? SelectedApplication.ApplicationLabel!.Trim()
                : string.Empty;
            StatusMessage = $"Đã nhận {foreground.PackageName}/{foreground.ActivityName}.";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            StatusMessage = exception.Message;
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task SaveNameAsync(CancellationToken cancellationToken)
    {
        if (IsBusy) return;
        var selected = SelectedApplication ??
            throw new InvalidOperationException("Hãy chọn ứng dụng trước khi lưu tên.");
        if (settingsStore is null)
            throw new InvalidOperationException("Dịch vụ lưu tên ứng dụng Android chưa sẵn sàng.");

        var displayName = ManualDisplayName.Trim();
        if (displayName.Length == 0)
        {
            await DeleteSavedNameAsync(cancellationToken);
            return;
        }
        var candidate = new Dictionary<string, string>(savedAliases, StringComparer.Ordinal);
        candidate[selected.PackageName] = displayName;

        IsBusy = true;
        try
        {
            await PersistAliasesAsync(candidate, cancellationToken);
            DisableCurrentSelectionOverlay(selected.PackageName);
            RefreshApplicationDisplay(selected.PackageName, selected.ActivityName);
            ManualDisplayName = displayName;
            aliasChanged?.Invoke(selected.PackageName, displayName);
            StatusMessage = $"Đã lưu tên '{displayName}' cho {selected.PackageName}.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task DeleteSavedNameAsync(CancellationToken cancellationToken)
    {
        if (IsBusy) return;
        var selected = SelectedApplication ??
            throw new InvalidOperationException("Hãy chọn ứng dụng trước khi xóa tên.");
        if (!savedAliases.ContainsKey(selected.PackageName))
        {
            DisableCurrentSelectionOverlay(selected.PackageName);
            RefreshApplicationDisplay(selected.PackageName, selected.ActivityName);
            var fallbackWithoutAlias = SelectedApplication?.HasResolvedApplicationLabel == true
                ? SelectedApplication.ApplicationLabel!.Trim()
                : null;
            ManualDisplayName = fallbackWithoutAlias ?? string.Empty;
            aliasChanged?.Invoke(selected.PackageName, fallbackWithoutAlias);
            StatusMessage = $"{selected.PackageName} không có tên tùy chỉnh; đã dùng tên Android hiện có.";
            return;
        }

        IsBusy = true;
        try
        {
            var candidate = new Dictionary<string, string>(savedAliases, StringComparer.Ordinal);
            candidate.Remove(selected.PackageName);
            await PersistAliasesAsync(candidate, cancellationToken);
            DisableCurrentSelectionOverlay(selected.PackageName);
            RefreshApplicationDisplay(selected.PackageName, selected.ActivityName);
            var fallback = SelectedApplication?.HasResolvedApplicationLabel == true
                ? SelectedApplication.ApplicationLabel!.Trim()
                : null;
            ManualDisplayName = fallback ?? string.Empty;
            aliasChanged?.Invoke(selected.PackageName, fallback);
            StatusMessage = $"Đã xóa tên đã lưu cho {selected.PackageName}.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ImportNamesAsync(CancellationToken cancellationToken)
    {
        if (IsBusy) return;
        EnsureLibraryServices();
        var path = fileDialogService!.SelectAndroidApplicationLibraryImportPath();
        if (path is null) return;

        IsBusy = true;
        try
        {
            var imported = await applicationLibraryTransferService!.ImportAsync(path, cancellationToken);
            var candidate = new Dictionary<string, string>(savedAliases, StringComparer.Ordinal);
            var applied = new List<AndroidApplicationLibraryEntry>();
            var added = 0;
            var overwritten = 0;
            var skipped = 0;
            foreach (var entry in imported.OrderBy(entry => entry.PackageName, StringComparer.Ordinal))
            {
                if (!candidate.TryGetValue(entry.PackageName, out var current))
                {
                    candidate[entry.PackageName] = entry.FriendlyName;
                    applied.Add(entry);
                    added++;
                    continue;
                }
                if (string.Equals(current, entry.FriendlyName, StringComparison.Ordinal))
                {
                    skipped++;
                    continue;
                }

                var resolution = importConflictService!.Resolve(
                    entry.PackageName, current, entry.FriendlyName);
                if (resolution == ApplicationNameImportConflictResolution.Cancel)
                {
                    StatusMessage = "Đã hủy nhập thư viện Android; không có thay đổi nào được lưu.";
                    return;
                }
                if (resolution == ApplicationNameImportConflictResolution.Skip)
                {
                    skipped++;
                    continue;
                }
                candidate[entry.PackageName] = entry.FriendlyName;
                applied.Add(entry);
                overwritten++;
            }

            if (applied.Count > 0)
            {
                var selectedPackage = SelectedApplication?.PackageName;
                var selectedActivity = SelectedApplication?.ActivityName;
                await PersistAliasesAsync(candidate, cancellationToken);
                AddImportedCandidates(applied);
                RefreshApplicationDisplay(selectedPackage, selectedActivity);
                ManualDisplayName = SelectedApplication?.HasResolvedApplicationLabel == true
                    ? SelectedApplication.ApplicationLabel!.Trim()
                    : string.Empty;
                foreach (var entry in applied)
                {
                    DisableCurrentSelectionOverlay(entry.PackageName);
                    aliasChanged?.Invoke(entry.PackageName, entry.FriendlyName);
                }
            }
            StatusMessage = $"Đã nhập {added} tên mới, ghi đè {overwritten}, bỏ qua {skipped}.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ExportNamesAsync(CancellationToken cancellationToken)
    {
        if (IsBusy) return;
        EnsureLibraryServices();
        var path = fileDialogService!.SelectAndroidApplicationLibraryExportPath(
            "thu-vien-ung-dung-android.androidappnames");
        if (path is null) return;

        IsBusy = true;
        try
        {
            var entries = savedAliases
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair =>
                {
                    var application = allApplications.Where(item =>
                            string.Equals(item.PackageName, pair.Key, StringComparison.Ordinal))
                        .OrderBy(item => item.ActivityName, StringComparer.Ordinal)
                        .FirstOrDefault();
                    return new AndroidApplicationLibraryEntry(
                        pair.Key,
                        application?.ActivityName ?? string.Empty,
                        pair.Value);
                })
                .ToList();
            await applicationLibraryTransferService!.ExportAsync(path, entries, cancellationToken);
            StatusMessage = $"Đã xuất {entries.Count} tên ứng dụng Android.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public AndroidApplicationInfo? CreateSelection()
    {
        if (SelectedApplication is null) return null;
        return SelectedApplication with
        {
            ApplicationLabel = string.IsNullOrWhiteSpace(ManualDisplayName)
                ? null
                : ManualDisplayName.Trim()
        };
    }

    private void ApplyFilter()
    {
        var filter = SearchText.Trim();
        var displayedApplications = allApplications.Select(ApplyDisplayNameOverlay).ToList();
        var filtered = string.IsNullOrEmpty(filter)
            ? displayedApplications
            : displayedApplications.Where(application =>
                application.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                application.PackageName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                application.ActivityName.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();

        Applications.Clear();
        foreach (var application in filtered) Applications.Add(application);
        var selection = selectedApplication is null
            ? null
            : Applications.FirstOrDefault(application =>
                string.Equals(application.PackageName, selectedApplication.PackageName, StringComparison.Ordinal) &&
                string.Equals(application.ActivityName, selectedApplication.ActivityName, StringComparison.Ordinal));
        if (selection is null && shouldRestoreCurrentSelection && currentSelection is not null)
        {
            selection = Applications.FirstOrDefault(application =>
                string.Equals(application.PackageName, currentSelection.PackageName, StringComparison.Ordinal) &&
                (string.IsNullOrWhiteSpace(currentSelection.ActivityName) ||
                 string.Equals(application.ActivityName, currentSelection.ActivityName, StringComparison.Ordinal)));
            shouldRestoreCurrentSelection = false;
            SelectedApplication = selection ?? Applications.FirstOrDefault();
            return;
        }
        SelectedApplication = selection ?? Applications.FirstOrDefault();
    }

    private AndroidApplicationInfo ApplyDisplayNameOverlay(AndroidApplicationInfo application)
    {
        if (savedAliases.TryGetValue(application.PackageName, out var alias) && !string.IsNullOrWhiteSpace(alias))
            return application with { ApplicationLabel = alias.Trim() };
        if (useCurrentSelectionOverlay && currentSelection is not null &&
            string.Equals(application.PackageName, currentSelection.PackageName, StringComparison.Ordinal))
            return application with { ApplicationLabel = currentSelection.ApplicationLabel!.Trim() };
        return application;
    }

    private void RefreshApplicationDisplay(string? packageName, string? activityName)
    {
        ApplyFilter();
        if (packageName is null) return;
        var selection = Applications.FirstOrDefault(application =>
                string.Equals(application.PackageName, packageName, StringComparison.Ordinal) &&
                string.Equals(application.ActivityName, activityName, StringComparison.Ordinal))
            ?? Applications.FirstOrDefault(application =>
                string.Equals(application.PackageName, packageName, StringComparison.Ordinal));
        if (selection is null && !string.IsNullOrWhiteSpace(SearchText))
        {
            SearchText = string.Empty;
            selection = Applications.FirstOrDefault(application =>
                    string.Equals(application.PackageName, packageName, StringComparison.Ordinal) &&
                    string.Equals(application.ActivityName, activityName, StringComparison.Ordinal))
                ?? Applications.FirstOrDefault(application =>
                    string.Equals(application.PackageName, packageName, StringComparison.Ordinal));
        }
        SelectedApplication = selection;
    }

    private async Task PersistAliasesAsync(
        IReadOnlyDictionary<string, string> candidate,
        CancellationToken cancellationToken)
    {
        if (settingsStore is null)
            throw new InvalidOperationException("Dịch vụ lưu tên ứng dụng Android chưa sẵn sàng.");
        await settingsStore.UpdateAsync(currentSettings =>
        {
            currentSettings.ApplicationDisplayNames.Clear();
            foreach (var pair in candidate)
                currentSettings.ApplicationDisplayNames[pair.Key] = pair.Value;
        }, cancellationToken);

        savedAliases.Clear();
        applicationSettings?.ApplicationDisplayNames.Clear();
        foreach (var pair in candidate)
        {
            savedAliases[pair.Key] = pair.Value;
            if (applicationSettings is not null)
                applicationSettings.ApplicationDisplayNames[pair.Key] = pair.Value;
        }
        OnPropertyChanged(nameof(CanDeleteSavedName));
    }

    private void AddImportedCandidates(IEnumerable<AndroidApplicationLibraryEntry> entries)
    {
        var updated = allApplications.ToList();
        foreach (var entry in entries.Where(entry => !string.IsNullOrWhiteSpace(entry.ActivityName)))
        {
            var candidate = new AndroidApplicationInfo(entry.PackageName, entry.ActivityName);
            if (!updated.Any(application => SameComponent(application, candidate)))
                updated.Add(candidate);
        }
        allApplications = updated
            .OrderBy(application => application.PackageName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(application => application.ActivityName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void DisableCurrentSelectionOverlay(string packageName)
    {
        if (currentSelection is not null &&
            string.Equals(currentSelection.PackageName, packageName, StringComparison.Ordinal))
            useCurrentSelectionOverlay = false;
    }

    private void EnsureLibraryServices()
    {
        if (fileDialogService is null || applicationLibraryTransferService is null || importConflictService is null)
            throw new InvalidOperationException("Dịch vụ trao đổi thư viện ứng dụng Android chưa sẵn sàng.");
    }

    private static bool SameComponent(AndroidApplicationInfo first, AndroidApplicationInfo second) =>
        string.Equals(first.PackageName, second.PackageName, StringComparison.Ordinal) &&
        string.Equals(first.ActivityName, second.ActivityName, StringComparison.Ordinal);

    private static bool ShouldUseCurrentSelectionOverlay(
        AndroidApplicationInfo? selection,
        IReadOnlyDictionary<string, string>? aliases) =>
        selection?.HasResolvedApplicationLabel == true &&
        !(aliases?.TryGetValue(selection.PackageName, out var alias) == true &&
          !string.IsNullOrWhiteSpace(alias));
}
