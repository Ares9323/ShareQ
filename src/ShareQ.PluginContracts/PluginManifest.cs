namespace ShareQ.PluginContracts;

/// <summary>
/// Metadata for an external plugin DLL. The host expects a <c>plugin.json</c> file next to the
/// DLL with these fields. Built-in plugins synthesize a manifest at registration time.
/// </summary>
public sealed record PluginManifest(
    string Id,
    string Version,
    string DisplayName,
    string DllName,
    /// <summary>SemVer of <c>ShareQ.PluginContracts</c> the plugin was built against.</summary>
    string ContractVersion,
    string? Description = null,
    string? Author = null,
    string? RepositoryUrl = null);
