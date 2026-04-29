using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Windows;
using Microsoft.Extensions.Logging;
using ShareQ.App.Native;
using ShareQ.Core.Pipeline;

namespace ShareQ.App.Services.PipelineTasks;

/// <summary>
/// Sends a single keystroke (down + up) to the foreground window via SendInput. Useful as a
/// separator between paste-history-item steps when the user wants a newline / tab between
/// pasted snippets. Config: <c>{"key":"enter"}</c> (default), <c>"tab"</c>.
/// </summary>
public sealed class PressKeyTask : IPipelineTask
{
    public const string TaskId = "shareq.press-key";

    private readonly ILogger<PressKeyTask> _logger;

    public PressKeyTask(ILogger<PressKeyTask> logger)
    {
        _logger = logger;
    }

    public string Id => TaskId;
    public string DisplayName => "Press key";
    public PipelineTaskKind Kind => PipelineTaskKind.Both;

    public async Task ExecuteAsync(PipelineContext context, JsonNode? config, CancellationToken cancellationToken)
    {
        var keyName = ((string?)config?["key"])?.ToLowerInvariant() ?? "enter";
        var vk = keyName switch
        {
            "enter" or "return" or "newline" => AppNativeMethods.VkReturn,
            "tab" => AppNativeMethods.VkTab,
            _ => AppNativeMethods.VkReturn,
        };

        // Tiny pre-delay mirrors AutoPaster.PasteAsync — gives the previous step's Ctrl+V time to
        // be consumed by the foreground window before we inject the next keystroke. Without it
        // the Enter can race the paste and arrive before the pasted text actually lands.
        await Task.Delay(120, cancellationToken).ConfigureAwait(false);

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            // Same race as in AutoPaster: a Win+Shift+P-style trigger leaves modifiers physically
            // held; an injected Enter on top would become Win+Enter (action centre), Shift+Enter,
            // etc. Drop them before sending the action key.
            KeyInjector.ReleaseStickyModifiers();

            var inputs = new AppNativeMethods.INPUT[]
            {
                KeyInjector.MakeKey(vk, keyUp: false),
                KeyInjector.MakeKey(vk, keyUp: true),
            };
            var sent = AppNativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<AppNativeMethods.INPUT>());
            _logger.LogDebug("PressKeyTask: sent {Key} (vk 0x{Vk:X2}); SendInput returned {Sent}", keyName, vk, sent);
        });
    }
}
