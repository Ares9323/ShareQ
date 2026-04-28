using Microsoft.Extensions.DependencyInjection;
using ShareQ.Core.Pipeline;
using ShareQ.Plugins.Tasks;

namespace ShareQ.Plugins.DependencyInjection;

/// <summary>
/// Registers the host-side glue for the plugin system: HTTP factory shared with plugins, the
/// <c>UploadTask</c> pipeline step. Plugin implementations themselves (uploaders, ...) come from
/// either the bundled plugin projects (referenced by the App) or the runtime plugin folder
/// loaded by <c>PluginLoader</c>.
/// </summary>
public static class PluginsServiceCollectionExtensions
{
    public static IServiceCollection AddShareQPlugins(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHttpClient();
        services.AddSingleton<IPipelineTask, UploadTask>();

        return services;
    }
}
