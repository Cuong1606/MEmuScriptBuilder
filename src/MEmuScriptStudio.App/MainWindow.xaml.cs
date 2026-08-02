using System.Windows;
using MEmuScriptStudio.App.ViewModels;

namespace MEmuScriptStudio.App;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
