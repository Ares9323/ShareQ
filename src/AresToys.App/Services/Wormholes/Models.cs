using System.Text.Json.Serialization;

namespace AresToys.App.Services.Wormholes;

/// <summary>Type of wormhole. Decided at creation; never changes for an existing record (the
/// user deletes + re-creates to switch). Data = curated list of shortcuts in a private folder;
/// Portal = live mirror of an external folder via FileSystemWatcher (see docs/WormholesSpec.md
/// §2). Note is reserved for fase 2 and is not part of the MVP enum.</summary>
public enum WormholeKind
{
    Data,
    Portal,
}

/// <summary>Runtime visibility state for a wormhole window. Not persisted — recalculated on
/// every startup + every <c>WM_DISPLAYCHANGE</c>. <see cref="WormholeRecord.IsHidden"/> (which
/// IS persisted) takes precedence: an <c>IsHidden=true</c> wormhole stays <see cref="UserHidden"/>
/// regardless of monitor state.</summary>
public enum HibernationState
{
    Active,
    MonitorOffline,
    UserHidden,
}

/// <summary>Window position, size, and rolled-state pivot. <see cref="UnrolledHeight"/> stores
/// the height to restore to when the user unrolls a previously rolled-up wormhole — without it
/// every unroll would default to a single hard-coded value and lose the user's chosen height.
/// <see cref="MonitorId"/> is best-effort (Windows display device name like
/// <c>\\.\DISPLAY1</c>) and used to decide hibernate vs. resurface on display-change events.</summary>
public sealed class WormholeGeometry
{
    public double X { get; set; } = 200;
    public double Y { get; set; } = 200;
    public double Width { get; set; } = 320;
    public double Height { get; set; } = 240;
    public double UnrolledHeight { get; set; } = 240;
    public string? MonitorId { get; set; }
}

/// <summary>A single entry inside a Data wormhole. <see cref="ShortcutPath"/> is relative to the
/// wormholes root (e.g. <c>Shortcuts\{wormholeId}\Notepad.lnk</c>) so a backup that zips the
/// wormholes folder is self-contained — paths inside the wormhole survive a restore on another
/// machine. For Portal wormholes the items are derived from the watched folder and not stored
/// here; the JSON only carries the Portal config.</summary>
public sealed class WormholeItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ShortcutPath { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public int DisplayOrder { get; set; }
}

public sealed class DataWormholeConfig
{
    public List<WormholeItem> Items { get; set; } = new();
}

public sealed class PortalWormholeConfig
{
    public string SourcePath { get; set; } = string.Empty;
    public bool IncludeSubdirectoriesAsItems { get; set; } = true;
    /// <summary>Sort mode label as a string (not an enum) so future modes can be added without
    /// breaking the persisted schema. Known values: <c>Name</c>, <c>Modified</c>, <c>Type</c>,
    /// <c>Manual</c> (uses <see cref="WormholeItem.DisplayOrder"/>). Unknown values fall back to
    /// <c>Name</c> at load time.</summary>
    public string SortMode { get; set; } = "Name";
}

/// <summary>Per-wormhole appearance overrides. The MVP renders everything against the global
/// theme; this record is present in the schema so future polish passes can add custom accent
/// colours and opacity sliders without a migration. Nullable means "use the theme default".</summary>
public sealed class WormholeAppearance
{
    public string? AccentOverride { get; set; }
    public double Opacity { get; set; } = 0.85;
}

/// <summary>Single wormhole record as persisted to <c>wormholes.json</c>. Lifecycle is owned by
/// <see cref="IWormholeStore"/> + <see cref="WormholeWindowManager"/>: the store hydrates the
/// list at startup, the manager spawns a window per record, and mutations from the UI flow back
/// through the store's <c>SaveAsync</c>.</summary>
public sealed class WormholeRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "Wormhole";
    public WormholeKind Kind { get; set; } = WormholeKind.Data;
    public WormholeGeometry Geometry { get; set; } = new();
    public bool IsLocked { get; set; }
    public bool IsRolled { get; set; }
    public bool IsHidden { get; set; }
    public WormholeAppearance Appearance { get; set; } = new();
    /// <summary>Present only when <see cref="Kind"/> is <see cref="WormholeKind.Data"/>. Null
    /// for Portal wormholes — the serializer drops null properties so a Portal record's JSON
    /// stays clean.</summary>
    public DataWormholeConfig? Data { get; set; }
    public PortalWormholeConfig? Portal { get; set; }
}

/// <summary>Top-level container of <c>wormholes.json</c>. Carries an explicit
/// <see cref="SchemaVersion"/> so future-format migrations (additive only, per the project
/// convention — see <c>Migration002</c>/<c>Migration003</c> in Storage) can detect older files
/// without parsing the body twice.</summary>
public sealed class WormholeStoreFile
{
    [JsonPropertyName("$schema_version")]
    public int SchemaVersion { get; set; } = 1;
    public List<WormholeRecord> Wormholes { get; set; } = new();
}
