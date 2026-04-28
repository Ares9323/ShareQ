using System.Reflection;
using ShareQ.PluginContracts;

namespace ShareQ.App.Services.Plugins;

/// <summary>
/// One row of the plugin registry — covers both built-in plugins (loaded from compiled-in types)
/// and external plugins (loaded from a folder). The host uses this for the settings UI and to
/// filter active plugins by enabled state.
/// </summary>
public sealed record PluginDescriptor(
    string Id,
    string DisplayName,
    string Version,
    bool BuiltIn,
    /// <summary>Folder on disk for external plugins; null for built-in.</summary>
    string? FolderPath,
    /// <summary>The plugin's loaded assembly (null for built-in: types come from compiled-in deps).</summary>
    Assembly? Assembly,
    /// <summary>Manifest as published next to the DLL; synthesized for built-in plugins.</summary>
    PluginManifest Manifest);
