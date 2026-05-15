using System.Text;
using System.Windows;
using Microsoft.Extensions.Logging;
using AresToys.App.Native;
using AresToys.Clipboard;
using AresToys.Core.Domain;
using AresToys.Storage.Items;

namespace AresToys.App.Services;

public sealed class AutoPaster
{
    private readonly IItemStore _items;
    private readonly TargetWindowTracker _target;
    private readonly IClipboardListener? _listener;
    private readonly ILogger<AutoPaster> _logger;

    public AutoPaster(IItemStore items, TargetWindowTracker target, ILogger<AutoPaster> logger, IClipboardListener? listener = null)
    {
        _items = items;
        _target = target;
        _logger = logger;
        // Optional — when the clipboard module is disabled (Settings → Modules) the listener
        // singleton isn't registered. We still want the paste to work; just skip the
        // SuppressNext call and accept the round-trip ingestion in that edge case.
        _listener = listener;
    }

    public async Task PasteAsync(long itemId, CancellationToken cancellationToken)
    {
        var record = await _items.GetByIdAsync(itemId, cancellationToken).ConfigureAwait(false);
        if (record is null) return;

        // Apply clipboard + restore foreground synchronously on the UI thread to keep
        // the foreground state consistent. SendInput then fires after a short settle delay.
        var ok = await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            // Tell the clipboard listener to drop the next ingestion — without this our own
            // SetText / SetPng below would echo back through Win32ClipboardReader and land as
            // a NEW item in the history every time the user pastes, producing a duplicate
            // entry on every Ctrl+V / hotkey paste.
            _listener?.SuppressNext();

            switch (record.Kind)
            {
                case ItemKind.Text:
                    var plainText = Encoding.UTF8.GetString(record.Payload.Span);
                    System.Windows.Clipboard.SetText(plainText);
                    break;

                case ItemKind.Html:
                    // Strip HTML on the full payload at paste time — NOT from record.SearchText.
                    // SearchText is the truncated 256-char preview written by Win32ClipboardReader
                    // for the FTS index + row label; using it as the paste source means long HTML
                    // payloads (ASCII art from a web page, long blocks of pre-formatted text)
                    // arrive at the target app cut short with a "…" tacked on. Falling back to
                    // SearchText is only safe when the live extraction returns empty.
                    var htmlPayload = Encoding.UTF8.GetString(record.Payload.Span);
                    var htmlPlain = ClipboardCleaning.HtmlToPlain(htmlPayload);
                    if (string.IsNullOrEmpty(htmlPlain) && !string.IsNullOrEmpty(record.SearchText))
                        htmlPlain = record.SearchText;
                    System.Windows.Clipboard.SetText(htmlPlain);
                    break;

                case ItemKind.Rtf:
                    // Same rationale as the HTML branch above — run the live RTF stripper on
                    // the full payload so long RTF documents don't paste with the SearchText
                    // truncation suffix.
                    var rtfPayload = Encoding.UTF8.GetString(record.Payload.Span);
                    var rtfPlain = ClipboardCleaning.RtfToPlain(rtfPayload);
                    if (string.IsNullOrEmpty(rtfPlain) && !string.IsNullOrEmpty(record.SearchText))
                        rtfPlain = record.SearchText;
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
                        // PNG-aware publish (preserves alpha for Telegram / Discord / browsers).
                        // SetImage alone publishes only CF_BITMAP and modern apps interpret it
                        // as no-alpha — semi-transparent pixels render opaque on paste.
                        ClipboardImagePublisher.SetPng(pngBytes);
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

        // If the user reached us via a hotkey like Win+Shift+P, those modifiers may still be
        // physically held when we get here — sending Ctrl+V on top would yield Win+Ctrl+V (audio
        // mixer in Win11), Win+Shift+V, etc., not the plain paste we want. Release them first;
        // a tiny settle delay lets the up events propagate before the V down arrives.
        var released = KeyInjector.ReleaseStickyModifiers();
        if (released > 0)
            await Task.Delay(40, cancellationToken).ConfigureAwait(false);

        var sent = SendCtrlV();
        _logger.LogDebug("AutoPaster: released {Released} sticky modifiers, SendInput returned {Sent} (expected 4)", released, sent);
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
