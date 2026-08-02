using System.Globalization;
using System.Windows.Data;

namespace MEmuScriptStudio.App.Converters;

public sealed class RunningStateConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? "Đang chạy" : "Đã tắt";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
