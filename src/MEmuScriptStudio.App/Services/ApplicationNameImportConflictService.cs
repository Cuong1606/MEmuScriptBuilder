using System.Windows;
using MEmuScriptStudio.Core.MEmu;

namespace MEmuScriptStudio.App.Services;

public interface IApplicationNameImportConflictService
{
    ApplicationNameImportConflictResolution Resolve(
        string packageName,
        string currentDisplayName,
        string importedDisplayName);
}

public sealed class ApplicationNameImportConflictService : IApplicationNameImportConflictService
{
    public ApplicationNameImportConflictResolution Resolve(
        string packageName,
        string currentDisplayName,
        string importedDisplayName)
    {
        var result = MessageBox.Show(
            $"Package '{packageName}' đã có tên '{currentDisplayName}'.\n" +
            $"Tên nhập vào: '{importedDisplayName}'.\n\n" +
            "Có = Ghi đè\nKhông = Bỏ qua\nHủy = Hủy toàn bộ lần nhập",
            "Xử lý tên ứng dụng trùng",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);
        return result switch
        {
            MessageBoxResult.Yes => ApplicationNameImportConflictResolution.Overwrite,
            MessageBoxResult.No => ApplicationNameImportConflictResolution.Skip,
            _ => ApplicationNameImportConflictResolution.Cancel
        };
    }
}
