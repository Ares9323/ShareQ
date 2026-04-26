namespace ShareQ.Storage.Settings;

public interface ISettingsStore
{
    Task<string?> GetAsync(string key, CancellationToken cancellationToken);
    Task SetAsync(string key, string value, bool sensitive, CancellationToken cancellationToken);
    Task<bool> RemoveAsync(string key, CancellationToken cancellationToken);
}
