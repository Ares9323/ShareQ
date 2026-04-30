using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ShareQ.Storage.Settings;

namespace ShareQ.App.Services;

/// <summary>Reads / writes the entire ShareQ settings store as a portable JSON document.
/// Sensitive values (OAuth tokens, credentials marked at write-time) are excluded from
/// exports — those are bound to the local user / machine via DPAPI and wouldn't decrypt on
/// another box anyway. Importing is non-destructive: existing keys are overwritten with the
/// imported values, missing keys are added, but keys present locally and absent from the
/// import file are left alone (so a partial backup doesn't wipe categories the user didn't
/// touch). The version field lets future formats migrate forward.</summary>
public sealed class SettingsBackupService
{
    private const int CurrentVersion = 1;
    /// <summary>Cache one configured options instance — analyzer (CA1869) flags allocating a
    /// new one per call as a perf footgun. Indented output stays the default since users open
    /// these files in editors / diff tools. <see cref="JavaScriptEncoder.UnsafeRelaxedJsonEscaping"/>
    /// disables the default HTML-safe escaping that turns embedded quotes into """ — our
    /// values are often nested JSON blobs (launcher state, hotkey config) where that produces
    /// unreadable noise. The "unsafe" tag is about HTML-context XSS, not filesystem use.</summary>
    private static readonly JsonSerializerOptions ExportOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly ISettingsStore _settings;
    private readonly ILogger<SettingsBackupService> _logger;

    public SettingsBackupService(ISettingsStore settings, ILogger<SettingsBackupService> logger)
    {
        _settings = settings;
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

        var doc = new BackupDocument
        {
            Version = CurrentVersion,
            ExportedAt = DateTimeOffset.UtcNow,
            Settings = entries,
        };
        await using var stream = File.Create(filePath);
        await JsonSerializer.SerializeAsync(stream, doc, ExportOptions, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("SettingsBackupService: exported {Count} settings to {Path}",
            entries.Count, filePath);
    }

    public async Task<int> ImportAsync(string filePath, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(filePath);
        var doc = await JsonSerializer.DeserializeAsync<BackupDocument>(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (doc is null || doc.Settings is null)
        {
            _logger.LogWarning("SettingsBackupService: import failed — empty or malformed file {Path}", filePath);
            return 0;
        }
        if (doc.Version > CurrentVersion)
        {
            // Forward-compat: a newer version of the app might add fields. We only know how
            // to re-apply key/value pairs, so do that and warn the user that exotic fields
            // might be ignored. Don't outright refuse — most backups will be plain k/v anyway.
            _logger.LogWarning("SettingsBackupService: import file is version {File}, app understands {App}; importing key/value entries only",
                doc.Version, CurrentVersion);
        }

        var imported = 0;
        foreach (var (key, value) in doc.Settings)
        {
            // Imported entries land as non-sensitive — sensitive values were never exported in
            // the first place, so any key found in the file is by construction non-sensitive.
            await _settings.SetAsync(key, value, sensitive: false, cancellationToken).ConfigureAwait(false);
            imported++;
        }
        _logger.LogInformation("SettingsBackupService: imported {Count} settings from {Path}",
            imported, filePath);
        return imported;
    }

    private sealed class BackupDocument
    {
        public int Version { get; set; }
        public DateTimeOffset ExportedAt { get; set; }
        public Dictionary<string, string>? Settings { get; set; }
    }
}
