using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace ShareQ.App.Services.Plugins;

/// <summary>
/// Isolated <see cref="AssemblyLoadContext"/> for a single plugin folder. Bundled dependencies of
/// the plugin are resolved relative to its own folder (via <see cref="AssemblyDependencyResolver"/>),
/// while shared contracts (<c>ShareQ.PluginContracts</c>) deliberately fall back to the default
/// load context — type identity must match between host and plugin or DI registration breaks.
/// </summary>
internal sealed class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;

    public PluginLoadContext(string pluginPath) : base(name: Path.GetFileNameWithoutExtension(pluginPath), isCollectible: false)
    {
        _resolver = new AssemblyDependencyResolver(pluginPath);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // Always share the contracts assembly with the host. Returning null here lets the default
        // ALC handle it, so `typeof(IUploader)` from the plugin == `typeof(IUploader)` from the host.
        if (assemblyName.Name == "ShareQ.PluginContracts") return null;

        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        return path is null ? null : LoadFromAssemblyPath(path);
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path is null ? IntPtr.Zero : LoadUnmanagedDllFromPath(path);
    }
}
