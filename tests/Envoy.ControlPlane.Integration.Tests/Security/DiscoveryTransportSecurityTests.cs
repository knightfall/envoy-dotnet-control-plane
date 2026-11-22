using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Authentication;
using System.Security.Claims;
using Envoy.ControlPlane.Cache;
using Envoy.ControlPlane.Integration.Tests.Harness;
using Envoy.ControlPlane.Streams;
using Grpc.Core;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;
using static Envoy.ControlPlane.Integration.Tests.Harness.LiveDiscoveryResources;
using static Envoy.ControlPlane.Integration.Tests.Harness.LiveEnvoyScenario;

namespace Envoy.ControlPlane.Integration.Tests.Security;

public sealed class DiscoveryTransportSecurityTests(ITestOutputHelper output)
{
    private const string BearerScheme = "Bearer";
    private const string Policy = "xds";
    private const string NodeA = "node-a";
    private const string NodeB = "node-b";
    private const string DiscoveryPath = "/envoy.service.discovery.v3.AggregatedDiscoveryService";
    private const string ProbePath = "/secure";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TlsAds_EnforcesBearerPolicyAndBindsTheAuthenticatedNode(bool delta)
    {
        using var authority = new TestCertificateAuthority();
        using var server = authority.IssueServer(EnvoyProxyContainer.ManagementHostname);
        var completed = new ConcurrentQueue<ProtectedCall>();
        string? token = null;
        NodeAuthorizationCallbacks? authorization = null;
        await RunAsync(async scenario =>
        {
            await using var backend = scenario.Backend("A");
            await backend.StartAsync(scenario.Token);
            var snapshot = Single("authorized", backend.Address);
            await scenario.PublishAsync(snapshot);
            await scenario.Cache.SetSnapshotAsync(NodeB, Single("protected-b", backend.Address), new ResponseContext(scenario.Token));
            var wire = scenario.Host.Observations;

            await using (var anonymous = CreateProxy(NodeA))
            {
                await anonymous.RunWithDiagnosticsAsync(output, async () =>
                {
                    await scenario.UntilAsync(() => completed.Any(call => call.HttpStatus == StatusCodes.Status401Unauthorized),
                        "the ASP.NET Core bearer challenge before gRPC dispatch");
                    await WaitForGrpcRejectionAsync(scenario, anonymous, StatusCode.Unauthenticated);
                    Assert.Empty(wire.Sotw.Opened);
                    Assert.Empty(wire.Delta.Opened);
                    Assert.Empty(scenario.Cache.GetStatusKeys());
                    await AssertNoActiveResourcesAsync(anonymous, scenario.Token);
                    await anonymous.StopAsync(scenario.Token);
                }, scenario.Token);
            }

            await using (var mismatchedNode = CreateProxy(NodeB, token))
            {
                await mismatchedNode.RunWithDiagnosticsAsync(output, async () =>
                {
                    await scenario.UntilAsync(() => completed.Any(call => call.NodeClaim == NodeA
                        && call.GrpcStatus == ((int)StatusCode.PermissionDenied).ToString(CultureInfo.InvariantCulture)),
                        "the authenticated permitted-policy call to receive node-binding PermissionDenied");
                    await WaitForGrpcRejectionAsync(scenario, mismatchedNode, StatusCode.PermissionDenied);
                    if (delta)
                    {
                        Assert.Contains(wire.Delta.Requests, observed => observed.Request.Node?.Id == NodeB);
                    }
                    else
                    {
                        Assert.Contains(wire.Sotw.Requests, observed => observed.Request.Node?.Id == NodeB);
                    }
                    Assert.Empty(wire.Sotw.Responses);
                    Assert.Empty(wire.Delta.Responses);
                    Assert.Empty(scenario.Cache.GetStatusKeys());
                    await AssertNoActiveResourcesAsync(mismatchedNode, scenario.Token);
                    await mismatchedNode.StopAsync(scenario.Token);
                    await scenario.UntilAsync(() => authorization!.ActiveBindings == 0, "the rejected node binding to be cleaned up");
                }, scenario.Token);
            }

            await using var accepted = CreateProxy(NodeA, token);
            await accepted.RunWithDiagnosticsAsync(output, async () =>
            {
                if (delta)
                {
                    await Delta.DeltaAssertions.WaitForAcksAsync(scenario, "authorized", BasicTypes);
                }
                else
                {
                    await scenario.WaitForAcksAsync(snapshot, BasicTypes);
                }
                await scenario.ExpectTrafficAsync(accepted, HttpPort, "A:/secure", path: ProbePath);
                Assert.Equal(1, authorization!.ActiveBindings);
                Assert.Equal([NodeA], scenario.Cache.GetStatusKeys());
                Assert.All(completed, call =>
                {
                    Assert.True(call.Https);
                    Assert.Equal("HTTP/2", call.Protocol);
                });
                await accepted.StopAsync(scenario.Token);
                await scenario.UntilAsync(() => authorization.ActiveBindings == 0
                    && scenario.Cache.GetStatusInfo(NodeA) is { NumWatches: 0, NumDeltaWatches: 0 },
                    "authorized stream and node-binding cleanup");
            }, scenario.Token);

            EnvoyProxyContainer CreateProxy(string nodeId, string? bearerToken = null) =>
                scenario.Proxy(EnvoyProxyContainer.RenderTlsAdsBootstrap(nodeId, scenario.Host.GrpcPort, delta, bearerToken: bearerToken),
                    authority.ManagementFiles(), bearerToken, HttpPort);
        }, nodeId: NodeA, createHost: cache => new HostControlPlaneFixture(cache, certificate: server,
            configureServices: services =>
            {
                services.AddDataProtection().UseEphemeralDataProtectionProvider();
                services.AddAuthentication(BearerScheme).AddBearerToken(BearerScheme);
                services.AddAuthorization(options => options.AddPolicy(Policy,
                    policy => policy.RequireAuthenticatedUser().RequireClaim("permission", Policy)));
                services.AddSingleton(provider => authorization = new NodeAuthorizationCallbacks(
                    provider.GetRequiredService<ControlPlaneObservations>()));
                services.AddSingleton<ISotwCallbacks>(provider => provider.GetRequiredService<NodeAuthorizationCallbacks>());
                services.AddSingleton<IDeltaCallbacks>(provider => provider.GetRequiredService<NodeAuthorizationCallbacks>());
            }, configureApplication: app =>
            {
                token = IssueBearerToken(app.Services, NodeA);
                app.Use(next => async context =>
                {
                    await next(context);
                    if (context.Request.Path.StartsWithSegments(DiscoveryPath))
                    {
                        var grpcStatus = context.Response.Headers["grpc-status"].ToString();
                        if (grpcStatus.Length == 0 && context.Features.Get<IHttpResponseTrailersFeature>() is { } trailers)
                        {
                            grpcStatus = trailers.Trailers["grpc-status"].ToString();
                        }
                        completed.Enqueue(new ProtectedCall(context.Request.IsHttps, context.Request.Protocol,
                            context.Response.StatusCode, grpcStatus, context.User.FindFirstValue(NodeAuthorizationCallbacks.NodeClaim)));
                    }
                });
                app.UseAuthentication();
                app.UseAuthorization();
            }, configureDiscoveryEndpoints: endpoints => endpoints.RequireAuthorization(Policy)));
    }

