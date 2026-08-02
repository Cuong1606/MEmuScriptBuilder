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

    private void Select_Click(object sender, RoutedEventArgs e)
    {
        if (viewModel.SelectedApplication is null) return;
        DialogResult = true;
    }

    private void Applications_DoubleClick(object sender, MouseButtonEventArgs e) => Select_Click(sender, e);

    protected override void OnClosed(EventArgs e)
    {
        refreshCancellation?.Cancel();
        refreshCancellation?.Dispose();
        base.OnClosed(e);
    }
}
