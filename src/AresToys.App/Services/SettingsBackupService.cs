using System.IO;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using AresToys.App.Services.Launcher;
using AresToys.Core.Domain;
using AresToys.Storage.Items;
using AresToys.Storage.Settings;

namespace AresToys.App.Services;

/// <summary>Reads / writes the entire AresToys settings store as a portable JSON document.
/// Sensitive values (OAuth tokens, credentials marked at write-time) are excluded from
/// exports — those are bound to the local user / machine via DPAPI and wouldn't decrypt on
/// another box anyway. Importing is non-destructive: existing keys are overwritten with the
/// imported values, missing keys are added, but keys present locally and absent from the
/// import file are left alone (so a partial backup doesn't wipe categories the user didn't
/// touch). Pinned items and user-defined categories are also bundled — pinned payloads
/// travel in the clear (base64) on the assumption the user doesn't pin sensitive data,
/// matching how the rest of the clipboard is treated. The version field lets future formats
/// migrate forward.</summary>
public sealed class SettingsBackupService
{
    private const int CurrentVersion = 3;
    /// <summary>Cache one configured options instance — analyzer (CA1869) flags allocating a
    /// new one per call as a perf footgun. Indented output stays the default since users open
    /// these files in editors / diff tools. <see cref="JavaScriptEncoder.UnsafeRelaxedJsonEscaping"/>
    /// disables the default HTML-safe escaping that turns embedded quotes into &quot; — our
    /// values are often nested JSON blobs (launcher state, hotkey config) where that produces
    /// unreadable noise. The "unsafe" tag is about HTML-context XSS, not filesystem use.</summary>
    private static readonly JsonSerializerOptions ExportOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly ISettingsStore _settings;
    private readonly ICategoryStore _categories;
    private readonly IItemStore _items;
    private readonly LauncherStore _launcher;
    private readonly ILogger<SettingsBackupService> _logger;

    public SettingsBackupService(
        ISettingsStore settings,
        ICategoryStore categories,
        IItemStore items,
        LauncherStore launcher,
        ILogger<SettingsBackupService> logger)
    {
        _settings = settings;
        _categories = categories;
        _items = items;
        _launcher = launcher;
        _logger = logger;
    }

    public async Task ExportAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        await foreach (var entry in _settings.EnumerateAsync(includeSensitive: false, cancellationToken)
                                              .ConfigureAwait(false))
        {
            entries[entry.Key] = entry.Value;
        }

        // Skip the seeded 'Clipboard' bucket — every install has it, exporting it just risks
        // overwriting the local user's customised default on import.
        var allCategories = await _categories.ListAsync(cancellationToken).ConfigureAwait(false);
        var customCategories = allCategories
            .Where(c => !string.Equals(c.Name, Category.Default, StringComparison.Ordinal))
            .Select(BackupCategory.From)
            .ToList();

        // Page through pinned rows — passing int.MaxValue as Limit blows up because ItemStore
        // pre-sizes a List<>(capacity) with that value (2B refs). 1000 / page is plenty: pinned
        // counts are tiny by definition (rarely more than a few dozen).
        var pinnedItems = new List<BackupPinnedItem>();
        await foreach (var rec in EnumerateAllAsync(pinnedOnly: true, includePayload: true, cancellationToken).ConfigureAwait(false))
        {
            pinnedItems.Add(BackupPinnedItem.From(rec));
        }

        var doc = new BackupDocument
        {
            Version = CurrentVersion,
            ExportedAt = DateTimeOffset.UtcNow,
            Settings = entries,
            Categories = customCategories,
            PinnedItems = pinnedItems,
        };
        await using var stream = File.Create(filePath);
        await JsonSerializer.SerializeAsync(stream, doc, ExportOptions, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("SettingsBackupService: exported {Settings} settings, {Categories} categories, {Pinned} pinned items to {Path}",
            entries.Count, customCategories.Count, pinnedItems.Count, filePath);
    }

