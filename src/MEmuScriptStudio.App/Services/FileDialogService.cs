using System.IO;
using Microsoft.Win32;

namespace MEmuScriptStudio.App.Services;

public interface IFileDialogService
{
    string? SelectMemucPath(string? currentPath);
    string? SelectAdbPath(string? currentPath) => null;
    string? SelectScriptImportPath();
    string? SelectScriptExportPath(string suggestedFileName);
    string? SelectApplicationNameImportPath();
    string? SelectApplicationNameExportPath(string suggestedFileName);
    string? SelectAndroidApplicationLibraryImportPath() => null;
    string? SelectAndroidApplicationLibraryExportPath(string suggestedFileName) => null;
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

    public string? SelectAdbPath(string? currentPath)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Chọn adb.exe",
            Filter = "Android Debug Bridge (adb.exe)|adb.exe",
            CheckFileExists = true,
            FileName = "adb.exe"
        };
        if (!string.IsNullOrWhiteSpace(currentPath) && Directory.Exists(Path.GetDirectoryName(currentPath)))
            dialog.InitialDirectory = Path.GetDirectoryName(currentPath);
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? SelectScriptImportPath()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Nhập kịch bản",
            Filter = "MEmu Script (*.memuscript)|*.memuscript",
            CheckFileExists = true,
            Multiselect = false
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? SelectScriptExportPath(string suggestedFileName)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Xuất kịch bản",
            Filter = "MEmu Script (*.memuscript)|*.memuscript",
            DefaultExt = ".memuscript",
            AddExtension = true,
            FileName = suggestedFileName
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? SelectApplicationNameImportPath()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Nhập thư viện tên ứng dụng",
            Filter = "MEmu App Names (*.memuappnames)|*.memuappnames",
            CheckFileExists = true,
            Multiselect = false
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? SelectApplicationNameExportPath(string suggestedFileName)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Xuất thư viện tên ứng dụng",
            Filter = "MEmu App Names (*.memuappnames)|*.memuappnames",
            DefaultExt = ".memuappnames",
            AddExtension = true,
            FileName = suggestedFileName
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? SelectAndroidApplicationLibraryImportPath()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Nhập thư viện ứng dụng Android",
            Filter = "Android App Library (*.androidappnames)|*.androidappnames",
            CheckFileExists = true,
            Multiselect = false
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? SelectAndroidApplicationLibraryExportPath(string suggestedFileName)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Xuất thư viện ứng dụng Android",
            Filter = "Android App Library (*.androidappnames)|*.androidappnames",
            DefaultExt = ".androidappnames",
            AddExtension = true,
            FileName = suggestedFileName
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
