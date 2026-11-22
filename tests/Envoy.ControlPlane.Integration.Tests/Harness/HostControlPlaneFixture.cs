using System.Net;
using System.Security.Cryptography.X509Certificates;
using Envoy.ControlPlane.AspNetCore;
using Envoy.ControlPlane.Cache;
using Envoy.ControlPlane.Fetch;
using Envoy.ControlPlane.Streams;
using Google.Protobuf.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Envoy.ControlPlane.Integration.Tests.Harness;

internal sealed class HostControlPlaneFixture(IConfigWatcher cache, bool enableHttpGateway = false,
    X509Certificate2? certificate = null, Action<IServiceCollection>? configureServices = null,
    TypeRegistry? httpGatewayTypes = null, Action<WebApplication>? configureApplication = null,
    Action<HttpsConnectionAdapterOptions>? configureHttps = null, Action<RouteGroupBuilder>? configureDiscoveryEndpoints = null) : IAsyncDisposable
{
    private WebApplication? application;
    private bool disposed;

    internal int GrpcPort { get; private set; }
    internal int? HttpGatewayPort { get; private set; }
    internal ControlPlaneObservations Observations { get; } = new();

    internal async Task StartAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (application is not null)
        {
            throw new InvalidOperationException("The control-plane host is already running.");
        }
        ListenOptions? grpcListener = null;
        ListenOptions? httpListener = null;
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(server =>
        {
            server.Listen(IPAddress.Any, GrpcPort, endpoint =>
            {
                grpcListener = endpoint;
                endpoint.Protocols = HttpProtocols.Http2;
                if (certificate is not null)
                {
                    endpoint.UseHttps(options =>
                    {
                        options.ServerCertificate = certificate;
                        configureHttps?.Invoke(options);
                    });
                }
            });
            if (enableHttpGateway)
            {
                server.Listen(IPAddress.Any, HttpGatewayPort ?? 0, endpoint =>
                {
                    httpListener = endpoint;
                    endpoint.Protocols = HttpProtocols.Http1;
                });
            }
        });
        builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(5));
        builder.Services.AddSingleton(cache);
        builder.Services.AddSingleton(Observations);
        builder.Services.AddSingleton<ISotwCallbacks>(Observations);
        builder.Services.AddSingleton<IDeltaCallbacks>(Observations);
        builder.Services.AddSingleton<IFetchCallbacks>(Observations);
        configureServices?.Invoke(builder.Services);
        builder.Services.AddEnvoyControlPlane();
        var host = builder.Build();
        try
        {
            configureApplication?.Invoke(host);
            var endpoints = host.MapEnvoyControlPlane();
            configureDiscoveryEndpoints?.Invoke(endpoints);
            if (enableHttpGateway)
            {
                host.MapEnvoyControlPlaneHttpGateway(httpGatewayTypes);
            }
            await host.StartAsync(cancellationToken);
            GrpcPort = grpcListener!.IPEndPoint!.Port;
            HttpGatewayPort = httpListener?.IPEndPoint?.Port;
            application = host;
        }
        catch
        {
            await host.DisposeAsync();
            throw;
        }
    }

    internal async Task StopAsync()
    {
        if (application is not { } host)
        {
            return;
        }
        application = null;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            await host.StopAsync(timeout.Token);
        }
        finally
        {
            await host.DisposeAsync();
        }
    }

    internal async Task RestartAsync(CancellationToken cancellationToken)
    {
        await StopAsync();
        await StartAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        disposed = true;
        await StopAsync();
    }
}
