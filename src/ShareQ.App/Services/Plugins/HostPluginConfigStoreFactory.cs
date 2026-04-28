using ShareQ.PluginContracts;
using ShareQ.Storage.Settings;

namespace ShareQ.App.Services.Plugins;

public sealed class HostPluginConfigStoreFactory : IPluginConfigStoreFactory
{
    private readonly ISettingsStore _settings;

    public HostPluginConfigStoreFactory(ISettingsStore settings)
    {
        _settings = settings;
    }

    public IPluginConfigStore Create(string pluginId)
        => new HostPluginConfigStore(_settings, pluginId);
}
