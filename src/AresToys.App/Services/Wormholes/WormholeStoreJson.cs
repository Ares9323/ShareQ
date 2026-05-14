using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using AresToys.Storage.Paths;

namespace AresToys.App.Services.Wormholes;

/// <summary>Single-file JSON store for wormhole records. Lives under
/// <c>%LocalAppData%\AresToys-Data\Wormholes\wormholes.json</c> with a sibling <c>Shortcuts\</c>
/// folder owned by <c>DataDropPolicy</c> (one subfolder per wormhole id) for Data-fence
/// <c>.lnk</c> files.
///
/// In-memory cache is the source of truth at runtime: <see cref="LoadAllAsync"/> hydrates it
/// from disk once (idempotent — re-reads on every call) and every <see cref="SaveAsync"/> /
/// <see cref="DeleteAsync"/> mutates the cache + flushes the file via a temp-rename for crash
/// safety. There's no debounce yet; mutations are infrequent (drag-end, lock toggle, delete)
/// and the file is small.</summary>
public sealed class WormholeStoreJson : IWormholeStore, IDisposable
{
    private const string WormholesFolderName = "Wormholes";
    private const string ShortcutsFolderName = "Shortcuts";
    private const string StoreFileName = "wormholes.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private readonly IStoragePathResolver _paths;
    private readonly ILogger<WormholeStoreJson> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private List<WormholeRecord>? _cache;

    public WormholeStoreJson(IStoragePathResolver paths, ILogger<WormholeStoreJson> logger)
    {
        _paths = paths;
        _logger = logger;
    }

    public string WormholesRootPath => Path.Combine(_paths.ResolveRoot(), WormholesFolderName);

    private string StoreFilePath => Path.Combine(WormholesRootPath, StoreFileName);

    public string GetShortcutsDirectory(Guid wormholeId)
    {
        var dir = Path.Combine(WormholesRootPath, ShortcutsFolderName, wormholeId.ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    public async Task<IReadOnlyList<WormholeRecord>> LoadAllAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(WormholesRootPath);
            var path = StoreFilePath;
            if (!File.Exists(path))
            {
                _cache = new List<WormholeRecord>();
                return _cache.AsReadOnly();
            }

            try
            {
                var raw = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
                var file = JsonSerializer.Deserialize<WormholeStoreFile>(raw, JsonOptions);
                _cache = file?.Wormholes ?? new List<WormholeRecord>();
            }
            catch (JsonException ex)
            {
                // Malformed file: rename it aside with a timestamp and start with an empty list.
                // Better than crashing the module at every launch; the user can inspect the
                // .corrupt copy if they want to recover anything by hand.
                _logger.LogWarning(ex, "wormholes.json is malformed — renaming aside and starting fresh");
                try { File.Move(path, path + $".corrupt-{DateTime.UtcNow:yyyyMMddHHmmss}", overwrite: false); }
                catch (IOException) { /* best-effort */ }
                _cache = new List<WormholeRecord>();
            }
            return _cache.AsReadOnly();
        }
        finally { _gate.Release(); }
    }

    public async Task SaveAsync(WormholeRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureCacheLoadedNoLockAsync(cancellationToken).ConfigureAwait(false);
            var idx = _cache!.FindIndex(r => r.Id == record.Id);
            if (idx >= 0) _cache[idx] = record;
            else _cache.Add(record);
            await FlushNoLockAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task DeleteAsync(Guid wormholeId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureCacheLoadedNoLockAsync(cancellationToken).ConfigureAwait(false);
            var removed = _cache!.RemoveAll(r => r.Id == wormholeId);
            if (removed == 0) return;

            // Clean up the wormhole's Shortcuts\{id}\ folder if it exists. Best-effort — a
            // locked file (e.g. AV scan in progress) shouldn't block the record deletion in
            // memory + JSON; the user can clean leftover .lnk files manually.
            try
            {
                var shortcuts = Path.Combine(WormholesRootPath, ShortcutsFolderName, wormholeId.ToString("N"));
                if (Directory.Exists(shortcuts)) Directory.Delete(shortcuts, recursive: true);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to remove shortcuts folder for wormhole {Id}", wormholeId);
            }

            await FlushNoLockAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    private async Task EnsureCacheLoadedNoLockAsync(CancellationToken cancellationToken)
    {
        if (_cache is not null) return;
        // We're already inside the gate from the calling method; replicate the load path
        // without re-entering the semaphore (a single semaphore is not re-entrant — would
        // deadlock if Load grabbed it again).
        Directory.CreateDirectory(WormholesRootPath);
        if (!File.Exists(StoreFilePath))
        {
            _cache = new List<WormholeRecord>();
            return;
        }
        try
        {
            var raw = await File.ReadAllTextAsync(StoreFilePath, cancellationToken).ConfigureAwait(false);
            _cache = JsonSerializer.Deserialize<WormholeStoreFile>(raw, JsonOptions)?.Wormholes ?? new List<WormholeRecord>();
        }
        catch (JsonException) { _cache = new List<WormholeRecord>(); }
    }

    public void Dispose() => _gate.Dispose();

    private async Task FlushNoLockAsync(CancellationToken cancellationToken)
    {
        var file = new WormholeStoreFile { SchemaVersion = 1, Wormholes = _cache! };
        var json = JsonSerializer.Serialize(file, JsonOptions);
        Directory.CreateDirectory(WormholesRootPath);
        var tmp = StoreFilePath + ".tmp";
        await File.WriteAllTextAsync(tmp, json, cancellationToken).ConfigureAwait(false);
        // File.Move with overwrite is the closest portable substitute for a true atomic
        // rename on NTFS. On Windows it uses MoveFileEx with REPLACE_EXISTING which is
        // documented atomic for same-volume moves.
        File.Move(tmp, StoreFilePath, overwrite: true);
    }
}
