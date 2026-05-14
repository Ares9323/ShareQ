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
    // Segoe Fluent Icons code points pinned via ConvertFromUtf32 so the source stays pure
    // ASCII (some tooling round-trips strip or re-encode private-use glyphs inline).
    private static readonly string ChevronUpGlyph   = char.ConvertFromUtf32(0xE70E);
    private static readonly string ChevronDownGlyph = char.ConvertFromUtf32(0xE70D);
    private static readonly string LockClosedGlyph  = char.ConvertFromUtf32(0xE72E);
    private static readonly string LockOpenGlyph    = char.ConvertFromUtf32(0xE785);

    /// <summary>Hard cap on Portal items rendered per wormhole. Mirrors the spec §6.4 default.
    /// Beyond this we emit a one-shot toast-style banner and truncate — large folders block
    /// the dispatcher today (no virtualisation in MVP).</summary>
    private const int PortalItemCap = 500;

    private readonly WormholeRecord _record;
    private readonly Action _onPersist;
    private readonly IconService _icons;
    private readonly string _wormholesRoot;
    private readonly string _shortcutsDirectory;
    private readonly ObservableCollection<WormholeItemViewModel> _items = new();
    private bool _isClosingFromManager;
    private bool _portalItemCapReached;

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

        if (_record.Kind == WormholeKind.Portal)
        {
            RefreshPortalItems();
            EmptyStateHint.Text = "Folder is empty — drop files here to add them.";
        }
        else
        {
            RebuildDataItems();
            EmptyStateHint.Text = "No items yet — drop files here.";
        }
        UpdateEmptyStateVisibility();

        // Wire the geometry-persist handlers on Loaded so WPF's initial reposition during the
        // very first Show() — which can clamp our requested Left/Top to the nearest visible
        // monitor's work area on PerMonitorV2 / multi-monitor setups — doesn't fire a persist
        // mid-boot. That synchronous SaveAsync from a startup-time LocationChanged caused
        // "Collection was modified" inside WormholeWindowManager.InitializeAsync's foreach
        // (the records list it was iterating was the same instance SaveAsync mutates).
        // After Loaded the position is settled, and any later LocationChanged is user-driven.
        Loaded += OnLoadedHookGeometryPersist;
        Closing += (_, _) =>
        {
            if (_isClosingFromManager) return;
            _onPersist();
        };
    }

    internal void CloseFromManager()
    {
        _isClosingFromManager = true;
        Close();
    }

    /// <summary>One-shot Loaded handler that subscribes the geometry-persist hooks AFTER the
    /// initial Show + reposition has settled. Self-unsubscribes so a re-Loaded event (rare for
    /// a top-level window but possible) doesn't double-subscribe and double-persist every drag.</summary>
    private void OnLoadedHookGeometryPersist(object? sender, RoutedEventArgs e)
    {
        Loaded -= OnLoadedHookGeometryPersist;
        LocationChanged += (_, _) =>
        {
            if (_isClosingFromManager) return;
            _record.Geometry.X = Left;
            _record.Geometry.Y = Top;
            _onPersist();
        };
        SizeChanged += (_, args) =>
        {
            if (_isClosingFromManager) return;
            if (!args.HeightChanged && !args.WidthChanged) return;
            _record.Geometry.Width = Width;
            if (!_record.IsRolled)
            {
                _record.Geometry.Height = Height;
                _record.Geometry.UnrolledHeight = Height;
            }
            _onPersist();
        };
    }

    public event EventHandler<Guid>? DeleteRequested;

    /// <summary>Re-enumerate the Portal source folder and rebuild <see cref="_items"/>. Called
    /// from the manager's <c>FolderWatcher.Changed</c> handler (debounced 300 ms) and from the
    /// hamburger "Refresh" entry. No-op for Data wormholes.</summary>
    public void RefreshPortalItems()
    {
        if (_record.Kind != WormholeKind.Portal) return;
        var portal = _record.Portal;
        if (portal is null) return;

        _items.Clear();
        _portalItemCapReached = false;
        try
        {
            if (!Directory.Exists(portal.SourcePath))
            {
                // Source went away (drive ejected, folder deleted): leave items empty and
                // show a banner via the empty-state slot. The watcher keeps the path so the
                // wormhole repopulates when the folder reappears.
                EmptyStateHint.Text = "Source folder is not available right now.";
                UpdateEmptyStateVisibility();
                return;
            }
            EmptyStateHint.Text = "Folder is empty — drop files here to add them.";

            // Folders first then files; both sorted by name (case-insensitive). Sort mode toggle
            // is part of §8.6 hamburger menu — Name is the default in the spec.
            IEnumerable<string> entries = portal.IncludeSubdirectoriesAsItems
                ? Directory.EnumerateDirectories(portal.SourcePath).Concat(Directory.EnumerateFiles(portal.SourcePath))
                : Directory.EnumerateFiles(portal.SourcePath);

            var ordered = entries.OrderBy(p => Path.GetFileName(p), StringComparer.CurrentCultureIgnoreCase).ToList();
            foreach (var path in ordered)
            {
                if (_items.Count >= PortalItemCap)
                {
                    _portalItemCapReached = true;
                    break;
                }
                _items.Add(new WormholeItemViewModel(path, _icons));
            }
        }
        catch (UnauthorizedAccessException)
        {
            EmptyStateHint.Text = "Access denied to source folder.";
        }
        catch (Exception)
        {
            // Defensive: rare IO failures shouldn't crash the wormhole. The next debounce tick
            // will retry.
            EmptyStateHint.Text = "Couldn't read source folder.";
        }

        UpdateEmptyStateVisibility();
        if (_portalItemCapReached)
        {
            // The cap is rare enough that a MessageBox would feel heavy on every refresh; we
            // surface it via the empty-state slot so it stays visible but unobtrusive even
            // while items render around it.
            EmptyStateHint.Text = $"Folder has more than {PortalItemCap} entries — only the first {PortalItemCap} are shown.";
            EmptyStateHint.Visibility = Visibility.Visible;
        }
    }

    // -----------------------------------------------------------------------------------------
    // Header gestures
    // -----------------------------------------------------------------------------------------

    private void OnHeaderMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_record.IsLocked) return;
        if (e.ClickCount == 2) { ToggleRoll(); return; }
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            try { DragMove(); }
            catch (InvalidOperationException) { }
        }
    }

    private void OnHeaderMouseUp(object sender, MouseButtonEventArgs e) { /* reserved */ }

    private void OnContentAreaMouseDown(object sender, MouseButtonEventArgs e)
    {
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
    // Hamburger menu (entries differ by Data vs Portal)
    // -----------------------------------------------------------------------------------------

    private void OnHamburgerClicked(object sender, RoutedEventArgs e)
    {
        var menu = new System.Windows.Controls.ContextMenu();
        var isPortal = _record.Kind == WormholeKind.Portal;

        var openFolder = new System.Windows.Controls.MenuItem
        {
            Header = isPortal ? "Open source folder" : "Open shortcuts folder",
        };
        openFolder.Click += (_, _) => OpenAssociatedFolder();
        menu.Items.Add(openFolder);

        if (isPortal)
        {
            var refresh = new System.Windows.Controls.MenuItem { Header = "Refresh" };
            refresh.Click += (_, _) => RefreshPortalItems();
            menu.Items.Add(refresh);

            var changeFolder = new System.Windows.Controls.MenuItem { Header = "Change folder…" };
            changeFolder.Click += (_, _) => OnChangeFolder();
            menu.Items.Add(changeFolder);
        }

        menu.Items.Add(new System.Windows.Controls.Separator());

        var rename = new System.Windows.Controls.MenuItem { Header = "Rename" };
        rename.Click += (_, _) => BeginInlineRename();
        menu.Items.Add(rename);

        var hide = new System.Windows.Controls.MenuItem { Header = "Hide this wormhole" };
        hide.Click += (_, _) =>
        {
            _record.IsHidden = true;
            _onPersist();
            CloseFromManager();
        };
        menu.Items.Add(hide);

        menu.Items.Add(new System.Windows.Controls.Separator());

        var delete = new System.Windows.Controls.MenuItem { Header = "Delete wormhole…" };
        delete.Click += (_, _) =>
        {
            var confirm = MessageBox.Show(this,
                isPortal
                    ? $"Delete wormhole \"{_record.Title}\"?\n\nThe source folder on disk is NOT touched."
                    : $"Delete wormhole \"{_record.Title}\"? This cannot be undone.",
                "AresToys",
                MessageBoxButton.OKCancel, MessageBoxImage.Question,
                MessageBoxResult.Cancel);
            if (confirm != MessageBoxResult.OK) return;
            DeleteRequested?.Invoke(this, _record.Id);
        };
        menu.Items.Add(delete);

        menu.PlacementTarget = (FrameworkElement)sender;
        menu.IsOpen = true;
    }

    private void OpenAssociatedFolder()
    {
        var folder = _record.Kind == WormholeKind.Portal
            ? _record.Portal?.SourcePath
            : _shortcutsDirectory;
        if (string.IsNullOrWhiteSpace(folder))
        {
            MessageBox.Show(this, "No folder is associated with this wormhole.", "AresToys",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try
        {
            if (_record.Kind == WormholeKind.Data) Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Couldn't open the folder:\n" + ex.Message,
                "AresToys", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnChangeFolder()
    {
        if (_record.Kind != WormholeKind.Portal || _record.Portal is null) return;
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Pick the new source folder for this wormhole",
            InitialDirectory = Directory.Exists(_record.Portal.SourcePath)
                ? _record.Portal.SourcePath
                : Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
        };
        if (dlg.ShowDialog(this) != true) return;
        _record.Portal.SourcePath = dlg.FolderName;
        _onPersist();
        // The manager listens for an explicit event to re-Start its watcher on the new path.
        // For the MVP we just refresh once and let the user toggle Refresh manually; the
        // watcher will reattach correctly on the next app launch. (Live watcher swap = polish.)
        RefreshPortalItems();
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
    // Drop handling — branches on Data vs Portal kind
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

    private DragDropEffects ResolveDropEffect(DragEventArgs e)
    {
        if (_record.IsLocked) return DragDropEffects.None;
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return DragDropEffects.None;
        // Data wormhole always reads as Copy — the original source file is never touched, only
        // a .lnk is materialised. Cursor reflects this so the user can't confuse it with a
        // real file move.
        if (_record.Kind != WormholeKind.Portal) return DragDropEffects.Copy;

        // Portal mirrors Explorer's standard drag semantics:
        //   - Ctrl  → force Copy
        //   - Shift → force Move
        //   - no modifier → Move if source and dest are on the same volume, Copy otherwise
        // The cursor previews the action of the first dragged path; per-file decisions in
        // DropOntoPortal apply the same rule per item, so a mixed-volume batch behaves
        // correctly even if the cursor only shows one of the two effects.
        var ctrl  = (e.KeyStates & DragDropKeyStates.ControlKey) == DragDropKeyStates.ControlKey;
        var shift = (e.KeyStates & DragDropKeyStates.ShiftKey)   == DragDropKeyStates.ShiftKey;
        if (ctrl)  return DragDropEffects.Copy;
        if (shift) return DragDropEffects.Move;

        var paths = (string[]?)e.Data.GetData(DataFormats.FileDrop);
        if (paths is null || paths.Length == 0) return DragDropEffects.Copy;
        return IsSameVolume(paths[0], _record.Portal?.SourcePath)
            ? DragDropEffects.Move
            : DragDropEffects.Copy;
    }

    /// <summary>True if <paramref name="source"/> and <paramref name="destFolder"/> share the
    /// same path root — drive letter for local paths, UNC server+share for network paths. Used
    /// to decide the default Move-vs-Copy behaviour on a Portal drop (Explorer semantics —
    /// same-volume drag defaults to Move, cross-volume defaults to Copy).</summary>
    private static bool IsSameVolume(string source, string? destFolder)
    {
        if (string.IsNullOrWhiteSpace(destFolder)) return false;
        try
        {
            var srcRoot = System.IO.Path.GetPathRoot(System.IO.Path.GetFullPath(source));
            var dstRoot = System.IO.Path.GetPathRoot(System.IO.Path.GetFullPath(destFolder));
            return !string.IsNullOrEmpty(srcRoot)
                && string.Equals(srcRoot, dstRoot, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            // Defensive — GetFullPath throws on bad paths (illegal chars, too-long). Fall back
            // to Copy on uncertainty so we never silently move a file we couldn't reason about.
            return false;
        }
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (_record.IsLocked) return;
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var paths = (string[]?)e.Data.GetData(DataFormats.FileDrop);
        if (paths is null || paths.Length == 0) return;
        e.Handled = true;

        if (_record.Kind == WormholeKind.Portal) DropOntoPortal(paths, e);
        else DropOntoData(paths);
    }

    private void DropOntoData(string[] paths)
    {
        Directory.CreateDirectory(_shortcutsDirectory);
        var added = 0;
        var errors = new List<string>();
        foreach (var path in paths)
        {
            try
            {
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
            MessageBox.Show(this, "Some items couldn't be added:\n\n" + string.Join("\n", errors),
                "AresToys", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void DropOntoPortal(string[] paths, DragEventArgs e)
    {
        if (_record.Portal is null || string.IsNullOrWhiteSpace(_record.Portal.SourcePath)) return;
        var dest = _record.Portal.SourcePath;
        if (!Directory.Exists(dest))
        {
            MessageBox.Show(this, "The Portal source folder isn't currently available.",
                "AresToys", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Per-file Move-vs-Copy decision matches the cursor preview rule in ResolveDropEffect:
        // Ctrl forces Copy, Shift forces Move, otherwise same-volume drags Move and cross-volume
        // drags Copy. Evaluated per item so a mixed-volume batch (e.g. user drags 2 files from
        // C:\ and 1 from D:\ at the same time) gets the right behaviour for each.
        var ctrl  = (e.KeyStates & DragDropKeyStates.ControlKey) == DragDropKeyStates.ControlKey;
        var shift = (e.KeyStates & DragDropKeyStates.ShiftKey)   == DragDropKeyStates.ShiftKey;
        bool ShouldMove(string source) =>
            !ctrl && (shift || IsSameVolume(source, dest));

        var errors = new List<string>();
        foreach (var source in paths)
        {
            try
            {
                // Skip a self-drop (dragging an item from the same source folder back in) —
                // the operation would either be a same-path Move (NOP) or a Copy that would
                // create "name (2)" pointlessly.
                var srcDir = Directory.Exists(source)
                    ? Path.GetDirectoryName(source.TrimEnd(Path.DirectorySeparatorChar))
                    : Path.GetDirectoryName(source);
                if (string.Equals(srcDir, dest, StringComparison.OrdinalIgnoreCase)) continue;

                // Block any drop that would put a folder inside itself or one of its descendants
                // (recursion-into-self). Catches: dropping the wormhole's own source folder onto
                // the wormhole (would create C:\Foo\Foo), or dropping any parent folder onto a
                // Portal whose source is a descendant (would infinite-loop Directory.Move /
                // CopyDirectoryRecursive). Only relevant when source is a directory — file drops
                // can't recurse.
                if (IsDestinationInsideSource(source, dest))
                {
                    errors.Add($"{Path.GetFileName(source.TrimEnd(Path.DirectorySeparatorChar))}: can't drop a folder into itself or one of its subfolders.");
                    continue;
                }

                var name = Path.GetFileName(source.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                var targetPath = Path.Combine(dest, name);
                targetPath = UniqueTargetPath(targetPath);

                var move = ShouldMove(source);
                if (Directory.Exists(source))
                {
                    if (move) Directory.Move(source, targetPath);
                    else CopyDirectoryRecursive(source, targetPath);
                }
                else
                {
                    if (move) File.Move(source, targetPath, overwrite: false);
                    else File.Copy(source, targetPath, overwrite: false);
                }
            }
            catch (Exception ex)
            {
                errors.Add($"{Path.GetFileName(source)}: {ex.Message}");
            }
        }
        // No manual refresh — the FolderWatcher will pick the changes up after its 300 ms tick
        // and the manager calls RefreshPortalItems(). Only show errors here.
        if (errors.Count > 0)
            MessageBox.Show(this, "Some items couldn't be moved/copied:\n\n" + string.Join("\n", errors),
                "AresToys", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    /// <summary>True if <paramref name="dest"/> is the same path as <paramref name="source"/>
    /// or a descendant directory of it. Used to block recursive moves / copies that would
    /// produce <c>C:\Foo\Foo</c> or trigger an infinite copy loop when the user drops a folder
    /// onto one of its own subtrees. Source-is-file → always false (files can't recurse).</summary>
    private static bool IsDestinationInsideSource(string source, string dest)
    {
        if (!Directory.Exists(source)) return false;
        try
        {
            var sourceFull = Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var destFull   = Path.GetFullPath(dest).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(destFull, sourceFull, StringComparison.OrdinalIgnoreCase)
                || destFull.StartsWith(sourceFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            // Defensive: GetFullPath can throw on illegal characters / too-long paths. Treat as
            // "not same" (= allow the drop) — File.Move / Directory.Move will surface their own
            // exception to the per-item error list if it really is a bad path.
            return false;
        }
    }

    private static string UniqueTargetPath(string candidate)
    {
        if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
        var dir = Path.GetDirectoryName(candidate)!;
        var stem = Path.GetFileNameWithoutExtension(candidate);
        var ext = Path.GetExtension(candidate);
        for (var n = 2; n < 1000; n++)
        {
            var next = Path.Combine(dir, $"{stem} ({n}){ext}");
            if (!File.Exists(next) && !Directory.Exists(next)) return next;
        }
        return Path.Combine(dir, $"{stem}-{Guid.NewGuid():N}{ext}");
    }

    private static void CopyDirectoryRecursive(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), overwrite: false);
        foreach (var sub in Directory.EnumerateDirectories(source))
            CopyDirectoryRecursive(sub, Path.Combine(dest, Path.GetFileName(sub)));
    }

    // -----------------------------------------------------------------------------------------
    // Items rendering
    // -----------------------------------------------------------------------------------------

    private void RebuildDataItems()
    {
        _items.Clear();
        if (_record.Data is null) return;
        foreach (var item in _record.Data.Items.OrderBy(i => i.DisplayOrder))
            _items.Add(new WormholeItemViewModel(item, _wormholesRoot, _icons));
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
            Process.Start(new ProcessStartInfo { FileName = vm.AbsolutePath, UseShellExecute = true });
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
            ChevronGlyph.Text = ChevronDownGlyph;
        }
        else
        {
            ContentArea.Visibility = Visibility.Visible;
            Height = _record.Geometry.UnrolledHeight;
            ResizeMode = _record.IsLocked ? ResizeMode.NoResize : ResizeMode.CanResizeWithGrip;
            ChevronGlyph.Text = ChevronUpGlyph;
        }
    }

    private void ApplyLockState()
    {
        LockGlyph.Text = _record.IsLocked ? LockClosedGlyph : LockOpenGlyph;
        if (!_record.IsRolled)
            ResizeMode = _record.IsLocked ? ResizeMode.NoResize : ResizeMode.CanResizeWithGrip;
    }
}
