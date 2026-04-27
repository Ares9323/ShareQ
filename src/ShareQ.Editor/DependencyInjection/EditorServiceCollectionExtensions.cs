using Microsoft.Extensions.DependencyInjection;
using ShareQ.Editor.ViewModels;
using ShareQ.Editor.Views;

namespace ShareQ.Editor.DependencyInjection;

public static class EditorServiceCollectionExtensions
{
    public static IServiceCollection AddShareQEditor(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddTransient<EditorViewModel>();
        services.AddTransient<EditorWindow>();
        return services;
    }
}
