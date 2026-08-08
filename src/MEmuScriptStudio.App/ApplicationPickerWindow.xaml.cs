using System.Windows;
using System.Windows.Input;
using MEmuScriptStudio.App.Services;

namespace MEmuScriptStudio.App;

public partial class ApplicationPickerWindow : Window
{
    private readonly IApplicationPickerViewModel viewModel;
    private CancellationTokenSource? refreshCancellation;

    public ApplicationPickerWindow(IApplicationPickerViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        DataContext = viewModel;
        Title = viewModel.WindowTitle;
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
        try
        {
            await viewModel.UseForegroundApplicationAsync(refreshCancellation.Token);
            if (ApplicationsGrid.SelectedItem is not null)
                ApplicationsGrid.ScrollIntoView(ApplicationsGrid.SelectedItem);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { MessageBox.Show(this, exception.Message, "Không thể nhận ứng dụng", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async void Select_Click(object sender, RoutedEventArgs e)
    {
        if (!viewModel.HasSelection) return;
        if (!await PersistSelectionNameIfRequiredAsync()) return;
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
        e.Handled = await TrySaveNameShortcutAsync(e.Key, Keyboard.Modifiers);
    }

    internal async Task<bool> TrySaveNameShortcutAsync(Key key, ModifierKeys modifiers)
    {
        if (!viewModel.ShowSaveNameAction || !ApplicationPickerShortcutPolicy.IsSaveShortcut(key, modifiers))
            return false;
        ManualDisplayNameTextBox.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateSource();
        await RunLibraryOperationAsync(viewModel.SaveNameAsync, "Không thể lưu tên ứng dụng");
        return true;
    }

    internal async Task<bool> PersistSelectionNameIfRequiredAsync()
    {
        ManualDisplayNameTextBox.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateSource();
        return !viewModel.PersistNameOnSelect ||
            await RunLibraryOperationAsync(viewModel.SaveNameAsync, "Không thể lưu tên ứng dụng");
    }

    private async Task<bool> RunLibraryOperationAsync(
        Func<CancellationToken, Task> operation,
        string errorTitle)
    {
        if (viewModel.IsBusy) return false;
        refreshCancellation?.Cancel();
        refreshCancellation?.Dispose();
        refreshCancellation = new CancellationTokenSource();
        try
        {
            await operation(refreshCancellation.Token);
            return true;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, errorTitle, MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        refreshCancellation?.Cancel();
        refreshCancellation?.Dispose();
        base.OnClosed(e);
    }
}
