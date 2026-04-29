using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using ShareQ.App.ViewModels;
using ShareQ.Editor.Model;
using ShareQ.Editor.Views;
using Wpf.Ui.Controls;
using Button = Wpf.Ui.Controls.Button;
using MenuItem = System.Windows.Controls.MenuItem;

namespace ShareQ.App;

public partial class MainWindow : FluentWindow
{
    private const string StepDragFormat = "ShareQ.WorkflowStep";
    private Point _dragStartPoint;
    private WorkflowStepViewModel? _dragSourceStep;
    private readonly ShareQ.App.Services.ScreenColorPickerService _screenSampler;
    private readonly ShareQ.Editor.Persistence.ColorRecentsStore _colorRecents;

    public MainWindow(SettingsViewModel viewModel,
        ShareQ.App.Services.ScreenColorPickerService screenSampler,
        ShareQ.Editor.Persistence.ColorRecentsStore colorRecents)
    {
        InitializeComponent();
        DataContext = viewModel;
        _screenSampler = screenSampler;
        _colorRecents = colorRecents;
        // Newly-added workflow: focus the inline name field and select its text so the user can
        // type the real name straight away. Defer to a low-priority dispatcher tick because the
        // edit-view's TextBox isn't realised until the visibility binding flips.
        viewModel.Hotkeys.EditNameFocusRequested += (_, _) =>
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                WorkflowNameInline.Focus();
                WorkflowNameInline.SelectAll();
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        };

        // Auto-scroll the Debug log to the latest entry. We listen at the collection level so
        // every Append (ILogger callback → DebugLogService.Append → ObservableCollection.Add)
        // ends up at the bottom of the visible list. Gated on Debug.AutoScroll so the user can
        // turn it off and read older entries without the view yanking back.
        ((INotifyCollectionChanged)viewModel.Debug.Entries).CollectionChanged += (_, e) =>
        {
            if (!viewModel.Debug.AutoScroll) return;
            if (e.Action != NotifyCollectionChangedAction.Add) return;
            // Skip when the Debug tab isn't currently realised — the ListBox lives inside a
            // collapsed Grid until the user selects the tab, and a virtualized ListBox throws
            // when ScrollIntoView is called before its ItemsPanel exists. Once the tab is
            // visited, IsLoaded + IsVisible become true and auto-scroll kicks in normally.
            if (DebugLogList is null || !DebugLogList.IsLoaded || !DebugLogList.IsVisible) return;
            var last = viewModel.Debug.Entries.Count > 0 ? viewModel.Debug.Entries[^1] : null;
            if (last is null) return;
            try { DebugLogList.ScrollIntoView(last); }
            catch { /* virtualization race during heavy log bursts — drop a single tick */ }
        };
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

    // ── Inline workflow rename ──────────────────────────────────────────────────────────────
    // The TextBox in edit-view is bound TwoWay to WorkflowsViewModel.EditingDisplayName with
    // UpdateSourceTrigger=LostFocus, so the property mirrors what the user typed when focus
    // leaves the box. We then call SaveDisplayNameAsync to persist (or Enter from the keyboard
    // shortcut handler — also commits the focused TextBox first by moving focus to the parent).

