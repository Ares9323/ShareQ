namespace ShareQ.PluginContracts;

/// <summary>
/// Factory for per-plugin <see cref="IPluginConfigStore"/> instances. Plugins inject this and
/// call <see cref="Create"/> with their own id to get a namespaced view of the settings table.
/// </summary>
public interface IPluginConfigStoreFactory
{
    IPluginConfigStore Create(string pluginId);
}
