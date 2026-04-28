namespace ShareQ.PluginContracts;

/// <summary>
/// Per-plugin key/value store for settings. Values flagged <c>sensitive</c> are encrypted at rest
/// (DPAPI on the host) so plugins don't need to handle their own crypto.
/// Resolved by id behind the scenes — each plugin gets its own namespaced view.
/// </summary>
public interface IPluginConfigStore
{
    Task<string?> GetAsync(string key, CancellationToken cancellationToken);
    Task SetAsync(string key, string value, bool sensitive, CancellationToken cancellationToken);
    Task<bool> RemoveAsync(string key, CancellationToken cancellationToken);
}
