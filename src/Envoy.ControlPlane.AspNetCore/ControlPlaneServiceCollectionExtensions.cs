using Envoy.ControlPlane.Cache;
using Envoy.ControlPlane.Streams;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Envoy.ControlPlane.AspNetCore;

/// <summary>Registers the SotW engine and its ASP.NET Core transport.</summary>
public static class ControlPlaneServiceCollectionExtensions
{
    /// <summary>Adds hosting for an application-registered singleton cache and optional callbacks/options.</summary>
    /// <param name="services">The application service collection.</param>
    /// <returns>The service collection for further host configuration.</returns>
    public static IServiceCollection AddEnvoyControlPlane(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddGrpc();
        services.TryAddSingleton<SotwOptions>();
        services.TryAddSingleton(provider => new SotwServer(
            provider.GetRequiredService<IConfigWatcher>(),
            provider.GetService<ISotwCallbacks>(),
            provider.GetRequiredService<SotwOptions>(),
            provider.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping));
        services.TryAddSingleton<SotwGrpcStream>();
        return services;
    }
}
