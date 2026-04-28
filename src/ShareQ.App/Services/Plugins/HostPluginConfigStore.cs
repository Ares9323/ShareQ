using ShareQ.PluginContracts;
using ShareQ.Storage.Settings;

namespace ShareQ.App.Services.Plugins;

/// <summary>
/// Host implementation of <see cref="IPluginConfigStore"/>. Each plugin gets a namespaced view —
/// keys are stored under <c>plugin.&lt;pluginId&gt;.&lt;key&gt;</c> in the shared settings table.
/// Sensitive values are flagged so the underlying store encrypts them via DPAPI.
/// </summary>
public sealed class HostPluginConfigStore : IPluginConfigStore
{
    private readonly ISettingsStore _settings;
    private readonly string _pluginId;

    public HostPluginConfigStore(ISettingsStore settings, string pluginId)
    {
        _settings = settings;
        _pluginId = pluginId;
    }

    public Task<string?> GetAsync(string key, CancellationToken cancellationToken)
        => _settings.GetAsync(NamespacedKey(key), cancellationToken);

    public Task SetAsync(string key, string value, bool sensitive, CancellationToken cancellationToken)
        => _settings.SetAsync(NamespacedKey(key), value, sensitive, cancellationToken);

    public Task<bool> RemoveAsync(string key, CancellationToken cancellationToken)
        => _settings.RemoveAsync(NamespacedKey(key), cancellationToken);

    private string NamespacedKey(string key) => $"plugin.{_pluginId}.{key}";
}
