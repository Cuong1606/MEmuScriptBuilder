using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using MEmuScriptStudio.App.Services;
using MEmuScriptStudio.App.ViewModels;
using MEmuScriptStudio.Core.Execution;
using MEmuScriptStudio.Core.MEmu;
using MEmuScriptStudio.Core.Processes;
using MEmuScriptStudio.Core.Scripts;
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
        try
        {
            var services = new ServiceCollection();
            services.AddSingleton<IProcessRunner, ProcessRunner>();
            services.AddSingleton<MemuCommandBuilder>();
            services.AddSingleton<ScriptStepCommandBuilder>();
            services.AddSingleton<MemuListVmsParser>();
            services.AddSingleton<AndroidLauncherActivityParser>();
            services.AddSingleton<AndroidApplicationLabelParser>();
            services.AddSingleton<IMemuInstanceService, MemuInstanceService>();
            services.AddSingleton<IMemuApplicationService, MemuApplicationService>();
            services.AddSingleton<IMemuInputCaptureService, WindowsMemuInputCaptureService>();
            services.AddSingleton<IMemucPathDiscovery, MemucPathDiscovery>();
            services.AddSingleton<ISettingsStore, JsonSettingsStore>();
            services.AddSingleton<IScriptStore, JsonScriptStore>();
            services.AddSingleton<IDelayProvider, TaskDelayProvider>();
            services.AddSingleton<IScriptExecutionEngine, ScriptExecutionEngine>();
            services.AddSingleton<IFileDialogService, FileDialogService>();
            services.AddSingleton<IConfirmationService, ConfirmationService>();
            services.AddSingleton<IApplicationPickerService, ApplicationPickerService>();
            services.AddSingleton<ISwipeCaptureOverlayService, SwipeCaptureOverlayService>();
            services.AddSingleton<MainViewModel>();
            services.AddSingleton<MainWindow>();
            serviceProvider = services.BuildServiceProvider();

            var viewModel = serviceProvider.GetRequiredService<MainViewModel>();
            try
            {
                await viewModel.InitializeAsync(CancellationToken.None);
            }
            catch (Exception exception)
            {
                viewModel.ReportUnexpectedError(exception);
            }

            serviceProvider.GetRequiredService<MainWindow>().Show();
        }
        catch (Exception exception)
        {
            try { StartupErrorReporter.Report(exception); }
            finally { Shutdown(-1); }
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        serviceProvider?.Dispose();
        base.OnExit(e);
    }
}
