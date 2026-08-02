using System.Globalization;
using System.Windows.Data;
using MEmuScriptStudio.Core.Models;

namespace MEmuScriptStudio.App.Converters;

public sealed class AndroidKeyEventDisplayConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        AndroidKeyEvent.Home => "Trang chủ",
        AndroidKeyEvent.Back => "Quay lại",
        AndroidKeyEvent.RecentApps => "Ứng dụng gần đây",
        AndroidKeyEvent.Menu => "Menu (phím cũ)",
        AndroidKeyEvent.VolumeUp => "Tăng âm lượng",
        AndroidKeyEvent.VolumeDown => "Giảm âm lượng",
        _ => value?.ToString() ?? string.Empty
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