    public async Task<ImportResult> ImportAsync(string filePath, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(filePath);
        var doc = await JsonSerializer.DeserializeAsync<BackupDocument>(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (doc is null)
        {
            _logger.LogWarning("SettingsBackupService: import failed — empty or malformed file {Path}", filePath);
            return ImportResult.Empty;
        }
        if (doc.Version > CurrentVersion)
        {
            // Forward-compat: a newer version of the app might add fields. We only know how
            // to re-apply the sections we recognise, so do that and warn the user that exotic
            // fields might be ignored. Don't outright refuse — most backups will be plain k/v.
            _logger.LogWarning("SettingsBackupService: import file is version {File}, app understands {App}; importing recognised sections only",
                doc.Version, CurrentVersion);
        }

        var settingsImported = 0;
        var launcherStateTouched = false;
        if (doc.Settings is not null)
        {
            foreach (var (key, value) in doc.Settings)
            {
                // Imported entries land as non-sensitive — sensitive values were never exported in
                // the first place, so any key found in the file is by construction non-sensitive.
                await _settings.SetAsync(key, value, sensitive: false, cancellationToken).ConfigureAwait(false);
                settingsImported++;
                if (key is "launcher.state" or "launcher.cells") launcherStateTouched = true;
            }
        }
        // The launcher pre-warms its in-memory view at app startup and uses a monotonic version
        // counter on LauncherStore to decide whether to reload on the next PrepareAsync. Direct
        // ISettingsStore writes above bypass LauncherStore.SaveAsync, so the counter stays put
        // and the pre-warmed window keeps showing the old (often empty) grid until the user
        // mutates a cell. Bump it explicitly here so the next open sees fresh data.
        if (launcherStateTouched) _launcher.BumpStateVersion();

        var categoriesImported = 0;
        if (doc.Categories is not null)
        {
            foreach (var bc in doc.Categories)
            {
                if (string.IsNullOrWhiteSpace(bc.Name)) continue;
                // Never overwrite the default bucket — it's seeded and shared across installs.
                if (string.Equals(bc.Name, Category.Default, StringComparison.Ordinal)) continue;
                var existing = await _categories.GetAsync(bc.Name, cancellationToken).ConfigureAwait(false);
                var category = new Category(bc.Name, bc.Icon, bc.SortOrder, bc.MaxItems, bc.AutoCleanupAfter);
                if (existing is null)
                    await _categories.AddAsync(category, cancellationToken).ConfigureAwait(false);
                else
                    await _categories.UpdateAsync(category, cancellationToken).ConfigureAwait(false);
                categoriesImported++;
            }
        }

        var pinnedImported = 0;
        var pinnedSkipped = 0;
        if (doc.PinnedItems is { Count: > 0 } pinnedFromFile)
        {
            // Build the dedup index up-front: for every existing non-deleted item, hash its
            // payload so imported entries with identical (kind, payload) are skipped. We hash
            // the *plaintext* payload exposed by ItemStore (DPAPI decryption already happened),
            // which matches what's in the backup file. Pagination keeps memory bounded even on
            // large libraries — passing int.MaxValue as Limit overflows ItemStore's
            // List<>(capacity) pre-allocation.
            var existingHashes = new HashSet<(ItemKind Kind, string Hash)>();
            await foreach (var rec in EnumerateAllAsync(pinnedOnly: false, includePayload: true, cancellationToken).ConfigureAwait(false))
            {
                if (rec.Payload.IsEmpty) continue;
                existingHashes.Add((rec.Kind, HashPayload(rec.Payload.Span)));
            }

            foreach (var bp in pinnedFromFile)
            {
                if (string.IsNullOrEmpty(bp.PayloadBase64)) continue;
                if (!Enum.TryParse<ItemKind>(bp.Kind, ignoreCase: false, out var kind)) continue;
                if (!Enum.TryParse<ItemSource>(bp.Source, ignoreCase: false, out var source)) source = ItemSource.Clipboard;

                byte[] payload;
                try { payload = Convert.FromBase64String(bp.PayloadBase64); }
                catch (FormatException) { continue; }

                var hash = HashPayload(payload);
                if (!existingHashes.Add((kind, hash)))
                {
                    pinnedSkipped++;
                    continue;
                }

                var newItem = new NewItem(
                    Kind: kind,
                    Source: source,
                    CreatedAt: bp.CreatedAt == default ? DateTimeOffset.UtcNow : bp.CreatedAt,
                    Payload: payload,
                    PayloadSize: payload.LongLength,
                    Pinned: true,
                    SourceProcess: bp.SourceProcess,
                    SourceWindow: bp.SourceWindow,
                    UploadedUrl: bp.UploadedUrl,
                    UploaderId: bp.UploaderId,
                    SearchText: bp.SearchText,
                    Category: string.IsNullOrEmpty(bp.Category) ? Category.Default : bp.Category,
                    Label: bp.Label);
                await _items.AddAsync(newItem, cancellationToken).ConfigureAwait(false);
                pinnedImported++;
            }
        }

        _logger.LogInformation("SettingsBackupService: imported {Settings} settings, {Categories} categories, {Pinned} pinned ({Skipped} skipped) from {Path}",
            settingsImported, categoriesImported, pinnedImported, pinnedSkipped, filePath);
        return new ImportResult(settingsImported, categoriesImported, pinnedImported, pinnedSkipped);
    }

    /// <summary>Pages through ItemStore in 1000-row chunks. ItemStore.ListAsync allocates a
    /// List with capacity == Limit, so passing int.MaxValue triggers an overflow on the
    /// underlying array. Pagination also caps peak memory when payloads are large (images).</summary>
    private async IAsyncEnumerable<ItemRecord> EnumerateAllAsync(
        bool pinnedOnly,
        bool includePayload,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        const int PageSize = 1000;
        var offset = 0;
        while (true)
        {
            var page = await _items.ListAsync(
                new ItemQuery(
                    Limit: PageSize,
                    Offset: offset,
                    Pinned: pinnedOnly ? true : null,
                    IncludePayload: includePayload,
                    IncludeThumbnail: false),
                cancellationToken).ConfigureAwait(false);
            if (page.Count == 0) yield break;
            foreach (var rec in page) yield return rec;
            if (page.Count < PageSize) yield break;
            offset += PageSize;
        }
    }

    private static string HashPayload(ReadOnlySpan<byte> payload)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(payload, hash);
        return Convert.ToHexString(hash);
    }

