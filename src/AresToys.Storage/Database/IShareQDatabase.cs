using Microsoft.Data.Sqlite;

namespace AresToys.Storage.Database;

public interface IAresToysDatabase : IAsyncDisposable
{
    /// <summary>Open and migrate the database. Idempotent.</summary>
    Task InitializeAsync(CancellationToken cancellationToken);

    /// <summary>Borrow the underlying connection. Storage internals only — do not expose to consumers.</summary>
    SqliteConnection GetOpenConnection();
}
