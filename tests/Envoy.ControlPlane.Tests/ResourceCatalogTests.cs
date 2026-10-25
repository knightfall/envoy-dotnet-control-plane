using Envoy.Config.Core.V3;
using Envoy.Config.Endpoint.V3;
using Envoy.Config.Listener.V3;
using Envoy.ControlPlane.Resources;
using Envoy.ControlPlane.Tests.Support;
using Google.Protobuf.WellKnownTypes;
using Xunit;

namespace Envoy.ControlPlane.Tests;

public sealed class ResourceCatalogTests
{
    [Fact]
    public void TypeMappings_FollowUpstreamOrder()
    {
        string[] names = ["envoy.config.cluster.v3.Cluster", "envoy.config.endpoint.v3.ClusterLoadAssignment",
            "envoy.config.endpoint.v3.LbEndpoint", "envoy.config.listener.v3.Listener", "envoy.config.route.v3.RouteConfiguration",
            "envoy.config.route.v3.ScopedRouteConfiguration", "envoy.config.route.v3.VirtualHost", "envoy.extensions.transport_sockets.tls.v3.Secret",
            "envoy.service.runtime.v3.Runtime", "envoy.config.core.v3.TypedExtensionConfig", "ratelimit.config.ratelimit.v3.RateLimitConfig",
            "envoy.config.listener.v3.FilterChain"];
        Assert.Equal(12, (int)ResponseType.Unknown);
        for (int index = 0; index < names.Length; index++)
        {
            string expected = "type.googleapis.com/" + names[index];
            Assert.Equal(expected, ResourceCatalog.GetTypeUrl((ResponseType)index));
            Assert.Equal((ResponseType)index, ResourceCatalog.GetResponseType(expected));
        }
    }

    [Fact]
    public void Names_EndpointAndGeneratedAndCustomMessages_MatchSource()
    {
        Assert.Equal("backend", ResourceCatalog.GetName(new ClusterLoadAssignment { ClusterName = "backend" }));
        Assert.Equal("listener", ResourceCatalog.GetName(new Listener { Name = "listener" }));
        Assert.Equal("custom", ResourceCatalog.GetName(new NamedResource("custom", "payload")));
        Assert.Equal(new[] { "backend", "listener", "listener", "" }, ResourceCatalog.GetNames([
            new(new ClusterLoadAssignment { ClusterName = "backend" }), new(new Listener { Name = "listener" }),
            new(new Listener { Name = "listener" }), new(null)]));
    }

    [Fact]
    public void UnknownNames_AndTypeUrls_HaveSourceOutcomes()
    {
        Assert.Equal("", ResourceCatalog.GetName(new Node { Id = "not-a-name" }));
        Assert.Equal("", ResourceCatalog.GetName(new StringValue { Value = "not-a-name" }));
        Assert.Equal("", ResourceCatalog.GetName(null));
        Assert.Equal(ResponseType.Unknown, ResourceCatalog.GetResponseType("custom/type"));
        Assert.Equal(ResponseType.Unknown, ResourceCatalog.GetResponseType(ResourceTypeUrls.ThriftRoute));
        Assert.Equal(ResponseType.Unknown, ResourceCatalog.GetResponseType(ResourceTypeUrls.Any));
        Assert.Throws<ArgumentOutOfRangeException>(() => ResourceCatalog.GetTypeUrl(ResponseType.Unknown));
    }

    [Theory]
    [InlineData("type.googleapis.com/envoy.config.cluster.v3.Cluster", true)]
    [InlineData("type.googleapis.com/envoy.config.listener.v3.Listener", true)]
    [InlineData("type.googleapis.com/envoy.config.endpoint.v3.ClusterLoadAssignment", false)]
    [InlineData("type.googleapis.com/envoy.config.route.v3.RouteConfiguration", false)]
    [InlineData("custom/type", false)]
    public void SotwFullState_IsLimitedToClustersAndListeners(string typeUrl, bool fullState) =>
        Assert.Equal(fullState, ResourceCatalog.RequiresFullStateInSotw(typeUrl));
}
