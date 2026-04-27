using System.Text;
using System.Windows;
using ShareQ.App.Native;
using ShareQ.Core.Domain;
using ShareQ.Storage.Items;

namespace ShareQ.App.Services;

public sealed class AutoPaster
{
    private readonly IItemStore _items;
    private readonly TargetWindowTracker _target;

    public AutoPaster(IItemStore items, TargetWindowTracker target)
    {
        _items = items;
        _target = target;
    }

    public async Task PasteAsync(long itemId, CancellationToken cancellationToken)
    {
        var record = await _items.GetByIdAsync(itemId, cancellationToken).ConfigureAwait(false);
        if (record is null) return;
        if (record.Kind is not (ItemKind.Text or ItemKind.Html or ItemKind.Rtf)) return;

        var text = Encoding.UTF8.GetString(record.Payload.Span);
        await Application.Current.Dispatcher.InvokeAsync(() => System.Windows.Clipboard.SetText(text));

        if (!_target.TryRestoreCaptured()) return;
        await Task.Delay(40, cancellationToken).ConfigureAwait(false);

        SendCtrlV();
    }

    private static void SendCtrlV()
    {
        var inputs = new AppNativeMethods.INPUT[4];
        inputs[0] = MakeKey(AppNativeMethods.VkControl, keyUp: false);
        inputs[1] = MakeKey(AppNativeMethods.VkV, keyUp: false);
        inputs[2] = MakeKey(AppNativeMethods.VkV, keyUp: true);
        inputs[3] = MakeKey(AppNativeMethods.VkControl, keyUp: true);
        AppNativeMethods.SendInput((uint)inputs.Length, inputs, System.Runtime.InteropServices.Marshal.SizeOf<AppNativeMethods.INPUT>());
    }

    private static AppNativeMethods.INPUT MakeKey(ushort virtualKey, bool keyUp) => new()
    {
        type = AppNativeMethods.InputKeyboard,
        u = new AppNativeMethods.InputUnion
        {
            ki = new AppNativeMethods.KEYBDINPUT
            {
                wVk = virtualKey,
                dwFlags = keyUp ? AppNativeMethods.KeyEventfKeyUp : 0
            }
        }
    };
}
