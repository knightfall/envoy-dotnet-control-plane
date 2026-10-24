using Envoy.Config.Core.V3;
using Envoy.Config.Listener.V3;
using Envoy.Extensions.Filters.Http.Router.V3;
using Envoy.Extensions.Filters.Network.HttpConnectionManager.V3;
using Envoy.Extensions.TransportSockets.Tls.V3;
using Envoy.Service.Discovery.V3;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Google.Protobuf.WellKnownTypes;
using Ratelimit.Config.Ratelimit.V3;
using Xunit;
using Filter = Envoy.Config.Listener.V3.Filter;
using ExternalPayload = Google.Protobuf.WellKnownTypes.Type;

namespace Envoy.ControlPlane.Protocol.Tests;

[Collection(ProtocolBindingCollection.Name)]
public sealed class SerializationTests
{
    [Fact]
    public void CommonGraph_ListenerHcmRouterTlsAndRatelimit_RoundTripsBinaryAnyAndJson()
    {
        var hcm = new HttpConnectionManager { StatPrefix = "ingress", Rds = new Rds { RouteConfigName = "routes" } };
        hcm.HttpFilters.Add(new HttpFilter { Name = "envoy.filters.http.router", TypedConfig = Any.Pack(new Router { SuppressEnvoyHeaders = true }) });
        var tls = new DownstreamTlsContext { CommonTlsContext = new CommonTlsContext() };
        tls.CommonTlsContext.TlsCertificates.Add(new TlsCertificate
        {
            CertificateChain = new DataSource { InlineString = "fixture certificate" },
            PrivateKey = new DataSource { InlineString = "fixture key" },
        });
        var listener = new Listener { Name = "edge", Address = new Address { SocketAddress = new SocketAddress { Address = "0.0.0.0", PortValue = 8443 } } };
        var chain = new FilterChain { TransportSocket = new TransportSocket { Name = "envoy.transport_sockets.tls", TypedConfig = Any.Pack(tls) } };
        chain.Filters.Add(new Filter { Name = "envoy.filters.network.http_connection_manager", TypedConfig = Any.Pack(hcm) });
        listener.FilterChains.Add(chain);
        var response = new DiscoveryResponse { VersionInfo = "v1", Nonce = "n1", TypeUrl = "type.googleapis.com/envoy.config.listener.v3.Listener" };
        response.Resources.Add(Any.Pack(listener));

        var registry = TypeRegistry.FromFiles(ProtocolCatalog.Files.Value);
        var formatter = new JsonFormatter(new JsonFormatter.Settings(false).WithTypeRegistry(registry));
        var parser = new JsonParser(JsonParser.Settings.Default.WithTypeRegistry(registry));
        Assert.Equal(response, DiscoveryResponse.Parser.ParseFrom(response.ToByteArray()));
        string json = formatter.Format(response);
        Assert.Contains("type.googleapis.com/envoy.extensions.filters.http.router.v3.Router", json, StringComparison.Ordinal);
        var decoded = parser.Parse<DiscoveryResponse>(json).Resources[0].Unpack<Listener>();
        Assert.Equal(listener, decoded);
        Assert.Equal(8443U, decoded.Address.SocketAddress.PortValue);
        Assert.Equal("ingress", decoded.FilterChains[0].Filters[0].TypedConfig.Unpack<HttpConnectionManager>().StatPrefix);
        Assert.Equal("fixture key", decoded.FilterChains[0].TransportSocket.TypedConfig.Unpack<DownstreamTlsContext>().CommonTlsContext.TlsCertificates[0].PrivateKey.InlineString);

        var config = new RateLimitConfig { Name = "api-limit", Domain = "api" };
        config.Descriptors.Add(new RateLimitDescriptor { Key = "user", Value = "alice", QuotaMode = true, Metadata = new Struct() });
        config.Descriptors[0].Metadata.Fields.Add("owner", Value.ForString("application"));
        var packed = Any.Pack(config);
        Assert.Equal("type.googleapis.com/ratelimit.config.ratelimit.v3.RateLimitConfig", packed.TypeUrl);
        Assert.Equal(config, Any.Parser.ParseFrom(packed.ToByteArray()).Unpack<RateLimitConfig>());
        Assert.Equal(config, parser.Parse<Any>(formatter.Format(packed)).Unpack<RateLimitConfig>());
        Assert.Equal(8, RateLimitDescriptor.Descriptor.FindFieldByName("metadata").FieldNumber);
        Assert.Equal("ratelimit/config/ratelimit/v3/rls_conf.proto", RateLimitConfig.Descriptor.File.Name);
    }

    [Fact]
    public void ExternalAny_UpstreamPayload_RequiresRegistrationAndRoundTrips()
    {
        // type.proto is an upstream protobuf descriptor outside our selected
        // catalog. It exercises external registration without a custom schema.
        var payload = new ExternalPayload { Name = "application policy", Oneofs = { "alpha", "beta" } };
        var packed = Any.Pack(payload);
        Assert.Equal(payload, Any.Parser.ParseFrom(packed.ToByteArray()).Unpack<ExternalPayload>());
        var standard = new JsonFormatter(new JsonFormatter.Settings(false).WithTypeRegistry(TypeRegistry.FromFiles(ProtocolCatalog.Files.Value)));
        Assert.Throws<InvalidOperationException>(() => standard.Format(packed));
        var registry = TypeRegistry.FromFiles(ProtocolCatalog.Files.Value.Append(ExternalPayload.Descriptor.File).ToArray());
        var formatter = new JsonFormatter(new JsonFormatter.Settings(false).WithTypeRegistry(registry));
        var parser = new JsonParser(JsonParser.Settings.Default.WithTypeRegistry(registry));
        Assert.Equal(payload, parser.Parse<Any>(formatter.Format(packed)).Unpack<ExternalPayload>());
        Assert.Throws<InvalidProtocolBufferException>(() => packed.Unpack<Listener>());
    }

    [Fact]
    public void Binary_UnknownFields_ArePreserved()
    {
        var listener = new Listener { Name = "edge" };
        byte[] bytes = [.. listener.ToByteArray(), 0xc0, 0x3e, 0x2a]; // field 1000, varint 42
        var parsed = Listener.Parser.ParseFrom(bytes);
        Assert.Equal("edge", parsed.Name);
        Assert.Equal(bytes, parsed.Clone().ToByteArray());
    }
}
