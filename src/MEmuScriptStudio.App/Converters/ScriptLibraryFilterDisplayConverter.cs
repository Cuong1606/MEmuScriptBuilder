using System.Globalization;
using System.Windows.Data;
using MEmuScriptStudio.App.ViewModels;

namespace MEmuScriptStudio.App.Converters;

public sealed class ScriptLibraryFilterDisplayConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        ScriptLibraryFilter.All => "Tất cả",
        ScriptLibraryFilter.Regular => "Thường",
        ScriptLibraryFilter.Composite => "Gộp",
        _ => value?.ToString() ?? string.Empty
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
