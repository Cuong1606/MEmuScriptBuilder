using System.Windows;
using MEmuScriptStudio.App.Services;

namespace MEmuScriptStudio.App;

public partial class AndroidDeviceAliasWindow : Window
{
    public AndroidDeviceAliasWindow(string serial, string? currentAlias)
    {
        InitializeComponent();
        SerialTextBlock.Text = $"Serial: {serial}";
        AliasTextBox.Text = currentAlias?.Trim() ?? string.Empty;
        Loaded += (_, _) =>
        {
            AliasTextBox.Focus();
            AliasTextBox.SelectAll();
        };
    }

    public AndroidDeviceAliasEditResult? Result { get; private set; }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var alias = AliasTextBox.Text.Trim();
        if (alias.Length == 0)
        {
            MessageBox.Show(this, "Tên thiết bị không được để trống. Dùng ‘Xóa alias’ để trở về tên mặc định.",
                "Tên thiết bị", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Result = new AndroidDeviceAliasEditResult(alias);
        DialogResult = true;
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        Result = new AndroidDeviceAliasEditResult(null, RemoveAlias: true);
        DialogResult = true;
    }
}