    private void OnWorkflowNameLostFocus(object sender, RoutedEventArgs e)
    {
        // Single point where the inline rename gets persisted. Forces the binding to push the
        // typed text into EditingDisplayName right now (UpdateSource is a no-op when the binding
        // already committed via UpdateSourceTrigger=LostFocus, but we run it explicitly so the
        // ordering doesn't matter), then kicks off the save.
        if (sender is System.Windows.Controls.TextBox tb)
        {
            var be = tb.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty);
            be?.UpdateSource();
        }
        if (DataContext is not SettingsViewModel vm) return;
        _ = vm.Hotkeys.Workflows.SaveDisplayNameAsync();
    }

    private void OnWorkflowNameKeyDown(object sender, KeyEventArgs e)
    {
        // Enter or first Esc on the inline rename: shift focus to the next focusable element.
        // That triggers the TextBox's natural LostFocus → OnWorkflowNameLostFocus runs and
        // commits the rename. After the focus move there's a real focused element receiving
        // keystrokes, so a subsequent Esc bubbles to the Window's KeyBinding and navigates back.
        // Marking e.Handled is what stops THIS Esc from also triggering the Window KeyBinding.
        if (e.Key is not (Key.Enter or Key.Escape)) return;
        if (sender is not System.Windows.Controls.TextBox tb) return;
        tb.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
        e.Handled = true;
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

    /// <summary>Theme tab: open the existing editor color picker on the accent-background swatch
    /// and write the chosen RGB back into the Theme view-model's hex string. Handlers live in
    /// code-behind (rather than the VM) so we don't drag <see cref="ColorPickerWindow"/> — a UI
    /// type — into the view-model layer. The hex assignment fires the existing TryApply path
    /// which propagates colors live across the app.</summary>
    private void OnAccentBgSwatchClick(object sender, MouseButtonEventArgs e)
        => PickAccentColor(isBackground: true);

    private void OnAccentFgSwatchClick(object sender, MouseButtonEventArgs e)
        => PickAccentColor(isBackground: false);

    private void PickAccentColor(bool isBackground)
    {
        if (DataContext is not SettingsViewModel vm) return;
        var currentHex = isBackground ? vm.Theme.AccentBackgroundHex : vm.Theme.AccentForegroundHex;
        var current = TryParseShapeColor(currentHex) ?? (isBackground ? ShapeColor.Black : new ShapeColor(255, 255, 255, 255));
        var dialog = new ColorPickerWindow(current) { Owner = this };

        // Wire 🔍 button → screen sampler. SampleAtCursor returns hex (no clipboard side-effect)
        // which we parse and push into the dialog via ApplySampledColor.
        dialog.EyedropperRequested += (_, _) =>
        {
            var hex = _screenSampler.SampleAtCursor();
            if (hex is null) return;
            if (TryParseShapeColor(hex) is { } sampled) dialog.ApplySampledColor(sampled);
        };

        // OK button live preview: as the user picks inside the dialog, override the OK button's
        // matching channel (Background when picking AccentBg, Foreground when picking AccentFg)
        // so the user sees the *future* accent landed in a real button shape — without the rest
        // of the app re-painting until they actually commit with OK.
        EventHandler<ShapeColor> previewHandler = (_, c) =>
        {
            var brush = new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B));
            brush.Freeze();
            if (isBackground) dialog.OkButton.Background = brush;
            else               dialog.OkButton.Foreground = brush;
        };
        dialog.ColorChanged += previewHandler;

        // Pre-populate the Recent palette inside the picker so the user sees their previous
        // accent picks. Without this each Theme picker session starts with an empty Recent grid.
        ColorSwatchButton.CurrentRecents = _colorRecents.LoadAsync(System.Threading.CancellationToken.None).GetAwaiter().GetResult();

        if (dialog.ShowDialog() != true) return;
        var picked = dialog.PickedColor;
        var hex2 = $"#{picked.R:X2}{picked.G:X2}{picked.B:X2}";
        if (isBackground) vm.Theme.AccentBackgroundHex = hex2;
        else               vm.Theme.AccentForegroundHex = hex2;
        // Push to recents so the colour shows up next time the user opens any picker.
        _ = _colorRecents.PushAsync(picked, System.Threading.CancellationToken.None);
    }

    private static ShapeColor? TryParseShapeColor(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        var s = hex.Trim().TrimStart('#');
        if (s.Length is not (6 or 8)) return null;
        try
        {
            byte a = 255, r, g, b;
            if (s.Length == 8)
            {
                a = Convert.ToByte(s[..2], 16); r = Convert.ToByte(s[2..4], 16);
                g = Convert.ToByte(s[4..6], 16); b = Convert.ToByte(s[6..8], 16);
            }
            else
            {
                r = Convert.ToByte(s[..2], 16); g = Convert.ToByte(s[2..4], 16); b = Convert.ToByte(s[4..6], 16);
            }
            return new ShapeColor(a, r, g, b);
        }
        catch { return null; }
    }
}
