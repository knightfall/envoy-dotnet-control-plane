using System.Net;
using Envoy.ControlPlane.AspNetCore;
using Envoy.ControlPlane.Cache;
using Envoy.ControlPlane.Streams;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace Envoy.ControlPlane.EnvoyProbe;

internal sealed class ProbeHost(WebApplication application, int port) : IAsyncDisposable
{
    internal int Port { get; } = port;
    private bool disposed;

    internal static async Task<ProbeHost> ManagementAsync(SnapshotCache cache, ProbeCallbacks callbacks,
        bool ordered, int port, CancellationToken cancellationToken)
    {
        var builder = Builder(HttpProtocols.Http2, port);
        builder.Services.AddSingleton<IConfigWatcher>(cache);
        builder.Services.AddSingleton<ISotwCallbacks>(callbacks);
        builder.Services.AddSingleton(new SotwOptions { OrderedAds = ordered });
        builder.Services.AddEnvoyControlPlane();
        var application = builder.Build();
        application.MapEnvoyControlPlane();
        return await StartAsync(application, cancellationToken);
    }

    internal static async Task<ProbeHost> BackendAsync(string name, CancellationToken cancellationToken)
    {
        var application = Builder(HttpProtocols.Http1, 0).Build();
        application.Run(context => context.Response.WriteAsync(name + ":" + context.Request.Path, context.RequestAborted));
        return await StartAsync(application, cancellationToken);
    }

    internal static async Task<ProbeHost> ManagementAsync(IConfigWatcher cache, IDeltaCallbacks callbacks,
        int port, CancellationToken cancellationToken)
    {
        var builder = Builder(HttpProtocols.Http2, port);
        builder.Services.AddSingleton<IConfigWatcher>(cache);
        builder.Services.AddSingleton(callbacks);
        builder.Services.AddEnvoyControlPlane();
        var application = builder.Build();
        application.MapEnvoyControlPlane();
        return await StartAsync(application, cancellationToken);
    }

    private static WebApplicationBuilder Builder(HttpProtocols protocol, int port)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(server => server.Listen(IPAddress.Loopback, port, endpoint => endpoint.Protocols = protocol));
        builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(3));
        return builder;
    }

    private static async Task<ProbeHost> StartAsync(WebApplication application, CancellationToken cancellationToken)
    {
        await application.StartAsync(cancellationToken);
        var addresses = application.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!;
        return new ProbeHost(application, new Uri(addresses.Addresses.Single()).Port);
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await application.StopAsync(timeout.Token);
        await application.DisposeAsync();
    }
}
