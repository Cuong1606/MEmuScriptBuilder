using System.Windows;

namespace MEmuScriptStudio.App.Services;

public sealed record AndroidDeviceAliasEditResult(string? Alias, bool RemoveAlias = false);

public interface IAndroidDeviceAliasDialogService
{
    AndroidDeviceAliasEditResult? Edit(string serial, string? currentAlias);
}

public sealed class AndroidDeviceAliasDialogService : IAndroidDeviceAliasDialogService
{
    public AndroidDeviceAliasEditResult? Edit(string serial, string? currentAlias)
    {
        var window = new AndroidDeviceAliasWindow(serial, currentAlias)
        {
            Owner = Application.Current?.MainWindow
        };
        return window.ShowDialog() == true ? window.Result : null;
    }
}