    [Fact]
    public async Task MutualTlsAds_RequiresAClientCertificateTrustedByKestrel()
    {
        using var authority = new TestCertificateAuthority();
        using var untrustedAuthority = new TestCertificateAuthority();
        using var server = authority.IssueServer(EnvoyProxyContainer.ManagementHostname);
        using var client = authority.IssueClient("integration-envoy");
        using var untrustedClient = untrustedAuthority.IssueClient("untrusted-envoy");
        var admitted = new ConcurrentQueue<(bool Https, string Protocol, string? Thumbprint)>();
        var handshakeFailures = new TlsHandshakeFailures();
        var untrustedThumbprints = new ConcurrentQueue<string>();
        await RunAsync(async scenario =>
        {
            await using var backend = scenario.Backend("A");
            await backend.StartAsync(scenario.Token);
            var snapshot = Single("m-tls", backend.Address);
            await scenario.PublishAsync(snapshot);
            var wire = scenario.Host.Observations.Sotw;

            foreach (var rejectedCertificate in new[] { null, untrustedClient })
            {
                int failuresBefore = handshakeFailures.Count;
                var bootstrap = EnvoyProxyContainer.RenderTlsAdsBootstrap(scenario.NodeId, scenario.Host.GrpcPort,
                    delta: false, sendClientCertificate: rejectedCertificate is not null);
                await using var rejected = scenario.Proxy(bootstrap, authority.ManagementFiles(rejectedCertificate), sensitiveValue: null, HttpPort);
                await rejected.RunWithDiagnosticsAsync(output, async () =>
                {
                    await scenario.UntilAsync(() => handshakeFailures.Count > failuresBefore, "Kestrel to reject the TLS handshake");
                    if (rejectedCertificate is not null)
                    {
                        await scenario.UntilAsync(() => untrustedThumbprints.Contains(rejectedCertificate.Thumbprint),
                            "Kestrel to reject the presented certificate issued by the untrusted CA");
                    }
                    await WaitForGrpcRejectionAsync(scenario, rejected, StatusCode.Unavailable);
                    Assert.Empty(admitted);
                    Assert.Empty(wire.Opened);
                    Assert.Empty(wire.Responses);
                    await AssertNoActiveResourcesAsync(rejected, scenario.Token);
                    await rejected.StopAsync(scenario.Token);
                }, scenario.Token);
            }

            var allowed = EnvoyProxyContainer.RenderTlsAdsBootstrap(scenario.NodeId, scenario.Host.GrpcPort,
                delta: false, sendClientCertificate: true);
            await using var accepted = scenario.Proxy(allowed, authority.ManagementFiles(client), sensitiveValue: null, HttpPort);
            await accepted.RunWithDiagnosticsAsync(output, async () =>
            {
                await scenario.WaitForAcksAsync(snapshot, BasicTypes);
                await scenario.ExpectTrafficAsync(accepted, HttpPort, "A:/secure", path: ProbePath);
                Assert.Equal((true, "HTTP/2", client.Thumbprint), Assert.Single(admitted));
                Assert.Single(wire.Opened);
                await accepted.StopAsync(scenario.Token);
                await scenario.UntilAsync(() => wire.Closed.Count == 1
                    && scenario.Cache.GetStatusInfo(scenario.NodeId) is { NumWatches: 0 }, "the mTLS stream and its watches to close");
            }, scenario.Token);
        }, nodeId: "mtls-envoy", createHost: cache => new HostControlPlaneFixture(cache, certificate: server,
            configureServices: services => services.AddLogging(logging => logging.AddProvider(handshakeFailures)
                .AddFilter<TlsHandshakeFailures>("Microsoft.AspNetCore.Server.Kestrel.Https", LogLevel.Debug)),
            configureApplication: app => app.Use(next => context =>
            {
                if (context.Request.Path.StartsWithSegments(DiscoveryPath))
                {
                    admitted.Enqueue((context.Request.IsHttps, context.Request.Protocol, context.Connection.ClientCertificate?.Thumbprint));
                }
                return next(context);
            }), configureHttps: options =>
            {
                options.ClientCertificateMode = ClientCertificateMode.RequireCertificate;
                options.ClientCertificateValidation = (certificate, _, _) =>
                {
                    bool trusted = authority.TrustsClient(certificate);
                    if (!trusted)
                    {
                        untrustedThumbprints.Enqueue(certificate.Thumbprint);
                    }
                    return trusted;
                };
            }));
    }

