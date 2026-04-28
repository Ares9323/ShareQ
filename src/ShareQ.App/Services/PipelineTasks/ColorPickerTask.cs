using System.Text.Json.Nodes;
using System.Windows;
using ShareQ.Core.Pipeline;

namespace ShareQ.App.Services.PipelineTasks;

/// <summary>Opens the on-screen color picker (eye-dropper) at the cursor position. Single-step
/// workflow that the color picker hotkey runs.</summary>
public sealed class ColorPickerTask : IPipelineTask
{
    public const string TaskId = "shareq.color-picker";

    private readonly ScreenColorPickerService _picker;

    public ColorPickerTask(ScreenColorPickerService picker)
    {
        _picker = picker;
    }

    public string Id => TaskId;
    public string DisplayName => "Screen color picker";
    public PipelineTaskKind Kind => PipelineTaskKind.Both;

    public Task ExecuteAsync(PipelineContext context, JsonNode? config, CancellationToken cancellationToken)
    {
        // PickAtCursor opens a UI overlay; must run on the WPF UI thread.
        Application.Current.Dispatcher.InvokeAsync(() => _picker.PickAtCursor());
        return Task.CompletedTask;
    }
}
