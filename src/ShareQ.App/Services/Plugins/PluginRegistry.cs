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

    public async Task<IReadOnlyList<string>> GetSelectedIdsAsync(UploaderCapabilities category, CancellationToken cancellationToken)
    {
        var raw = await _settings.GetAsync(SelectionKey(category), cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrEmpty(raw)) return [];
        return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public Task SetSelectedIdsAsync(UploaderCapabilities category, IReadOnlyList<string> ids, CancellationToken cancellationToken)
        => _settings.SetAsync(SelectionKey(category), string.Join(',', ids), sensitive: false, cancellationToken);

    public async Task<IReadOnlyList<IUploader>> ResolveCategoryAsync(UploaderCapabilities category, CancellationToken cancellationToken)
    {
        var primary = await ResolveStrictCategoryAsync(category, cancellationToken).ConfigureAwait(false);
        if (primary.Count > 0) return primary;

        // Fallback: if the user hasn't configured a category-specific list, fall back to the more
        // generic "file" selection. Generic uploaders (Catbox/OneDrive/S3/...) cover images, video,
        // and text just fine; specific image hosts (Imgur etc) only show up here when the user has
        // explicitly opted into them, so this fallback never silently changes their preference.
        if (category != UploaderCapabilities.File)
        {
            return await ResolveStrictCategoryAsync(UploaderCapabilities.File, cancellationToken).ConfigureAwait(false);
        }
        return [];
    }

    private async Task<IReadOnlyList<IUploader>> ResolveStrictCategoryAsync(UploaderCapabilities category, CancellationToken cancellationToken)
    {
        var selected = await GetSelectedIdsAsync(category, cancellationToken).ConfigureAwait(false);
        var result = new List<IUploader>(selected.Count);
        foreach (var id in selected)
        {
            if (!_uploaders.TryGetValue(id, out var uploader)) continue;
            if ((uploader.Capabilities & category) == 0) continue; // capability mismatch
            if (!await IsEnabledAsync(id, cancellationToken).ConfigureAwait(false)) continue;
            result.Add(uploader);
        }
        return result;
    }

    private static string SelectionKey(UploaderCapabilities category) => $"uploaders.{category.ToString().ToLowerInvariant()}";
}
