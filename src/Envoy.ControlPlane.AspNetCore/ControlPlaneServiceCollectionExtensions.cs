using Envoy.ControlPlane.Cache;
using Envoy.ControlPlane.Fetch;
using Envoy.ControlPlane.Streams;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Envoy.ControlPlane.AspNetCore;

/// <summary>Registers the SotW/Delta engines and their ASP.NET Core transport.</summary>
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
        services.TryAddSingleton<DeltaOptions>();
        services.TryAddSingleton(provider => new SotwServer(
            provider.GetRequiredService<IConfigWatcher>(),
            provider.GetService<ISotwCallbacks>(),
            provider.GetRequiredService<SotwOptions>(),
            provider.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping));
        services.TryAddSingleton<SotwGrpcStream>();
        services.TryAddSingleton(provider => new DeltaServer(
            provider.GetRequiredService<IConfigWatcher>(),
            provider.GetService<IDeltaCallbacks>(),
            provider.GetRequiredService<DeltaOptions>(),
            provider.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping));
        services.TryAddSingleton<DeltaGrpcStream>();
        services.TryAddSingleton(provider => new FetchServer(
            provider.GetService<IConfigFetcher>() ?? provider.GetRequiredService<IConfigWatcher>() as IConfigFetcher ?? new UnsupportedFetcher(),
            provider.GetService<IFetchCallbacks>()));
        services.TryAddSingleton<FetchGrpc>();
        return services;
    }
}