    private static string IssueBearerToken(IServiceProvider services, string nodeId)
    {
        var options = services.GetRequiredService<IOptionsMonitor<BearerTokenOptions>>().Get(BearerScheme);
        var identity = new ClaimsIdentity([new Claim("permission", Policy), new Claim(NodeAuthorizationCallbacks.NodeClaim, nodeId)], BearerScheme);
        var properties = new AuthenticationProperties { ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(10) };
        return options.BearerTokenProtector.Protect(new AuthenticationTicket(new ClaimsPrincipal(identity), properties, BearerScheme));
    }

    private static async Task AssertNoActiveResourcesAsync(EnvoyProxyContainer proxy, CancellationToken cancellationToken)
    {
        var dump = await proxy.GetConfigDumpAsync(cancellationToken);
        Assert.Empty(AdminEntries(dump, "ClustersConfigDump", "dynamic_active_clusters"));
        Assert.Empty(AdminEntries(dump, "ClustersConfigDump", "dynamic_warming_clusters"));
        Assert.Empty(AdminEntries(dump, "ListenersConfigDump", "dynamic_listeners"));
        Assert.Empty(AdminEntries(dump, "RoutesConfigDump", "dynamic_route_configs"));
    }

    private static Task WaitForGrpcRejectionAsync(LiveEnvoyScenario scenario, EnvoyProxyContainer proxy, StatusCode status)
    {
        string result = ": " + ((int)status).ToString(CultureInfo.InvariantCulture) + ",";
        return scenario.UntilAsync(async token =>
        {
            string log = await proxy.GetErrorLogAsync(token);
            return log.Split('\n').Any(line => line.Contains("gRPC config stream", StringComparison.Ordinal) && line.Contains(result, StringComparison.Ordinal));
        }, $"Envoy to log gRPC status {status}");
    }

    private sealed class TlsHandshakeFailures : ILoggerProvider
    {
        private int count;
        internal int Count => Volatile.Read(ref count);

        public ILogger CreateLogger(string categoryName) => new HandshakeLogger(this,
            categoryName == "Microsoft.AspNetCore.Server.Kestrel.Https.Internal.HttpsConnectionMiddleware");

        public void Dispose()
        {
        }

        private sealed class HandshakeLogger(TlsHandshakeFailures owner, bool enabled) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => enabled && logLevel != LogLevel.None;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (enabled && exception is AuthenticationException)
                {
                    Interlocked.Increment(ref owner.count);
                }
            }
        }
    }

    private sealed record ProtectedCall(bool Https, string Protocol, int HttpStatus, string GrpcStatus, string? NodeClaim);
}