    public readonly record struct ImportResult(int Settings, int Categories, int PinnedItems, int PinnedSkipped)
    {
        public static ImportResult Empty => new(0, 0, 0, 0);
    }

    private sealed class BackupDocument
    {
        public int Version { get; set; }
        public DateTimeOffset ExportedAt { get; set; }
        public Dictionary<string, string>? Settings { get; set; }
        public List<BackupCategory>? Categories { get; set; }
        public List<BackupPinnedItem>? PinnedItems { get; set; }
    }

    private sealed class BackupCategory
    {
        public string Name { get; set; } = string.Empty;
        public string? Icon { get; set; }
        public int SortOrder { get; set; }
        public int MaxItems { get; set; }
        public int AutoCleanupAfter { get; set; }

        public static BackupCategory From(Category c) => new()
        {
            Name = c.Name,
            Icon = c.Icon,
            SortOrder = c.SortOrder,
            MaxItems = c.MaxItems,
            AutoCleanupAfter = c.AutoCleanupAfter,
        };
    }

    private sealed class BackupPinnedItem
    {
        public string Kind { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; }
        public string? SourceProcess { get; set; }
        public string? SourceWindow { get; set; }
        public string? UploadedUrl { get; set; }
        public string? UploaderId { get; set; }
        public string? SearchText { get; set; }
        public string? Category { get; set; }
        /// <summary>Optional per-item label (CopyQ "Notes" equivalent). v2 backups omit this
        /// field; <see cref="ImportAsync"/> imports them with a null label and the user can
        /// add one after the fact. Added in backup schema v3.</summary>
        public string? Label { get; set; }
        /// <summary>Raw item payload, base64-encoded. Stored in the clear: pinned items are
        /// assumed not to contain secrets (the user explicitly chose to pin them), and the
        /// alternative — DPAPI ciphertext — wouldn't survive a move to another machine.</summary>
        public string PayloadBase64 { get; set; } = string.Empty;

        public static BackupPinnedItem From(ItemRecord r) => new()
        {
            Kind = r.Kind.ToString(),
            Source = r.Source.ToString(),
            CreatedAt = r.CreatedAt,
            SourceProcess = r.SourceProcess,
            SourceWindow = r.SourceWindow,
            UploadedUrl = r.UploadedUrl,
            UploaderId = r.UploaderId,
            SearchText = r.SearchText,
            Category = r.Category,
            Label = r.Label,
            PayloadBase64 = r.Payload.IsEmpty ? string.Empty : Convert.ToBase64String(r.Payload.Span),
        };
    }
}
