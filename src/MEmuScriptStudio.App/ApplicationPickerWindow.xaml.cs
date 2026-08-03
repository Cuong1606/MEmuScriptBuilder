using System.Windows;
using System.Windows.Input;
using MEmuScriptStudio.App.Services;

namespace MEmuScriptStudio.App;

public partial class ApplicationPickerWindow : Window
{
    private readonly ApplicationPickerViewModel viewModel;
    private CancellationTokenSource? refreshCancellation;

    public ApplicationPickerWindow(ApplicationPickerViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        DataContext = viewModel;
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        refreshCancellation?.Cancel();
        refreshCancellation?.Dispose();
        refreshCancellation = new CancellationTokenSource();
        try { await viewModel.RefreshAsync(refreshCancellation.Token); }
        catch (OperationCanceledException) { }
        catch (Exception exception) { MessageBox.Show(this, exception.Message, "Không thể làm mới", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async void Foreground_Click(object sender, RoutedEventArgs e)
    {
        refreshCancellation?.Cancel();
        refreshCancellation?.Dispose();
        refreshCancellation = new CancellationTokenSource();
        try { await viewModel.UseForegroundApplicationAsync(refreshCancellation.Token); }
        catch (OperationCanceledException) { }
        catch (Exception exception) { MessageBox.Show(this, exception.Message, "Không thể nhận ứng dụng", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void Select_Click(object sender, RoutedEventArgs e)
    {
        if (viewModel.SelectedApplication is null) return;
        DialogResult = true;
    }

    private void Applications_DoubleClick(object sender, MouseButtonEventArgs e) => Select_Click(sender, e);

    private async void SaveName_Click(object sender, RoutedEventArgs e) =>
        await RunLibraryOperationAsync(viewModel.SaveNameAsync, "Không thể lưu tên ứng dụng");

    private async void DeleteName_Click(object sender, RoutedEventArgs e) =>
        await RunLibraryOperationAsync(viewModel.DeleteSavedNameAsync, "Không thể xóa tên ứng dụng");

    private async void ImportNames_Click(object sender, RoutedEventArgs e) =>
        await RunLibraryOperationAsync(viewModel.ImportNamesAsync, "Không thể nhập thư viện tên ứng dụng");

    private async void ExportNames_Click(object sender, RoutedEventArgs e) =>
        await RunLibraryOperationAsync(viewModel.ExportNamesAsync, "Không thể xuất thư viện tên ứng dụng");

    private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!ApplicationPickerShortcutPolicy.IsSaveShortcut(e.Key, Keyboard.Modifiers)) return;
        e.Handled = true;
        ManualDisplayNameTextBox.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateSource();
        await RunLibraryOperationAsync(viewModel.SaveNameAsync, "Không thể lưu tên ứng dụng");
    }

    private async Task RunLibraryOperationAsync(
        Func<CancellationToken, Task> operation,
        string errorTitle)
    {
        if (viewModel.IsBusy) return;
        refreshCancellation?.Cancel();
        refreshCancellation?.Dispose();
        refreshCancellation = new CancellationTokenSource();
        try { await operation(refreshCancellation.Token); }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, errorTitle, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        refreshCancellation?.Cancel();
        refreshCancellation?.Dispose();
        base.OnClosed(e);
    }
}
