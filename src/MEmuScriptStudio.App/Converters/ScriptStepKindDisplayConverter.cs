using System.Globalization;
using System.Windows.Data;
using MEmuScriptStudio.Core.Models;

namespace MEmuScriptStudio.App.Converters;

public sealed class ScriptStepKindDisplayConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        ScriptStepKind.AndroidShell => "Lệnh Android shell",
        ScriptStepKind.ForceStop => "Buộc dừng ứng dụng",
        ScriptStepKind.OpenApp => "Mở ứng dụng",
        ScriptStepKind.Delay => "Chờ",
        ScriptStepKind.Tap => "Chạm",
        ScriptStepKind.Hold => "Nhấn giữ",
        ScriptStepKind.Swipe => "Vuốt",
        ScriptStepKind.InputText => "Nhập văn bản",
        ScriptStepKind.AndroidClipboardPaste => "Dán clipboard Android",
        ScriptStepKind.KeyEvent => "Phím Android",
        ScriptStepKind.Note => "Ghi chú — không thực thi",
        ScriptStepKind.CloseChromeTabs => "Đóng tất cả tab Chrome",
        _ => value?.ToString() ?? string.Empty
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
