using ShareQ.App.ViewModels;
using Wpf.Ui.Controls;

namespace ShareQ.App;

public partial class MainWindow : FluentWindow
{
    public MainWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
