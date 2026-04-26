using ShareQ.App.ViewModels;
using Wpf.Ui.Controls;

namespace ShareQ.App;

public partial class MainWindow : FluentWindow
{
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
