using Microsoft.Data.Sqlite;
using ShareQ.Core.Pipeline;
using ShareQ.Storage.Database;

namespace ShareQ.Pipeline.Profiles;

public sealed class SqlitePipelineProfileStore : IPipelineProfileStore
{
    private readonly IShareQDatabase _database;

    public SqlitePipelineProfileStore(IShareQDatabase database)
    {
        _database = database;
    }

    public async Task<PipelineProfile?> GetAsync(string id, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        var conn = _database.GetOpenConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, display_name, trigger, tasks_json FROM pipeline_profiles WHERE id = $id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", id);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        return Map(reader);
    }

    public async Task<IReadOnlyList<PipelineProfile>> ListAsync(CancellationToken cancellationToken)
    {
        var conn = _database.GetOpenConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, display_name, trigger, tasks_json FROM pipeline_profiles ORDER BY id;";
        var results = new List<PipelineProfile>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(Map(reader));
        }
        return results;
    }

    public async Task UpsertAsync(PipelineProfile profile, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var conn = _database.GetOpenConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO pipeline_profiles (id, display_name, trigger, tasks_json)
            VALUES ($id, $display, $trigger, $tasks)
            ON CONFLICT(id) DO UPDATE SET
                display_name = $display,
                trigger = $trigger,
                tasks_json = $tasks;
            """;
        cmd.Parameters.AddWithValue("$id", profile.Id);
        cmd.Parameters.AddWithValue("$display", profile.DisplayName);
        cmd.Parameters.AddWithValue("$trigger", profile.Trigger);
        cmd.Parameters.AddWithValue("$tasks", PipelineProfileSerializer.SerializeSteps(profile.Steps));
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        var conn = _database.GetOpenConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM pipeline_profiles WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        var rows = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return rows == 1;
    }

    private static PipelineProfile Map(SqliteDataReader reader)
    {
        var id = reader.GetString(0);
        var displayName = reader.GetString(1);
        var trigger = reader.GetString(2);
        var tasksJson = reader.GetString(3);
        var steps = PipelineProfileSerializer.DeserializeSteps(tasksJson);
        return new PipelineProfile(id, displayName, trigger, steps);
    }
}
