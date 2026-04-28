using System.IO;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ShareQ.PluginContracts;

namespace ShareQ.App.Services.Plugins;

/// <summary>
/// Discovers external plugin folders, validates their <c>plugin.json</c>, loads each in an
/// isolated <see cref="PluginLoadContext"/>, and registers exported types into the DI container.
/// Failures (missing manifest, bad DLL, contract mismatch) are caught per-plugin so one broken
/// plugin doesn't stop the rest from loading.
/// </summary>
public sealed class PluginLoader
{
    private static readonly Type[] ContractTypes =
    [
        typeof(IUploader),
    ];

    public static string DefaultPluginsRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ShareQ", "plugins");

    public IReadOnlyList<PluginDescriptor> LoadFromFolder(string root, IServiceCollection services, Action<string, Exception>? onError = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (!Directory.Exists(root)) return [];

        var loaded = new List<PluginDescriptor>();
        foreach (var dir in Directory.EnumerateDirectories(root))
        {
            var manifestPath = Path.Combine(dir, "plugin.json");
            if (!File.Exists(manifestPath)) continue;
            try
            {
                var manifest = ReadManifest(manifestPath);
                var dllPath = Path.Combine(dir, manifest.DllName);
                if (!File.Exists(dllPath))
                    throw new FileNotFoundException($"plugin DLL '{manifest.DllName}' missing", dllPath);

                var alc = new PluginLoadContext(dllPath);
                var asm = alc.LoadFromAssemblyPath(dllPath);
                RegisterContractTypes(services, asm);
                loaded.Add(new PluginDescriptor(
                    manifest.Id, manifest.DisplayName, manifest.Version,
                    BuiltIn: false, FolderPath: dir, Assembly: asm, Manifest: manifest));
            }
            catch (Exception ex)
            {
                onError?.Invoke(dir, ex);
            }
        }
        return loaded;
    }

    private static void RegisterContractTypes(IServiceCollection services, Assembly assembly)
    {
        foreach (var type in SafeGetTypes(assembly))
        {
            if (type.IsAbstract || type.IsInterface || !type.IsClass) continue;
            foreach (var contract in ContractTypes)
            {
                if (contract.IsAssignableFrom(type))
                    services.AddSingleton(contract, type);
            }
        }
    }

    private static IEnumerable<Type> SafeGetTypes(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.OfType<Type>(); }
    }

    private static PluginManifest ReadManifest(string path)
    {
        using var stream = File.OpenRead(path);
        var manifest = JsonSerializer.Deserialize<PluginManifest>(stream, ManifestSerializerOptions)
            ?? throw new InvalidDataException("manifest deserialized to null");
        if (string.IsNullOrEmpty(manifest.Id)) throw new InvalidDataException("manifest.id is required");
        if (string.IsNullOrEmpty(manifest.DllName)) throw new InvalidDataException("manifest.dllName is required");
        return manifest;
    }

    private static readonly JsonSerializerOptions ManifestSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };
}
