using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using ShareQ.App.ViewModels;
using Wpf.Ui.Controls;
using Button = Wpf.Ui.Controls.Button;
using MenuItem = System.Windows.Controls.MenuItem;

namespace ShareQ.App;

public partial class MainWindow : FluentWindow
{
    public MainWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    /// <summary>Builds the "+ Add step" categorized context menu on demand. Doing this in
    /// code-behind keeps the XAML clean and avoids the WPF HierarchicalDataTemplate gotchas
    /// around binding Command on dynamically-templated leaf MenuItems.</summary>
    private void OnAddStepButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        if (DataContext is not SettingsViewModel vm) return;

        var menu = new ContextMenu
        {
            PlacementTarget = btn,
            Placement = PlacementMode.Bottom,
        };
        foreach (var group in vm.Workflows.Editor.AddableActions)
        {
            var groupItem = new MenuItem { Header = group.Category };
            foreach (var action in group.Actions)
            {
                var leaf = new MenuItem
                {
                    Header = action.DisplayName,
                    ToolTip = action.Description,
                };
                var capturedDescriptor = action;
                leaf.Click += (_, _) => vm.Workflows.Editor.AddStepCommand.Execute(capturedDescriptor);
                groupItem.Items.Add(leaf);
            }
            menu.Items.Add(groupItem);
        }
        btn.ContextMenu = menu;
        menu.IsOpen = true;
    }
}
