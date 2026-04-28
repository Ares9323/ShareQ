using ShareQ.App.Services.Plugins;

namespace ShareQ.App.ViewModels;

/// <summary>
/// Builds the runtime list of <see cref="WorkflowActionDescriptor"/>s the user can pick from in
/// the workflow editor. Combines the static catalog (built-in tasks like Capture / Save / Notify)
/// with one synthetic <c>shareq.upload</c> descriptor per <em>enabled</em> uploader, so the picker
/// shows entries like "Upload to OneDrive" / "Upload to Catbox" alongside the generic
/// "Upload to selected image uploaders" / "...selected file uploaders" category entries.
///
/// Why dynamic: the set of available uploaders (built-in + external plugins, each toggleable in
/// Settings → Plugins) is only known at runtime. A static enum can't enumerate user-installed
/// plugins, and we don't want disabled uploaders cluttering the picker.
/// </summary>
public sealed class WorkflowActionProvider
{
    private readonly PluginRegistry _registry;

    public WorkflowActionProvider(PluginRegistry registry)
    {
        _registry = registry;
    }

    public async Task<IReadOnlyList<WorkflowActionDescriptor>> GetAllAsync(CancellationToken cancellationToken)
    {
        var list = new List<WorkflowActionDescriptor>(WorkflowActionCatalog.All);
        foreach (var uploader in _registry.AllUploaders)
        {
            if (!await _registry.IsEnabledAsync(uploader.Id, cancellationToken).ConfigureAwait(false)) continue;
            // Embed the uploader id verbatim — they're already constrained to the regex used by
            // PluginContracts (lower-case, dash-separated), no JSON-string escaping needed.
            var configJson = $"{{\"uploader\":\"{uploader.Id}\"}}";
            list.Add(new WorkflowActionDescriptor(
                TaskId: "shareq.upload",
                DisplayName: $"Upload to {uploader.DisplayName}",
                Description: $"Upload the current bytes via the {uploader.DisplayName} uploader. Other uploaders aren't run.",
                Category: "Upload",
                DefaultConfigJson: configJson));
        }
        return list;
    }
}
