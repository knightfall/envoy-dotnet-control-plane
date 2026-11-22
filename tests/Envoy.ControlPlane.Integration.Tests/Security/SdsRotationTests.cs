using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Envoy.Config.Core.V3;
using Envoy.ControlPlane.Integration.Tests.Harness;
using Envoy.ControlPlane.Resources;
using Envoy.Extensions.TransportSockets.Tls.V3;
using Google.Protobuf.WellKnownTypes;
using Xunit;
using static Envoy.ControlPlane.Integration.Tests.Harness.LiveDiscoveryResources;
using static Envoy.ControlPlane.Integration.Tests.Harness.LiveEnvoyScenario;

namespace Envoy.ControlPlane.Integration.Tests.Security;

public sealed class SdsRotationTests(ITestOutputHelper output)
{
    private const string CertificateSecretName = "downstream-listener";
    private const string RequestPath = "/sds";

    [Fact]
    public async Task Sds_RotatesTheLiveDownstreamCertificateWithoutRestartingEnvoy()
    {
        using var authority = new TestCertificateAuthority();
        using var first = authority.IssueServer("localhost", addLoopback: true);
        using var second = authority.IssueServer("localhost", addLoopback: true);
        Assert.NotEqual(first.Thumbprint, second.Thumbprint);
        Assert.NotEqual(first.SerialNumber, second.SerialNumber);
        await RunAsync(async scenario =>
        {
            await using var backend = scenario.Backend("A");
            await backend.StartAsync(scenario.Token);
            var source = Source(ads: true);
            var listener = CreateListener(ListenerA, HttpPort, HttpFilter(HttpManager(RouteA, source)));
            listener.FilterChains[0].TransportSocket = new TransportSocket
            {
                Name = "envoy.transport_sockets.tls",
                TypedConfig = Any.Pack(new DownstreamTlsContext
                {
                    CommonTlsContext = new CommonTlsContext
                    {
                        TlsCertificateSdsSecretConfigs =
                        {
                            new SdsSecretConfig { Name = CertificateSecretName, SdsConfig = source },
                        },
                    },
                }),
            };
            var graph = Replace(Single("initial", backend.Address), ResourceTypeUrls.Listener, "initial", listener);
            var initial = Replace(graph, ResourceTypeUrls.Secret, "cert-v1", CreateSecret(first));
            initial.EnsureConsistent();
            await scenario.PublishAsync(initial);

            await using var proxy = scenario.Proxy(scenario.AdsBootstrap(), HttpPort);
            await proxy.RunWithDiagnosticsAsync(output, async () =>
            {
                var wire = scenario.Host.Observations.Sotw;
                await scenario.WaitForAcksAsync(initial, [.. BasicTypes, ResourceTypeUrls.Secret]);
                long stream = Assert.Single(wire.Opened).StreamId;
                await VerifyLeafAsync("cert-v1", first, stream);
                int beforeRotation = wire.Responses.Count;

                var rotated = Replace(initial, ResourceTypeUrls.Secret, "cert-v2", CreateSecret(second));
                await scenario.PublishAsync(rotated);
                await scenario.WaitForAcksAsync(rotated, [ResourceTypeUrls.Secret]);
                await VerifyLeafAsync("cert-v2", second, stream);

                var sent = Assert.Single(wire.Responses.Skip(beforeRotation));
                Assert.Equal(stream, sent.StreamId);
                Assert.Equal(ResourceTypeUrls.Secret, sent.Response.TypeUrl);
                Assert.Equal("cert-v2", sent.Response.VersionInfo);
                Assert.Equal(CertificateSecretName, Assert.Single(sent.Response.Resources).Unpack<Secret>().Name);
                Assert.Equal(stream, Assert.Single(wire.Opened).StreamId);
                Assert.Empty(wire.Closed);
                Assert.All(wire.Requests, observed => Assert.Null(observed.Request.ErrorDetail));
                await proxy.StopAsync(scenario.Token);
                await scenario.UntilAsync(() => wire.Closed.Count == 1
                    && scenario.Cache.GetStatusInfo(scenario.NodeId) is { NumWatches: 0 }, "the SDS stream and watches to close");

                async Task VerifyLeafAsync(string version, X509Certificate2 expected, long streamId)
                {
                    Assert.Contains(wire.Requests, observed => observed.StreamId == streamId
                        && observed.Request.TypeUrl == ResourceTypeUrls.Secret
                        && observed.Request.ResourceNames.SequenceEqual([CertificateSecretName]));
                    var uri = new UriBuilder(proxy.ListenerAddress(HttpPort)) { Scheme = Uri.UriSchemeHttps, Path = RequestPath }.Uri;
                    await scenario.UntilAsync(async token =>
                    {
                        var served = await ReadHttpsAsync(uri, authority, token);
                        return served == new HttpsObservation(HttpStatusCode.OK, "A:/sds", expected.Thumbprint, expected.SerialNumber);
                    }, $"a validated HTTPS connection serving {version} and the backend's response");
                    var dump = await proxy.GetConfigDumpAsync(scenario.Token);
                    var active = Assert.Single(AdminEntries(dump, "SecretsConfigDump", "dynamic_active_secrets"));
                    Assert.Equal(CertificateSecretName, active.GetProperty("name").GetString());
                    Assert.Equal(version, active.GetProperty("version_info").GetString());
                }
            }, scenario.Token);
        }, nodeId: "sds-rotation");
    }

    private static Secret CreateSecret(X509Certificate2 certificate) => new()
    {
        Name = CertificateSecretName,
        TlsCertificate = new TlsCertificate
        {
            CertificateChain = new DataSource { InlineString = certificate.ExportCertificatePem() },
            PrivateKey = new DataSource { InlineString = TestCertificateAuthority.PrivateKeyPem(certificate) },
        },
    };

    private static async Task<HttpsObservation> ReadHttpsAsync(Uri uri, TestCertificateAuthority authority, CancellationToken cancellationToken)
    {
        string? thumbprint = null;
        string? serial = null;
        using var handler = new SocketsHttpHandler
        {
            SslOptions = new SslClientAuthenticationOptions
            {
                CertificateChainPolicy = authority.ServerTrustPolicy(),
                RemoteCertificateValidationCallback = (_, certificate, _, errors) =>
                {
                    if (certificate is null || errors != SslPolicyErrors.None)
                    {
                        return false;
                    }
                    thumbprint = certificate.GetCertHashString();
                    serial = certificate.GetSerialNumberString();
                    return true;
                },
            },
        };
        // A fresh handler owns each connection, so a pooled pre-rotation handshake cannot satisfy the check.
        using var client = new HttpClient(handler);
        using var response = await client.GetAsync(uri, cancellationToken);
        return new HttpsObservation(response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken), thumbprint, serial);
    }

    private sealed record HttpsObservation(HttpStatusCode Status, string Body, string? Thumbprint, string? Serial);
}
