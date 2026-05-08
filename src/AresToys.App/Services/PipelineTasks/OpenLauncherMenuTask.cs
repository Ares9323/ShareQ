using System.Text.Json.Nodes;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using AresToys.App.Views;
using AresToys.Core.Pipeline;

namespace AresToys.App.Services.PipelineTasks;

/// <summary>Opens the launcher overlay — a 3×10 keyboard grid where every printable key is
/// mapped to a path / shortcut / shell target. Acts on its own (no payload required), so it
/// can be the entire workflow behind a global shortcut, MaxLaunchpad-style.</summary>
public sealed class OpenLauncherMenuTask : IPipelineTask
{
    public const string TaskId = "arestoys.open-launcher-menu";

    private readonly IServiceProvider _services;

    public OpenLauncherMenuTask(IServiceProvider services) { _services = services; }

    public string Id => TaskId;
    public string DisplayName => "Open launcher menu";
    public PipelineTaskKind Kind => PipelineTaskKind.Both;

    public Task ExecuteAsync(PipelineContext context, JsonNode? config, CancellationToken cancellationToken)
    {
        // Window construction touches WPF state, has to land on the UI thread. We resolve the
        // window from the container (singleton) so DI wires its constructor dependencies and
        // subsequent opens reuse the same instance. PrepareAsync runs BEFORE Show so the
        // cell grid paints already populated — eliminates the old "grey flash" on open.
        // Toggle behaviour: if the launcher is already up, close it instead of stacking a new
        // instance — the same shortcut becomes "open" on first press, "close" on the second.
        Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            if (LauncherWindow.IsOpen) { LauncherWindow.RequestClose(); return; }
            var window = _services.GetRequiredService<LauncherWindow>();
            await window.PrepareAsync();
            window.Show();
            window.Activate();
        });
        return Task.CompletedTask;
    }
}
