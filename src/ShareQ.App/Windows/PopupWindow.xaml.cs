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
        // PreviewKeyDown (tunneling) so the Window sees Ctrl+digits before the SearchBox swallows them.
        PreviewKeyDown += OnKeyDown;
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
                ScrollSelectedIntoView();
                e.Handled = true;
                break;
            case Key.Up:
                ViewModel.MoveSelectionCommand.Execute(-1);
                ScrollSelectedIntoView();
                e.Handled = true;
                break;
            case Key.Enter:
                if (ViewModel.SelectedRow is { } row)
                {
                    // Don't Hide() here — the popup must remain the foreground window so AutoPaster
                    // can call SetForegroundWindow on the target without tripping Win32's
                    // anti-focus-stealing rules. The popup hides itself via Deactivated when the
                    // target window gets focus.
                    PasteRequested?.Invoke(this, row.Id);
                }
                e.Handled = true;
                break;
            default:
                // Ctrl+P toggles pin on the selected row. Plain "P" can't be used because the search
                // box always holds focus and P is a valid search character.
                if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && e.Key == Key.P)
                {
                    ViewModel.TogglePinSelectedCommand.Execute(null);
                    e.Handled = true;
                    break;
                }
                // Ctrl+1..9: quick-paste row N (1-indexed). Same Hide-after-restore reasoning as Enter.
                if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control
                    && e.Key >= Key.D1 && e.Key <= Key.D9)
                {
                    var idx = e.Key - Key.D1;
                    if (idx >= 0 && idx < ViewModel.Rows.Count)
                    {
                        var quick = ViewModel.Rows[idx];
                        PasteRequested?.Invoke(this, quick.Id);
                    }
                    e.Handled = true;
                }
                break;
        }
    }

    private void ScrollSelectedIntoView()
    {
        if (ViewModel.SelectedRow is null) return;
        ResultsList.ScrollIntoView(ViewModel.SelectedRow);
    }
}
