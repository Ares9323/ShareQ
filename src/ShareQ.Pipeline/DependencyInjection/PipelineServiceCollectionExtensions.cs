using Microsoft.Extensions.DependencyInjection;
using ShareQ.Core.Pipeline;
using ShareQ.Pipeline.Profiles;
using ShareQ.Pipeline.Registry;
using ShareQ.Pipeline.Tasks;

namespace ShareQ.Pipeline.DependencyInjection;

public static class PipelineServiceCollectionExtensions
{
    public static IServiceCollection AddShareQPipeline(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IPipelineProfileStore, SqlitePipelineProfileStore>();
        services.AddSingleton<PipelineProfileSeeder>();

        // Each baked task is registered as IPipelineTask so the registry sees them.
        services.AddSingleton<IPipelineTask, AddToHistoryTask>();
        services.AddSingleton<IPipelineTask, SaveToFileTask>();

        services.AddSingleton<IPipelineTaskRegistry, PipelineTaskRegistry>();
        services.AddSingleton<PipelineExecutor>();

        return services;
    }
}
