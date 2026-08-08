using System.Windows;
using System.Windows.Threading;
using System.Net.Http;
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
    private MainWindow? mainWindow;
    private SingleInstanceCoordinator? singleInstanceCoordinator;
    private MainWindowActivationController? mainWindowActivationController;

    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            singleInstanceCoordinator = new SingleInstanceCoordinator(
                SingleInstanceNames.ForCurrentUserSession(),
                exception => ApplicationLifecycleLogger.WriteException("Single-instance IPC failure", exception));
            mainWindowActivationController = new MainWindowActivationController(
                new WpfActivationDispatcher(Dispatcher),
                () => mainWindow is null ? null : new WpfMainWindowActivationTarget(mainWindow),
                exception => ApplicationErrorReporter.Report(exception, "ActivateMainWindow"));
            var singleInstanceResult = singleInstanceCoordinator.Start(mainWindowActivationController.RequestActivation);
            if (!singleInstanceResult.ShouldContinueStartup)
            {
                singleInstanceCoordinator.Dispose();
                singleInstanceCoordinator = null;
                Shutdown(0);
                return;
            }

            ApplicationLifecycleLogger.Write("App startup");
            var services = new ServiceCollection();
            services.AddSingleton<IProcessLifecycleLogger, ApplicationProcessLifecycleLogger>();
            services.AddSingleton<IProcessRunner, ProcessRunner>();
            services.AddSingleton(new HttpClient { Timeout = Timeout.InfiniteTimeSpan });
            services.AddSingleton<MemuCommandBuilder>();
            services.AddSingleton<IAdbForwardTransport, MemucAdbForwardTransport>();
            services.AddSingleton<IChromeDevToolsClientFactory, ChromeDevToolsClientFactory>();
            services.AddSingleton<ILegacyChromeDevToolsClientFactory, LegacyChromeDevToolsClientFactory>();
            services.AddSingleton<IChromeTabService, ChromeCdpTabService>();
            services.AddSingleton<ISpecializedStepExecutor, ChromeSpecializedStepExecutor>();
            services.AddSingleton<ScriptStepCommandBuilder>();
            services.AddSingleton<MemuListVmsParser>();
            services.AddSingleton<AndroidLauncherActivityParser>();
            services.AddSingleton<AndroidApplicationLabelParser>();
            services.AddSingleton<AndroidForegroundApplicationParser>();
            services.AddSingleton<IMemuInstanceService, MemuInstanceService>();
            services.AddSingleton<IMemuHealthDiagnosticLogger, ApplicationMemuHealthDiagnosticLogger>();
            services.AddSingleton<IMemuCoreIdentityResolver, WindowsMemuCoreIdentityResolver>();
            services.AddSingleton<IPinnedMemuCoreHealthCheck, WindowsPinnedMemuCoreHealthCheck>();
            services.AddSingleton<MemuApplicationService>();
            services.AddSingleton<IMemuApplicationService>(provider => provider.GetRequiredService<MemuApplicationService>());
            services.AddSingleton<IMemuForegroundApplicationService>(provider => provider.GetRequiredService<MemuApplicationService>());
            services.AddSingleton<IMemuInputCaptureService, WindowsMemuInputCaptureService>();
            services.AddSingleton<IMemucPathDiscovery, MemucPathDiscovery>();
            services.AddSingleton<ISettingsStore, JsonSettingsStore>();
            services.AddSingleton<IScriptStore, JsonScriptStore>();
            services.AddSingleton<IScriptTransferService, JsonScriptTransferService>();
            services.AddSingleton<IApplicationNameTransferService, JsonApplicationNameTransferService>();
            services.AddSingleton<IDelayProvider, TaskDelayProvider>();
            services.AddSingleton<ScriptExecutionEngine>();
            services.AddSingleton<IScriptExecutionEngine, CompositeScriptExecutionEngine>();
            services.AddSingleton<ILaunchDelayProvider, LaunchDelayProvider>();
            services.AddSingleton<ILaunchSpacingRandom, LaunchSpacingRandom>();
            services.AddSingleton<IMultiInstanceExecutionScheduler, MultiInstanceExecutionScheduler>();
            services.AddSingleton<IStartupIssueLogger, StartupIssueLogger>();
            services.AddSingleton<IFileDialogService, FileDialogService>();
            services.AddSingleton<IConfirmationService, ConfirmationService>();
            services.AddSingleton<IScriptImportConflictService, ScriptImportConflictService>();
            services.AddSingleton<IApplicationNameImportConflictService, ApplicationNameImportConflictService>();
            services.AddSingleton<IApplicationPickerService, ApplicationPickerService>();
            services.AddSingleton<ITapCaptureOverlayService, TapCaptureOverlayService>();
            services.AddSingleton<ISwipeCaptureOverlayService, SwipeCaptureOverlayService>();
            services.AddSingleton<MainViewModel>();
            services.AddSingleton<MainWindow>();
            serviceProvider = services.BuildServiceProvider();

            var viewModel = serviceProvider.GetRequiredService<MainViewModel>();
            mainWindow = serviceProvider.GetRequiredService<MainWindow>();
            mainWindow.ContentRendered += OnMainWindowReadyForActivation;
            ApplicationLifecycleLogger.Write("MainWindow created");
            WindowFirstStartup.ConfigureMainWindow(new WpfStartupHost(this), mainWindow);
            await WindowFirstStartup.ShowAndInitializeAsync(
                mainWindow,
                () => viewModel.InitializeAsync(CancellationToken.None),
                exception =>
                {
                    var logPath = StartupErrorReporter.Report(exception, showDialog: false);
                    viewModel.ReportInitializationError(exception, logPath);
                });
        }
        catch (Exception exception)
        {
            ApplicationLifecycleLogger.WriteException("App startup failed", exception);
            try { StartupErrorReporter.Report(exception); }
            finally { Shutdown(-1); }
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        ApplicationLifecycleLogger.Write($"App Exit ExitCode={e.ApplicationExitCode}");
        DispatcherUnhandledException -= OnDispatcherUnhandledException;
        if (mainWindow is not null) mainWindow.ContentRendered -= OnMainWindowReadyForActivation;
        singleInstanceCoordinator?.Dispose();
        singleInstanceCoordinator = null;
        mainWindowActivationController = null;
        serviceProvider?.Dispose();
        mainWindow = null;
        base.OnExit(e);
    }

    private void OnMainWindowReadyForActivation(object? sender, EventArgs e) =>
        mainWindowActivationController?.MarkWindowReady();

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ApplicationLifecycleLogger.WriteException("Unhandled exception", e.Exception);
        if (Current?.MainWindow?.DataContext is MainViewModel viewModel)
            viewModel.ReportUnexpectedError(e.Exception);
        else
            ApplicationErrorReporter.Report(e.Exception, "DispatcherUnhandledException");
        e.Handled = false;
    }
}
