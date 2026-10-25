using Envoy.Config.Cluster.V3;
using Envoy.Config.Core.V3;
using Envoy.Config.Endpoint.V3;
using Envoy.Config.Listener.V3;
using Envoy.Config.Route.V3;
using Envoy.ControlPlane.Resources;
using Envoy.Extensions.Filters.Http.Router.V3;
using Envoy.Extensions.Filters.Network.HttpConnectionManager.V3;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using EnvoyEndpoint = Envoy.Config.Endpoint.V3.Endpoint;
using EnvoyRoute = Envoy.Config.Route.V3.Route;
using ListenerFilter = Envoy.Config.Listener.V3.Filter;

namespace Envoy.ControlPlane.Example;

internal static class ExampleResources
{
    internal static Snapshot CreateSnapshot(string version, uint backendPort, uint listenerPort)
    {
        var snapshot = new Snapshot(version, new Dictionary<string, IEnumerable<IMessage?>>
        {
            [ResourceTypeUrls.Cluster] = [CreateCluster()],
            [ResourceTypeUrls.Endpoint] = [CreateEndpoints(backendPort)],
            [ResourceTypeUrls.Route] = [CreateRoutes()],
            [ResourceTypeUrls.Listener] = [CreateListener(listenerPort)],
        });
        snapshot.EnsureConsistent();
        return snapshot;
    }

    private static ConfigSource AdsSource() => new()
    {
        Ads = new AggregatedConfigSource(),
        ResourceApiVersion = ApiVersion.V3,
    };

    private static Cluster CreateCluster() => new()
    {
        Name = "service",
        Type = Cluster.Types.DiscoveryType.Eds,
        ConnectTimeout = Duration.FromTimeSpan(TimeSpan.FromSeconds(1)),
        EdsClusterConfig = new Cluster.Types.EdsClusterConfig { EdsConfig = AdsSource(), ServiceName = "service" },
    };

    private static ClusterLoadAssignment CreateEndpoints(uint port) => new()
    {
        ClusterName = "service",
        Endpoints =
        {
            new LocalityLbEndpoints
            {
                LbEndpoints =
                {
                    new LbEndpoint
                    {
                        Endpoint = new EnvoyEndpoint
                        {
                            Address = new Address
                            {
                                SocketAddress = new SocketAddress { Address = "127.0.0.1", PortValue = port },
                            },
                        },
                    },
                },
            },
        },
    };

    private static RouteConfiguration CreateRoutes() => new()
    {
        Name = "routes",
        VirtualHosts =
        {
            new VirtualHost
            {
                Name = "backend",
                Domains = { "*" },
                Routes =
                {
                    new EnvoyRoute
                    {
                        Match = new RouteMatch { Prefix = "/" },
                        Route_ = new RouteAction { Cluster = "service" },
                    },
                },
            },
        },
    };

    private static Listener CreateListener(uint port)
    {
        var manager = new HttpConnectionManager
        {
            StatPrefix = "ingress",
            Rds = new Rds { ConfigSource = AdsSource(), RouteConfigName = "routes" },
            HttpFilters = { new HttpFilter { Name = WellKnownFilterNames.Router, TypedConfig = Any.Pack(new Router()) } },
        };
        return new Listener
        {
            Name = "listener",
            Address = new Address { SocketAddress = new SocketAddress { Address = "127.0.0.1", PortValue = port } },
            FilterChains =
            {
                new FilterChain
                {
                    Filters = { new ListenerFilter { Name = WellKnownFilterNames.HttpConnectionManager, TypedConfig = Any.Pack(manager) } },
                },
            },
        };
    }
}
