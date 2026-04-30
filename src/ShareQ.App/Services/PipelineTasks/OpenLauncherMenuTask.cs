using System.Text.Json.Nodes;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using ShareQ.App.Views;
using ShareQ.Core.Pipeline;

namespace ShareQ.App.Services.PipelineTasks;

/// <summary>Opens the launcher overlay — a 3×10 keyboard grid where every printable key is
/// mapped to a path / shortcut / shell target. Acts on its own (no payload required), so it
/// can be the entire workflow behind a global shortcut, MaxLaunchpad-style.</summary>
public sealed class OpenLauncherMenuTask : IPipelineTask
{
    public const string TaskId = "shareq.open-launcher-menu";

    private readonly IServiceProvider _services;

    public OpenLauncherMenuTask(IServiceProvider services) { _services = services; }

    public string Id => TaskId;
    public string DisplayName => "Open launcher menu";
    public PipelineTaskKind Kind => PipelineTaskKind.Both;

    public Task ExecuteAsync(PipelineContext context, JsonNode? config, CancellationToken cancellationToken)
    {
        // Window construction touches WPF state, has to land on the UI thread. We resolve the
        // window from the container so its constructor dependencies (LauncherStore + logger)
        // are wired by DI; a fresh instance per opening is cheap and avoids stale state.
        // Toggle behaviour: if the launcher is already up, close it instead of stacking a new
        // instance — the same shortcut becomes "open" on first press, "close" on the second.
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (LauncherWindow.IsOpen) { LauncherWindow.RequestClose(); return; }
            var window = _services.GetRequiredService<LauncherWindow>();
            window.Show();
            window.Activate();
        });
        return Task.CompletedTask;
    }
}
