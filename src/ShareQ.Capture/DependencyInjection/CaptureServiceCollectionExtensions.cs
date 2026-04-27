using Microsoft.Extensions.DependencyInjection;

namespace ShareQ.Capture.DependencyInjection;

public static class CaptureServiceCollectionExtensions
{
    public static IServiceCollection AddShareQCapture(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<ICaptureSource, BitBltCaptureSource>();
        return services;
    }
}
