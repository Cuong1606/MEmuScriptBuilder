using System.Windows;
using MEmuScriptStudio.Core.Models;
using MEmuScriptStudio.Core.Scripts;

namespace MEmuScriptStudio.App.Services;

public interface IScriptImportConflictService
{
    ScriptImportConflictResolution Resolve(ScriptDefinition importedScript);
}

public sealed class ScriptImportConflictService : IScriptImportConflictService
{
    public ScriptImportConflictResolution Resolve(ScriptDefinition importedScript)
    {
        var result = MessageBox.Show(
            $"Kịch bản '{importedScript.Name}' đã tồn tại.\n\nCó = Ghi đè\nKhông = Tạo bản sao\nHủy = Bỏ qua",
            "Xử lý kịch bản trùng",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);
        return result switch
        {
            MessageBoxResult.Yes => ScriptImportConflictResolution.Overwrite,
            MessageBoxResult.No => ScriptImportConflictResolution.CreateCopy,
            _ => ScriptImportConflictResolution.Skip
        };
    }
}
