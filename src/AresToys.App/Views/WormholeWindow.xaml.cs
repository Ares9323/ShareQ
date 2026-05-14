using System.Windows;
using System.Windows.Input;
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
    private bool _isClosingFromManager;

    public WormholeWindow(WormholeRecord record, Action onPersist)
    {
        _record = record;
        _onPersist = onPersist;
        InitializeComponent();
        DataContext = record;

        // Apply persisted geometry. WPF uses Top/Left, not X/Y; the record stores screen coords
        // in DIPs which match WPF's coordinate system on a per-monitor-DPI-aware app (we are).
        Left = record.Geometry.X;
        Top = record.Geometry.Y;
        Width = record.Geometry.Width;
        Height = record.Geometry.Height;

        ApplyLockState();
        ApplyRollState();

        // Persist geometry on user-driven moves / resizes. Hooks fire continuously during drag;
        // the manager-level save is debounced inside the JSON store (semaphore + atomic rename),
        // but we additionally only persist on completion (LocationChanged + SizeChanged fire
        // after the gesture too — the last call always carries the final value).
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
            // Don't overwrite UnrolledHeight while the user is in a rolled state — the height
            // would collapse to the chrome height and lose the previous setpoint.
            if (!_record.IsRolled)
            {
                _record.Geometry.Height = Height;
                _record.Geometry.UnrolledHeight = Height;
            }
            _onPersist();
        };

        // Persist + cleanly intercept close. Closing through the window's own close path (Alt+F4,
        // an OS-driven kill) flushes the geometry one last time. Manager-driven close skips the
        // persist (the record was just deleted, no point writing it back).
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

    private void OnHeaderMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_record.IsLocked) return;
        if (e.ClickCount == 2)
        {
            // Double click on header → toggle roll-up. Configurable per the spec (Settings →
            // Wormholes → "Roll-up on title double-click"); the toggle plumbing lands with the
            // Settings card. For the skeleton we honour the default ON.
            ToggleRoll();
            return;
        }
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            // DragMove blocks until mouse-up — wrap in try/catch because if the window state
            // changes mid-drag (rare, but possible during a display change) DragMove throws.
            try { DragMove(); }
            catch (InvalidOperationException) { /* geometry is persisted via LocationChanged anyway */ }
        }
    }

    private void OnHeaderMouseUp(object sender, MouseButtonEventArgs e) { /* reserved for future inline-rename gesture */ }

    private void OnContentAreaMouseDown(object sender, MouseButtonEventArgs e)
    {
        // Drag from the content area too — Portals offers this as a toggle ("Allow Portal to be
        // Dragged by Dragging Content Area"); we make it the default. Locked wormholes ignore.
        if (_record.IsLocked) return;
        if (e.ChangedButton != MouseButton.Left) return;
        if (e.OriginalSource is System.Windows.Controls.Button) return; // chrome buttons handle their own click
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

    private void OnHamburgerClicked(object sender, RoutedEventArgs e)
    {
        // Stub menu — full hamburger contents land in the next pass (rename, hide, delete,
        // sort by, open folder, refresh, change folder, lock-from-menu). For the skeleton just
        // surface "Delete wormhole" so the user has a way to remove the placeholder they spawned.
        var menu = new System.Windows.Controls.ContextMenu();
        var delete = new System.Windows.Controls.MenuItem { Header = "Delete wormhole..." };
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

    /// <summary>Raised when the user picks "Delete wormhole..." from the hamburger menu. The
    /// manager subscribes in <c>SpawnWindow</c> and routes through its own <c>DeleteAsync</c>
    /// (record + folder + window close).</summary>
    public event EventHandler<Guid>? DeleteRequested;

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
