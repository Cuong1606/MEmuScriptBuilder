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

public sealed class ApplicationPickerService(IMemuApplicationService applicationService) : IApplicationPickerService
{
    public async Task<MemuApplicationInfo?> SelectAsync(
        string memucPath,
        int instanceIndex,
        CancellationToken cancellationToken)
    {
        var viewModel = new ApplicationPickerViewModel(applicationService, memucPath, instanceIndex);
        var window = new ApplicationPickerWindow(viewModel)
        {
            Owner = Application.Current?.MainWindow
        };
        await viewModel.RefreshAsync(cancellationToken);
        return window.ShowDialog() == true ? viewModel.SelectedApplication : null;
    }
}

public sealed class ApplicationPickerViewModel(
    IMemuApplicationService applicationService,
    string memucPath,
    int instanceIndex) : ObservableObject
{
    private IReadOnlyList<MemuApplicationInfo> allApplications = [];
    private string searchText = string.Empty;
    private bool isBusy;
    private string statusMessage = "Đang tải danh sách ứng dụng…";
    private MemuApplicationInfo? selectedApplication;

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
        }
    }
    public bool CanRefresh => !IsBusy;
    public string StatusMessage { get => statusMessage; private set => SetProperty(ref statusMessage, value); }
    public MemuApplicationInfo? SelectedApplication { get => selectedApplication; set => SetProperty(ref selectedApplication, value); }

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        StatusMessage = "Đang tải danh sách ứng dụng…";
        try
        {
            allApplications = await applicationService.GetApplicationsAsync(memucPath, instanceIndex, cancellationToken);
            ApplyFilter();
            StatusMessage = allApplications.Count == 0
                ? "Không tìm thấy ứng dụng có launcher Activity."
                : $"Đã tải {allApplications.Count} ứng dụng.";
        }
        finally { IsBusy = false; }
    }

    private void ApplyFilter()
    {
        var filter = SearchText.Trim();
        var filtered = string.IsNullOrEmpty(filter)
            ? allApplications
            : allApplications.Where(application =>
                application.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                application.PackageName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                application.ActivityName.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
        Applications.Clear();
        foreach (var application in filtered) Applications.Add(application);
        SelectedApplication = Applications.FirstOrDefault();
    }
}
