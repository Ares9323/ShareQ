using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging;
using ShareQ.App.Native;
using ShareQ.Core.Domain;
using ShareQ.Storage.Items;

namespace ShareQ.App.Services;

public sealed class AutoPaster
{
    private readonly IItemStore _items;
    private readonly TargetWindowTracker _target;
    private readonly ILogger<AutoPaster> _logger;

    public AutoPaster(IItemStore items, TargetWindowTracker target, ILogger<AutoPaster> logger)
    {
        _items = items;
        _target = target;
        _logger = logger;
    }

    public async Task PasteAsync(long itemId, CancellationToken cancellationToken)
    {
        var record = await _items.GetByIdAsync(itemId, cancellationToken).ConfigureAwait(false);
        if (record is null) return;

        // Apply clipboard + restore foreground synchronously on the UI thread to keep
        // the foreground state consistent. SendInput then fires after a short settle delay.
        var ok = await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            switch (record.Kind)
            {
                case ItemKind.Text:
                    var plainText = Encoding.UTF8.GetString(record.Payload.Span);
                    System.Windows.Clipboard.SetText(plainText);
                    break;

                case ItemKind.Html:
                    // SearchText (CF_UNICODETEXT or HTML-stripped fallback) is the clean plaintext we
                    // captured at copy time. Pasting raw HTML payload as text would dump <p>/<span>
                    // markup into the target.
                    var htmlPlain = !string.IsNullOrEmpty(record.SearchText)
                        ? record.SearchText
                        : ClipboardCleaning.HtmlToPlain(Encoding.UTF8.GetString(record.Payload.Span));
                    System.Windows.Clipboard.SetText(htmlPlain);
                    break;

                case ItemKind.Rtf:
                    var rtfPlain = !string.IsNullOrEmpty(record.SearchText)
                        ? record.SearchText
                        : ClipboardCleaning.RtfToPlain(Encoding.UTF8.GetString(record.Payload.Span));
                    System.Windows.Clipboard.SetText(rtfPlain);
                    break;

                case ItemKind.Image:
                    var pngBytes = record.Payload.ToArray();
                    _logger.LogDebug("AutoPaster image: {Length} bytes, first={B0:X2} {B1:X2} {B2:X2} {B3:X2}",
                        pngBytes.Length,
                        pngBytes.Length > 0 ? pngBytes[0] : (byte)0,
                        pngBytes.Length > 1 ? pngBytes[1] : (byte)0,
                        pngBytes.Length > 2 ? pngBytes[2] : (byte)0,
                        pngBytes.Length > 3 ? pngBytes[3] : (byte)0);
                    if (pngBytes.Length == 0) return false;
                    try
                    {
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.StreamSource = new MemoryStream(pngBytes);
                        bitmap.EndInit();
                        bitmap.Freeze();
                        System.Windows.Clipboard.SetImage(bitmap);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "AutoPaster image decode/clipboard set failed");
                        return false;
                    }
                    break;

                default:
                    return false; // Files / unknown kinds: paste not supported yet
            }

            var restoreOk = _target.TryRestoreCaptured();
            _logger.LogDebug("AutoPaster: TryRestoreCaptured returned {Ok}", restoreOk);
            return restoreOk;
        });

        if (!ok)
        {
            _logger.LogWarning("AutoPaster: TryRestoreCaptured returned false; not sending Ctrl+V");
            return;
        }

        await Task.Delay(120, cancellationToken).ConfigureAwait(false);
        var sent = SendCtrlV();
        _logger.LogDebug("AutoPaster: SendInput returned {Sent} (expected 4)", sent);
    }

    private static uint SendCtrlV()
    {
        var inputs = new AppNativeMethods.INPUT[4];
        inputs[0] = MakeKey(AppNativeMethods.VkControl, keyUp: false);
        inputs[1] = MakeKey(AppNativeMethods.VkV, keyUp: false);
        inputs[2] = MakeKey(AppNativeMethods.VkV, keyUp: true);
        inputs[3] = MakeKey(AppNativeMethods.VkControl, keyUp: true);
        return AppNativeMethods.SendInput((uint)inputs.Length, inputs, System.Runtime.InteropServices.Marshal.SizeOf<AppNativeMethods.INPUT>());
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
