using ShareQ.PluginContracts;
using ShareQ.Plugins;
using ShareQ.Storage.Settings;

namespace ShareQ.App.Services.Plugins;

/// <summary>
/// Central catalog of plugins (built-in + externally-loaded) with per-id enabled/disabled state
/// persisted in the settings store. Consumers (UploadTask, settings UI) query through here so
/// disabled plugins disappear from the active surface without being unloaded from memory.
/// </summary>
public sealed class PluginRegistry : IUploaderResolver
{
    private const string EnabledKeyPrefix = "plugin.";
    private const string EnabledKeySuffix = ".enabled";

    private readonly ISettingsStore _settings;
    private readonly IReadOnlyDictionary<string, IUploader> _uploaders;
    private readonly IReadOnlyList<PluginDescriptor> _externalDescriptors;

    public PluginRegistry(
        ISettingsStore settings,
        IEnumerable<IUploader> uploaders,
        IReadOnlyList<PluginDescriptor>? externalDescriptors = null)
    {
        _settings = settings;
        _uploaders = uploaders.ToDictionary(u => u.Id, StringComparer.Ordinal);
        _externalDescriptors = externalDescriptors ?? [];
    }

    /// <summary>Resolve an uploader by id. Returns null if it isn't registered or has been disabled.</summary>
    public async Task<IUploader?> ResolveAsync(string uploaderId, CancellationToken cancellationToken)
    {
        if (!_uploaders.TryGetValue(uploaderId, out var uploader)) return null;
        return await IsEnabledAsync(uploaderId, cancellationToken).ConfigureAwait(false) ? uploader : null;
    }

    /// <summary>All known uploaders (enabled + disabled), used by the settings UI listing.</summary>
    public IReadOnlyList<IUploader> AllUploaders => _uploaders.Values.ToList();

    public IReadOnlyList<PluginDescriptor> ExternalDescriptors => _externalDescriptors;

    /// <summary>Descriptor list for the settings UI: built-in entries are synthesized so
    /// disabled-toggle and metadata work uniformly across built-in and external plugins.</summary>
    public IReadOnlyList<PluginDescriptor> AllDescriptors()
    {
        var result = new List<PluginDescriptor>(_uploaders.Count + _externalDescriptors.Count);
        var externalIds = new HashSet<string>(_externalDescriptors.Select(d => d.Id), StringComparer.Ordinal);

        foreach (var uploader in _uploaders.Values)
        {
            if (externalIds.Contains(uploader.Id)) continue; // external entry already covers this id
            var manifest = new PluginManifest(
                Id: uploader.Id,
                Version: "built-in",
                DisplayName: uploader.DisplayName,
                DllName: string.Empty,
                ContractVersion: "n/a",
                Description: $"Built-in {uploader.DisplayName} uploader.");
            result.Add(new PluginDescriptor(
                uploader.Id, uploader.DisplayName, "built-in",
                BuiltIn: true, FolderPath: null, Assembly: null, Manifest: manifest));
        }
        result.AddRange(_externalDescriptors);
        return result;
    }

    public async Task<bool> IsEnabledAsync(string pluginId, CancellationToken cancellationToken)
    {
        var raw = await _settings.GetAsync(KeyFor(pluginId), cancellationToken).ConfigureAwait(false);
        // Default: enabled. The user has to opt out explicitly by toggling off in settings.
        return raw is null || string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase);
    }

    public Task SetEnabledAsync(string pluginId, bool enabled, CancellationToken cancellationToken)
        => _settings.SetAsync(KeyFor(pluginId), enabled ? "true" : "false", sensitive: false, cancellationToken);

    private static string KeyFor(string pluginId) => $"{EnabledKeyPrefix}{pluginId}{EnabledKeySuffix}";
}
