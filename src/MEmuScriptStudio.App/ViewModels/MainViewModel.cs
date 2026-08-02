using System.Collections.ObjectModel;
using MEmuScriptStudio.App.Services;
using MEmuScriptStudio.Core.MEmu;
using MEmuScriptStudio.Core.Models;

namespace MEmuScriptStudio.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly IMemuInstanceService instanceService;
    private readonly IMemucPathDiscovery pathDiscovery;
    private readonly ISettingsStore settingsStore;
    private readonly IFileDialogService fileDialogService;
    private string memucPath = string.Empty;
    private string statusMessage = "Đang đọc cấu hình…";
    private bool isBusy;

    public MainViewModel(
        IMemuInstanceService instanceService,
        IMemucPathDiscovery pathDiscovery,
        ISettingsStore settingsStore,
        IFileDialogService fileDialogService)
    {
        this.instanceService = instanceService;
        this.pathDiscovery = pathDiscovery;
        this.settingsStore = settingsStore;
        this.fileDialogService = fileDialogService;
        BrowseCommand = new AsyncCommand(BrowseAsync, () => !IsBusy);
        RefreshCommand = new AsyncCommand(RefreshAsync, () => !IsBusy && IsPathValid);
    }

    public ObservableCollection<MemuInstance> Instances { get; } = [];
    public AsyncCommand BrowseCommand { get; }
    public AsyncCommand RefreshCommand { get; }

    public string MemucPath
    {
        get => memucPath;
        private set
        {
            if (!SetProperty(ref memucPath, value)) return;
            OnPropertyChanged(nameof(IsPathValid));
            RefreshCommand.RaiseCanExecuteChanged();
        }
    }

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetProperty(ref statusMessage, value);
    }

    public bool IsPathValid => pathDiscovery.IsValidMemucPath(MemucPath);

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (!SetProperty(ref isBusy, value)) return;
            BrowseCommand.RaiseCanExecuteChanged();
            RefreshCommand.RaiseCanExecuteChanged();
        }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var settings = await settingsStore.LoadAsync(cancellationToken);
        MemucPath = pathDiscovery.IsValidMemucPath(settings.MemucPath)
            ? settings.MemucPath!
            : pathDiscovery.FindMemucPath() ?? string.Empty;

        StatusMessage = IsPathValid
            ? "Đã tìm thấy memuc.exe. Chọn Làm mới để đọc danh sách máy ảo."
            : "Chưa tìm thấy memuc.exe. Hãy chọn file cài đặt thủ công.";

        if (IsPathValid && !string.Equals(settings.MemucPath, MemucPath, StringComparison.OrdinalIgnoreCase))
        {
            await settingsStore.SaveAsync(new ApplicationSettings { MemucPath = MemucPath }, cancellationToken);
        }
    }

    private async Task BrowseAsync()
    {
        var selectedPath = fileDialogService.SelectMemucPath(MemucPath);
        if (selectedPath is null) return;
        if (!pathDiscovery.IsValidMemucPath(selectedPath))
        {
            StatusMessage = "File đã chọn không phải memuc.exe hợp lệ.";
            return;
        }

        MemucPath = selectedPath;
        Instances.Clear();
        await settingsStore.SaveAsync(new ApplicationSettings { MemucPath = selectedPath }, CancellationToken.None);
        StatusMessage = "Đã lưu đường dẫn. Chọn Làm mới để đọc danh sách máy ảo.";
    }

    private async Task RefreshAsync()
    {
        IsBusy = true;
        StatusMessage = "Đang đọc danh sách máy ảo…";
        try
        {
            var instances = await instanceService.GetInstancesAsync(MemucPath, CancellationToken.None);
            Instances.Clear();
            foreach (var instance in instances) Instances.Add(instance);
            StatusMessage = instances.Count == 0
                ? "Không tìm thấy máy ảo nào trong kết quả trả về."
                : $"Đã tải {instances.Count} máy ảo.";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Không thể đọc danh sách máy ảo: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
