using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using ShareQ.App.Windows;

namespace ShareQ.App.Services;

public sealed partial class PopupWindowController
{
    private readonly IServiceProvider _services;
    private readonly TargetWindowTracker _target;
    private readonly AutoPaster _paster;
    private PopupWindow? _window;

    public PopupWindowController(IServiceProvider services, TargetWindowTracker target, AutoPaster paster)
    {
        _services = services;
        _target = target;
        _paster = paster;
    }

    public async Task ShowAsync()
    {
        _target.CaptureCurrentForeground();
        EnsureWindow();
        await _window!.ViewModel.RefreshAsync(CancellationToken.None).ConfigureAwait(true);

        if (TryGetCursorPosition(out var x, out var y))
        {
            _window!.Left = x;
            _window!.Top = y;
        }
        _window!.Show();
        _window!.Activate();
    }

    private void EnsureWindow()
    {
        if (_window is not null) return;
        _window = _services.GetRequiredService<PopupWindow>();
        _window.PasteRequested += OnPasteRequested;
    }

    private async void OnPasteRequested(object? sender, long itemId)
    {
        try
        {
            await _paster.PasteAsync(itemId, CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // Failures are logged inside collaborators.
        }
    }

    private static bool TryGetCursorPosition(out int x, out int y)
    {
        if (NativeCursor.GetCursorPos(out var pt))
        {
            x = pt.X;
            y = pt.Y;
            return true;
        }
        x = 0; y = 0;
        return false;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    private static partial class NativeCursor
    {
        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool GetCursorPos(out POINT lpPoint);
    }
}
