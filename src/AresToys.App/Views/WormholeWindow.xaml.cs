using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using AresToys.App.Services.Launcher;
using AresToys.App.Services.Wormholes;

namespace AresToys.App.Views;

public partial class WormholeWindow : Window
{
    // Segoe Fluent Icons code points pinned as ints so the source file stays pure ASCII —
    // some tooling round-trips strip or re-encode private-use glyphs inline; constructing them
    // from the code point at static-init avoids that whole class of bug.
    private static readonly string ChevronUpGlyph   = char.ConvertFromUtf32(0xE70E);
    private static readonly string ChevronDownGlyph = char.ConvertFromUtf32(0xE70D);
    private static readonly string LockClosedGlyph  = char.ConvertFromUtf32(0xE72E);
    private static readonly string LockOpenGlyph    = char.ConvertFromUtf32(0xE785);

    private readonly WormholeRecord _record;
    private readonly Action _onPersist;
    private readonly IconService _icons;
    private readonly string _wormholesRoot;
    private readonly string _shortcutsDirectory;
    private readonly ObservableCollection<WormholeItemViewModel> _items = new();
    private bool _isClosingFromManager;

    public WormholeWindow(
        WormholeRecord record,
        Action onPersist,
        IconService icons,
        string wormholesRoot,
        string shortcutsDirectory)
    {
        _record = record;
        _onPersist = onPersist;
        _icons = icons;
        _wormholesRoot = wormholesRoot;
        _shortcutsDirectory = shortcutsDirectory;
        InitializeComponent();
        DataContext = record;
        ItemsHost.ItemsSource = _items;

        Left = record.Geometry.X;
        Top = record.Geometry.Y;
        Width = record.Geometry.Width;
        Height = record.Geometry.Height;

        ApplyLockState();
        ApplyRollState();
        RebuildItems();
        UpdateEmptyStateVisibility();

        LocationChanged += (_, _) =>
        {
            if (_isClosingFromManager) return;
            _record.Geometry.X = Left;
            _record.Geometry.Y = Top;
            _onPersist();
        };
        SizeChanged += (_, e) =>
        {
            if (_isClosingFromManager) return;
            if (!e.HeightChanged && !e.WidthChanged) return;
            _record.Geometry.Width = Width;
            if (!_record.IsRolled)
            {
                _record.Geometry.Height = Height;
                _record.Geometry.UnrolledHeight = Height;
            }
            _onPersist();
        };
        Closing += (_, _) =>
        {
            if (_isClosingFromManager) return;
            _onPersist();
        };
    }

    /// <summary>Marker so the manager's <c>CloseAll</c> / <c>DeleteAsync</c> path can close the
    /// window without our Closing handler trying to re-persist a record that's about to be
    /// (or has just been) deleted.</summary>
    internal void CloseFromManager()
    {
        _isClosingFromManager = true;
        Close();
    }

    /// <summary>Raised when the user picks "Delete wormhole…" from the hamburger menu. The
    /// manager subscribes in <c>SpawnWindow</c> and routes through its own <c>DeleteAsync</c>.</summary>
    public event EventHandler<Guid>? DeleteRequested;

    // -----------------------------------------------------------------------------------------
    // Header gestures
    // -----------------------------------------------------------------------------------------

