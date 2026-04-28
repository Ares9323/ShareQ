using System.Text.Json.Nodes;
using System.Windows;
using ShareQ.Core.Pipeline;

namespace ShareQ.App.Services.PipelineTasks;

/// <summary>Opens the Win+V clipboard popup. Single-step workflow that the popup hotkey runs.</summary>
public sealed class OpenPopupTask : IPipelineTask
{
    public const string TaskId = "shareq.open-popup";

    private readonly PopupWindowController _controller;

    public OpenPopupTask(PopupWindowController controller)
    {
        _controller = controller;
    }

    public string Id => TaskId;
    public string DisplayName => "Show clipboard popup";
    public PipelineTaskKind Kind => PipelineTaskKind.Both;

    public Task ExecuteAsync(PipelineContext context, JsonNode? config, CancellationToken cancellationToken)
    {
        // PopupWindowController must be touched on the UI thread (creates / shows a Window).
        Application.Current.Dispatcher.InvokeAsync(() => _ = _controller.ShowAsync());
        return Task.CompletedTask;
    }
}
