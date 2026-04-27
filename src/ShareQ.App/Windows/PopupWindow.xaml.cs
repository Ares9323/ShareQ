using System.Windows;
using System.Windows.Input;
using ShareQ.App.ViewModels;
using ShareQ.Core.Domain;

namespace ShareQ.App.Windows;

public partial class PopupWindow : Window
{
    public PopupWindow(PopupWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        ViewModel = viewModel;

        Loaded += (_, _) => SearchBox.Focus();
        Deactivated += (_, _) => Hide();
        KeyDown += OnKeyDown;
        ResultsList.MouseDoubleClick += OnResultsListDoubleClick;
    }

    public PopupWindowViewModel ViewModel { get; }

    public event EventHandler<long>? PasteRequested;
    public event EventHandler<long>? OpenInEditorRequested;

    private void OnResultsListDoubleClick(object? sender, MouseButtonEventArgs e)
    {
        if (ViewModel.SelectedRow is { } row && row.Kind == ItemKind.Image)
        {
            Hide();
            OpenInEditorRequested?.Invoke(this, row.Id);
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Hide();
                e.Handled = true;
                break;
            case Key.Down:
                ViewModel.MoveSelectionCommand.Execute(1);
                e.Handled = true;
                break;
            case Key.Up:
                ViewModel.MoveSelectionCommand.Execute(-1);
                e.Handled = true;
                break;
            case Key.Enter:
                if (ViewModel.SelectedRow is { } row)
                {
                    Hide();
                    PasteRequested?.Invoke(this, row.Id);
                }
                e.Handled = true;
                break;
            default:
                break;
        }
    }
}
