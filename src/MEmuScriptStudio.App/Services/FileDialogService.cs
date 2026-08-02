using System.IO;
using Microsoft.Win32;

namespace MEmuScriptStudio.App.Services;

public interface IFileDialogService
{
    string? SelectMemucPath(string? currentPath);
}

public sealed class FileDialogService : IFileDialogService
{
    public string? SelectMemucPath(string? currentPath)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Chọn memuc.exe",
            Filter = "MEmu Console (memuc.exe)|memuc.exe",
            CheckFileExists = true,
            FileName = "memuc.exe"
        };

        if (!string.IsNullOrWhiteSpace(currentPath) && Directory.Exists(Path.GetDirectoryName(currentPath)))
        {
            dialog.InitialDirectory = Path.GetDirectoryName(currentPath);
        }

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
