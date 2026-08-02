using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using MEmuScriptStudio.App.Services;
using MEmuScriptStudio.App.ViewModels;
using MEmuScriptStudio.Core.MEmu;
using MEmuScriptStudio.Core.Processes;
using MEmuScriptStudio.Infrastructure.MEmu;
using MEmuScriptStudio.Infrastructure.Persistence;
using MEmuScriptStudio.Infrastructure.Processes;

namespace MEmuScriptStudio.App;

public partial class App : Application
{
    private ServiceProvider? serviceProvider;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<MemuCommandBuilder>();
        services.AddSingleton<MemuListVmsParser>();
        services.AddSingleton<IMemuInstanceService, MemuInstanceService>();
        services.AddSingleton<IMemucPathDiscovery, MemucPathDiscovery>();
        services.AddSingleton<ISettingsStore, JsonSettingsStore>();
        services.AddSingleton<IFileDialogService, FileDialogService>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
        serviceProvider = services.BuildServiceProvider();

        var viewModel = serviceProvider.GetRequiredService<MainViewModel>();
        await viewModel.InitializeAsync(CancellationToken.None);
        serviceProvider.GetRequiredService<MainWindow>().Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        serviceProvider?.Dispose();
        base.OnExit(e);
    }
}
