using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using ShareQ.App.ViewModels;
using Wpf.Ui.Controls;
using Button = Wpf.Ui.Controls.Button;
using MenuItem = System.Windows.Controls.MenuItem;

namespace ShareQ.App;

public partial class MainWindow : FluentWindow
{
    private const string StepDragFormat = "ShareQ.WorkflowStep";
    private Point _dragStartPoint;
    private WorkflowStepViewModel? _dragSourceStep;

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

    // ── Workflow step drag-and-drop reordering ──────────────────────────────────────────────
    // Dragging is initiated only from the ⋮⋮ handle (so the row's other interactive elements —
    // toggle, parameter buttons, remove — keep working without drag interference). MouseDown
    // captures the source row + start point; MouseMove starts DoDragDrop once the system drag
    // threshold is exceeded. The drop target is any Border in the same ItemsControl that has
    // AllowDrop=True (i.e. another step row).

    private void OnStepHandleMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe) return;
        if (fe.DataContext is not WorkflowStepViewModel step) return;
        _dragStartPoint = e.GetPosition(null);
        _dragSourceStep = step;
    }

    private void OnStepHandleMouseUp(object sender, MouseButtonEventArgs e)
    {
        // Cancel a pending-but-not-started drag if the user just clicked without moving.
        _dragSourceStep = null;
    }

    private void OnStepHandleMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragSourceStep is null) return;
        if (e.LeftButton != MouseButtonState.Pressed) return;
        var current = e.GetPosition(null);
        var dx = Math.Abs(current.X - _dragStartPoint.X);
        var dy = Math.Abs(current.Y - _dragStartPoint.Y);
        if (dx < SystemParameters.MinimumHorizontalDragDistance &&
            dy < SystemParameters.MinimumVerticalDragDistance) return;
        if (DataContext is not SettingsViewModel vm) return;

        var source = _dragSourceStep;
        _dragSourceStep = null; // consumed

        // Visual feedback: dim the source while the drag is in flight. Stays set until the editor
        // clears it after the drop / cancel below.
        source.IsDragSource = true;

        var data = new DataObject(StepDragFormat, source);
        DragDrop.DoDragDrop((DependencyObject)sender, data, DragDropEffects.Move);

        // DoDragDrop blocks until the user drops or cancels. Either way we wipe the visuals so no
        // stale highlight or insertion line lingers.
        vm.Workflows.Editor.ClearDragVisuals();
    }

    private void OnStepRowDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(StepDragFormat))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }
        if (DataContext is not SettingsViewModel vm)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }
        if (sender is not FrameworkElement fe || fe.DataContext is not WorkflowStepViewModel hovered)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        // Cursor in the upper half of this row → drop above this row (= activate this row's
        // top-gutter indicator). Cursor in the lower half → drop below this row, which we render
        // as "above the next row" so every gap is covered by exactly one indicator. If this is
        // the last row and cursor is in the lower half, the footer gutter is the target instead.
        var pos = e.GetPosition(fe);
        var insertAbove = pos.Y < fe.ActualHeight / 2.0;
        var items = vm.Workflows.Editor.Items;
        var idx = items.IndexOf(hovered);

        WorkflowStepViewModel? indicatorRow = null;
        var atEnd = false;
        if (insertAbove)
        {
            indicatorRow = hovered;
        }
        else if (idx >= 0 && idx + 1 < items.Count)
        {
            indicatorRow = items[idx + 1];
        }
        else
        {
            atEnd = true;
        }

        foreach (var item in items)
            item.IsDropTargetAbove = ReferenceEquals(item, indicatorRow);
        vm.Workflows.Editor.IsDropTargetAtEnd = atEnd;

        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void OnStepRowDragLeave(object sender, DragEventArgs e)
    {
        // Don't clear here: with a single-indicator model, cursor moving from row N's lower half
        // into row N+1's top gutter should keep showing the line in that gap. DragOver on the
        // new element fires the same tick and sets the right indicator. Clearing on leave makes
        // the line flicker. Stale indicators are cleaned up when the drag completes.
    }

    private void OnStepRowDrop(object sender, DragEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm) return;
        if (e.Data.GetData(StepDragFormat) is not WorkflowStepViewModel source) return;
        e.Handled = true;
        DispatchDrop(vm, source);
    }

    private void OnStepFooterDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(StepDragFormat) || DataContext is not SettingsViewModel vm)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }
        // Footer hover always means "drop at end". Clear per-row indicators so only the footer
        // line shows.
        foreach (var item in vm.Workflows.Editor.Items) item.IsDropTargetAbove = false;
        vm.Workflows.Editor.IsDropTargetAtEnd = true;
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void OnStepFooterDragLeave(object sender, DragEventArgs e)
    {
        // Same reasoning as the row leave: don't clear, let DragOver on the next element take
        // over. Drag completion handles final cleanup.
    }

    private void OnStepFooterDrop(object sender, DragEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm) return;
        if (e.Data.GetData(StepDragFormat) is not WorkflowStepViewModel source) return;
        e.Handled = true;
        DispatchDrop(vm, source);
    }

    /// <summary>Common drop dispatcher: reads the indicator state set by the most recent DragOver
    /// to know where the drop should land (above some row, or at the end), then calls the
    /// matching editor command.</summary>
    private static void DispatchDrop(SettingsViewModel vm, WorkflowStepViewModel source)
    {
        var editor = vm.Workflows.Editor;
        if (editor.IsDropTargetAtEnd)
        {
            _ = editor.MoveToEndAsync(source);
            return;
        }
        var aboveTarget = editor.Items.FirstOrDefault(i => i.IsDropTargetAbove);
        if (aboveTarget is not null)
            _ = editor.MoveToAsync(source, aboveTarget, insertAfter: false);
    }
}