    private void OnHeaderMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_record.IsLocked) return;
        if (e.ClickCount == 2)
        {
            ToggleRoll();
            return;
        }
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            try { DragMove(); }
            catch (InvalidOperationException) { /* geometry persisted via LocationChanged */ }
        }
    }

    private void OnHeaderMouseUp(object sender, MouseButtonEventArgs e) { /* reserved */ }

    private void OnContentAreaMouseDown(object sender, MouseButtonEventArgs e)
    {
        // Drag the wormhole from the content area too — Portals offers this as a toggle; we
        // make it the default. Ignored on click of a chrome button (they handle their own
        // routed Click) or on an item tile (item handler swallows the down).
        if (_record.IsLocked) return;
        if (e.ChangedButton != MouseButton.Left) return;
        if (e.OriginalSource is System.Windows.Controls.Button) return;
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            try { DragMove(); }
            catch (InvalidOperationException) { }
        }
    }

    private void OnChevronClicked(object sender, RoutedEventArgs e) => ToggleRoll();

    private void OnLockClicked(object sender, RoutedEventArgs e)
    {
        _record.IsLocked = !_record.IsLocked;
        ApplyLockState();
        _onPersist();
    }

    // -----------------------------------------------------------------------------------------
    // Hamburger menu
    // -----------------------------------------------------------------------------------------

    private void OnHamburgerClicked(object sender, RoutedEventArgs e)
    {
        var menu = new System.Windows.Controls.ContextMenu();

        var openFolder = new System.Windows.Controls.MenuItem { Header = "Open folder" };
        openFolder.Click += (_, _) => OpenShortcutsFolder();
        menu.Items.Add(openFolder);

        menu.Items.Add(new System.Windows.Controls.Separator());

        var rename = new System.Windows.Controls.MenuItem { Header = "Rename" };
        rename.Click += (_, _) => BeginInlineRename();
        menu.Items.Add(rename);

        var hide = new System.Windows.Controls.MenuItem { Header = "Hide this wormhole" };
        hide.Click += (_, _) =>
        {
            _record.IsHidden = true;
            _onPersist();
            CloseFromManager(); // window goes away but the record stays in JSON; reopen via Settings.
        };
        menu.Items.Add(hide);

        menu.Items.Add(new System.Windows.Controls.Separator());

        var delete = new System.Windows.Controls.MenuItem { Header = "Delete wormhole…" };
        delete.Click += (_, _) =>
        {
            var ok = MessageBox.Show(this,
                $"Delete wormhole \"{_record.Title}\"? This cannot be undone.",
                "AresToys",
                MessageBoxButton.OKCancel, MessageBoxImage.Question,
                MessageBoxResult.Cancel);
            if (ok != MessageBoxResult.OK) return;
            DeleteRequested?.Invoke(this, _record.Id);
        };
        menu.Items.Add(delete);

        menu.PlacementTarget = (FrameworkElement)sender;
        menu.IsOpen = true;
    }

    private void OpenShortcutsFolder()
    {
        try
        {
            Directory.CreateDirectory(_shortcutsDirectory);
            Process.Start(new ProcessStartInfo { FileName = _shortcutsDirectory, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Couldn't open the folder:\n" + ex.Message,
                "AresToys", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // -----------------------------------------------------------------------------------------
    // Inline rename
    // -----------------------------------------------------------------------------------------

    private void BeginInlineRename()
    {
        TitleEditor.Text = _record.Title;
        TitleEditor.Visibility = Visibility.Visible;
        TitleText.Visibility = Visibility.Collapsed;
        TitleEditor.Focus();
        TitleEditor.SelectAll();
    }

    private void CommitInlineRename()
    {
        var next = (TitleEditor.Text ?? string.Empty).Trim();
        if (!string.IsNullOrEmpty(next) && next != _record.Title)
        {
            _record.Title = next;
            // DataContext binding refreshes the TextBlock; Title isn't INPC so we re-poke the
            // DataContext to force the binding to re-read. Cheap because there's only one
            // binding observing it.
            DataContext = null;
            DataContext = _record;
            _onPersist();
        }
        EndInlineRename();
    }

    private void EndInlineRename()
    {
        TitleEditor.Visibility = Visibility.Collapsed;
        TitleText.Visibility = Visibility.Visible;
    }

    private void OnTitleEditorKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { CommitInlineRename(); e.Handled = true; }
        else if (e.Key == Key.Escape) { EndInlineRename(); e.Handled = true; }
    }

    private void OnTitleEditorLostFocus(object sender, RoutedEventArgs e)
    {
        if (TitleEditor.Visibility == Visibility.Visible) CommitInlineRename();
    }

    // -----------------------------------------------------------------------------------------
    // Drop handling — Explorer file drop creates .lnk shortcuts
    // -----------------------------------------------------------------------------------------

    private void OnDragEnter(object sender, DragEventArgs e)
    {
        e.Effects = ResolveDropEffect(e);
        e.Handled = true;
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = ResolveDropEffect(e);
        e.Handled = true;
    }

    private static DragDropEffects ResolveDropEffect(DragEventArgs e)
    {
        // Accept file drops only. Other formats (text, internal drags) fall through to None so
        // Windows shows the "not allowed" cursor.
        return e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (_record.IsLocked) return;
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var paths = (string[]?)e.Data.GetData(DataFormats.FileDrop);
        if (paths is null || paths.Length == 0) return;
        e.Handled = true;

        Directory.CreateDirectory(_shortcutsDirectory);
        var added = 0;
        var errors = new List<string>();
        foreach (var path in paths)
        {
            try
            {
                // If the dropped item already is a .lnk we copy it verbatim (preserves the
                // original target). Anything else (file, folder, .exe) gets a fresh .lnk that
                // points back to it — original file is never moved or copied.
                var extension = Path.GetExtension(path);
                var isShortcut = extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase);
                var isUrl = extension.Equals(".url", StringComparison.OrdinalIgnoreCase);

                string lnkPath;
                if (isShortcut)
                {
                    lnkPath = ShortcutFactory.SuggestUniqueShortcutPath(_shortcutsDirectory, path, ".lnk");
                    File.Copy(path, lnkPath, overwrite: false);
                }
                else if (isUrl)
                {
                    lnkPath = ShortcutFactory.SuggestUniqueShortcutPath(_shortcutsDirectory, path, ".url");
                    File.Copy(path, lnkPath, overwrite: false);
                }
                else
                {
                    lnkPath = ShortcutFactory.SuggestUniqueShortcutPath(_shortcutsDirectory, path, ".lnk");
                    ShortcutFactory.CreateLnk(lnkPath, path);
                }

                var item = new WormholeItem
                {
                    Id = Guid.NewGuid(),
                    ShortcutPath = Path.GetRelativePath(_wormholesRoot, lnkPath),
                    DisplayOrder = (_record.Data?.Items.Count ?? 0) + added,
                };
                _record.Data ??= new DataWormholeConfig();
                _record.Data.Items.Add(item);
                _items.Add(new WormholeItemViewModel(item, _wormholesRoot, _icons));
                added++;
            }
            catch (Exception ex)
            {
                errors.Add($"{Path.GetFileName(path)}: {ex.Message}");
            }
        }

        if (added > 0) _onPersist();
        UpdateEmptyStateVisibility();
        if (errors.Count > 0)
        {
            MessageBox.Show(this,
                "Some items couldn't be added:\n\n" + string.Join("\n", errors),
                "AresToys", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // -----------------------------------------------------------------------------------------
    // Items rendering
    // -----------------------------------------------------------------------------------------

    private void RebuildItems()
    {
        _items.Clear();
        if (_record.Data is null) return;
        foreach (var item in _record.Data.Items.OrderBy(i => i.DisplayOrder))
        {
            _items.Add(new WormholeItemViewModel(item, _wormholesRoot, _icons));
        }
    }

    private void UpdateEmptyStateVisibility()
    {
        EmptyStateHint.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnItemMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2) return;
        if (sender is not FrameworkElement fe) return;
        if (fe.DataContext is not WormholeItemViewModel vm) return;
        try
        {
            // ShellExecute on the .lnk → Windows resolves the target and opens it with the
            // correct handler. Same path Explorer takes when the user double-clicks a shortcut.
            Process.Start(new ProcessStartInfo
            {
                FileName = vm.AbsoluteShortcutPath,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Couldn't open the item:\n" + ex.Message,
                "AresToys", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // -----------------------------------------------------------------------------------------
    // Roll / lock state
    // -----------------------------------------------------------------------------------------

    private void ToggleRoll()
    {
        _record.IsRolled = !_record.IsRolled;
        ApplyRollState();
        _onPersist();
    }

    private void ApplyRollState()
    {
        if (_record.IsRolled)
        {
            ContentArea.Visibility = Visibility.Collapsed;
            Height = 34;
            ResizeMode = ResizeMode.NoResize;
            ChevronGlyph.Text = ChevronDownGlyph; // "click to unroll"
        }
        else
        {
            ContentArea.Visibility = Visibility.Visible;
            Height = _record.Geometry.UnrolledHeight;
            ResizeMode = _record.IsLocked ? ResizeMode.NoResize : ResizeMode.CanResizeWithGrip;
            ChevronGlyph.Text = ChevronUpGlyph; // "click to roll up"
        }
    }

    private void ApplyLockState()
    {
        LockGlyph.Text = _record.IsLocked ? LockClosedGlyph : LockOpenGlyph;
        if (!_record.IsRolled)
            ResizeMode = _record.IsLocked ? ResizeMode.NoResize : ResizeMode.CanResizeWithGrip;
    }
}
