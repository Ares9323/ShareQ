using System.Windows;
using ShareQ.App.ViewModels;

namespace ShareQ.App.Windows;

public partial class PluginConfigDialog : Window
{
    public PluginConfigDialog(PluginConfigViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        CloseButton.Click += (_, _) => { DialogResult = true; Close(); };
        Loaded += async (_, _) => await vm.LoadAsync(System.Threading.CancellationToken.None);
    }
}
