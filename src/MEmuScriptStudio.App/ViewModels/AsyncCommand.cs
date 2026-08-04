using System.Windows.Input;
using MEmuScriptStudio.App.Services;

namespace MEmuScriptStudio.App.ViewModels;

public sealed class AsyncCommand(
    Func<Task> execute,
    Func<bool>? canExecute = null,
    Action<Exception>? onError = null) : ICommand
{
    private bool isExecuting;

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => !isExecuting && (canExecute?.Invoke() ?? true);

    public async void Execute(object? parameter) => await ExecuteAsync();

    public async Task ExecuteAsync()
    {
        if (!CanExecute(null)) return;
        isExecuting = true;
        RaiseCanExecuteChanged();
        try
        {
            await execute();
        }
        catch (Exception exception)
        {
            if (onError is not null) onError(exception);
            else ApplicationErrorReporter.Report(exception, "AsyncCommandFailure");
        }
        finally
        {
            isExecuting = false;
            RaiseCanExecuteChanged();
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
