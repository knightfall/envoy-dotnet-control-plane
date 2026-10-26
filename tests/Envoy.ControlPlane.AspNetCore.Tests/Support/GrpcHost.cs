using System.Net;
using Envoy.ControlPlane.Cache;
using Envoy.ControlPlane.Streams;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Envoy.ControlPlane.AspNetCore.Tests.Support;

internal sealed class GrpcHost(WebApplication application, GrpcChannel channel, ObservedCache cache, HostCallbacks callbacks)
    : IAsyncDisposable
{
    internal static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    internal WebApplication Application { get; } = application;
    internal GrpcChannel Channel { get; } = channel;
    internal ObservedCache Cache { get; } = cache;
    internal HostCallbacks Callbacks { get; } = callbacks;
    private bool disposed;

    internal static async Task<GrpcHost> StartAsync(SotwOptions? options = null, bool authorize = false,
        Action<ObservedCache, HostCallbacks>? configure = null, DeltaOptions? deltaOptions = null)
    {
        var cache = new ObservedCache();
        var callbacks = new HostCallbacks();
        configure?.Invoke(cache, callbacks);
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(server => server.Listen(IPAddress.Loopback, 0,
            endpoint => endpoint.Protocols = HttpProtocols.Http2));
        builder.Services.AddSingleton<IConfigWatcher>(cache);
        builder.Services.AddSingleton<ISotwCallbacks>(callbacks);
        builder.Services.AddSingleton<IDeltaCallbacks>(callbacks);
        if (options is not null)
        {
            builder.Services.AddSingleton(options);
        }
        if (deltaOptions is not null)
        {
            builder.Services.AddSingleton(deltaOptions);
        }
        builder.Services.AddEnvoyControlPlane();
        builder.Services.Configure<HostOptions>(host => host.ShutdownTimeout = TimeSpan.FromSeconds(2));
        if (authorize)
        {
            builder.Services.AddAuthentication("test").AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("test", _ => { });
            builder.Services.AddAuthorization(authorization => authorization.AddPolicy("xds",
                policy => policy.RequireAuthenticatedUser().RequireClaim("xds-node", "node")));
        }
        var application = builder.Build();
        if (authorize)
        {
            application.UseAuthentication();
            application.UseAuthorization();
        }
        var endpoints = application.MapEnvoyControlPlane();
        if (authorize)
        {
            endpoints.RequireAuthorization("xds");
        }
        await application.StartAsync(TestContext.Current.CancellationToken);
        var addresses = application.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!;
        return new GrpcHost(application, GrpcChannel.ForAddress(addresses.Addresses.Single()), cache, callbacks);
    }

    internal static Task Wait(Task task) => task.WaitAsync(Timeout, TestContext.Current.CancellationToken);
    internal static Task<T> Wait<T>(Task<T> task) => task.WaitAsync(Timeout, TestContext.Current.CancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        Channel.Dispose();
        using var cancellation = new CancellationTokenSource(Timeout);
        await Application.StopAsync(cancellation.Token);
        await Application.DisposeAsync();
    }
}
