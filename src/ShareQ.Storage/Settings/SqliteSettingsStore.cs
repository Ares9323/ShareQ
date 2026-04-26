using System.Text;
using ShareQ.Storage.Database;
using ShareQ.Storage.Protection;

namespace ShareQ.Storage.Settings;

public sealed class SqliteSettingsStore : ISettingsStore
{
    private readonly IShareQDatabase _database;
    private readonly IPayloadProtector _protector;

    public SqliteSettingsStore(IShareQDatabase database, IPayloadProtector protector)
    {
        _database = database;
        _protector = protector;
    }

    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        var conn = _database.GetOpenConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value, is_sensitive FROM settings WHERE key = $key LIMIT 1;";
        cmd.Parameters.AddWithValue("$key", key);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;

        var stored = reader.GetString(0);
        var sensitive = reader.GetInt32(1) == 1;
        if (!sensitive) return stored;

        var ciphertext = Convert.FromBase64String(stored);
        var plaintext = _protector.Unprotect(ciphertext);
        return Encoding.UTF8.GetString(plaintext);
    }

    public async Task SetAsync(string key, string value, bool sensitive, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(value);
        var conn = _database.GetOpenConnection();

        var stored = sensitive
            ? Convert.ToBase64String(_protector.Protect(Encoding.UTF8.GetBytes(value)))
            : value;

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO settings (key, value, is_sensitive) VALUES ($key, $value, $sensitive)
            ON CONFLICT(key) DO UPDATE SET value = $value, is_sensitive = $sensitive;
            """;
        cmd.Parameters.AddWithValue("$key", key);
        cmd.Parameters.AddWithValue("$value", stored);
        cmd.Parameters.AddWithValue("$sensitive", sensitive ? 1 : 0);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> RemoveAsync(string key, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        var conn = _database.GetOpenConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM settings WHERE key = $key;";
        cmd.Parameters.AddWithValue("$key", key);
        var rows = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return rows == 1;
    }
}
