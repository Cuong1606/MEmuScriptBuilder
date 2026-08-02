using System.Windows;

namespace MEmuScriptStudio.App.Services;

public interface IConfirmationService
{
    bool Confirm(string message, string title);
}

public sealed class ConfirmationService : IConfirmationService
{
    public bool Confirm(string message, string title) =>
        MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
}
