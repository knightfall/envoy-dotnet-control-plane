using System.Net;
using Envoy.Config.Cluster.V3;
using Envoy.Config.Core.V3;
using Envoy.Config.Endpoint.V3;
using Envoy.Config.Listener.V3;
using Envoy.Config.Route.V3;
using Envoy.ControlPlane.Resources;
using Envoy.Extensions.Filters.Http.Router.V3;
using Envoy.Extensions.Filters.Network.HttpConnectionManager.V3;
using Envoy.Extensions.Filters.Network.TcpProxy.V3;
using Envoy.Service.Runtime.V3;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Endpoint = Envoy.Config.Endpoint.V3.Endpoint;
using Filter = Envoy.Config.Listener.V3.Filter;
using HttpProtocolOptions = Envoy.Extensions.Upstreams.Http.V3.HttpProtocolOptions;
using SocketAddress = Envoy.Config.Core.V3.SocketAddress;

namespace Envoy.ControlPlane.Integration.Tests.Harness;

internal static class LiveDiscoveryResources
{
    internal const string ServiceA = "service-a";
    internal const string ServiceB = "service-b";
    internal const string ListenerA = "listener-a";
    internal const string RouteA = "route-a";
    internal const string RouteB = "route-b";
    internal const string Scope = "integration-scope";
    internal const string ScopeHeader = "x-route-scope";
    internal const string RuntimeLayer = "integration-runtime";
    internal const string RuntimeKey = "integration.rollout";
    internal const string ExtensionName = "integration-hcm";
    internal const int HttpPort = 10000;
    internal const int ScopedPort = 10001;
    internal const int TcpPort = 10002;
    internal const int ExtensionPort = 10003;
    internal static readonly string[] BasicTypes = [ResourceTypeUrls.Cluster, ResourceTypeUrls.Endpoint,
        ResourceTypeUrls.Listener, ResourceTypeUrls.Route];
    internal static readonly string[] RolloutTypes = [.. BasicTypes, ResourceTypeUrls.ScopedRoute,
        ResourceTypeUrls.Runtime, ResourceTypeUrls.ExtensionConfig];

    internal static Snapshot Single(string version, IPAddress backend, bool ads = true, bool delta = false) =>
        Single(version, backend, Source(ads, delta));

    internal static Snapshot Single(string version, IPAddress backend, ConfigSource source)
    {
        var snapshot = new Snapshot(version, new Dictionary<string, IEnumerable<IMessage?>>
        {
            [ResourceTypeUrls.Cluster] = [CreateCluster(ServiceA, source)],
            [ResourceTypeUrls.Endpoint] = [Assignment(ServiceA, backend)],
            [ResourceTypeUrls.Listener] = [CreateListener(ListenerA, HttpPort, HttpFilter(HttpManager(RouteA, source)))],
            [ResourceTypeUrls.Route] = [Routes(RouteA, ServiceA)],
        });
        snapshot.EnsureConsistent();
        return snapshot;
    }

    internal static Snapshot Rollout(string version, int runtimeValue, IPAddress backendA, IPAddress backendB, string rewrite = "")
    {
        var source = Source(ads: true);
        var scopedManager = HttpManager(RouteB, source);
        scopedManager.ScopedRoutes = new ScopedRoutes
        {
            Name = "integration-scopes",
            RdsConfigSource = source,
            ScopedRds = new ScopedRds { ScopedRdsConfigSource = source },
            ScopeKeyBuilder = new ScopedRoutes.Types.ScopeKeyBuilder
            {
                Fragments =
                {
                    new ScopedRoutes.Types.ScopeKeyBuilder.Types.FragmentBuilder
                    {
                        HeaderValueExtractor = new ScopedRoutes.Types.ScopeKeyBuilder.Types.FragmentBuilder.Types.HeaderValueExtractor
                        {
                            Name = ScopeHeader,
                            Index = 0,
                        },
                    },
                },
            },
        };
        var tcp = new Filter
        {
            Name = "envoy.filters.network.tcp_proxy",
            TypedConfig = Any.Pack(new TcpProxy { StatPrefix = "integration-tcp", Cluster = ServiceB }),
        };
        var extension = new Filter
        {
            Name = ExtensionName,
            ConfigDiscovery = new ExtensionConfigSource
            {
                ConfigSource = source,
                TypeUrls = { ResourceTypeUrls.Prefix + HttpConnectionManager.Descriptor.FullName },
            },
        };
        var extensionManager = HttpManager(RouteA, source);
        extensionManager.StatPrefix = "extension-" + version;
        var snapshot = new Snapshot(version, new Dictionary<string, IEnumerable<IMessage?>>
        {
            [ResourceTypeUrls.Cluster] = [CreateCluster(ServiceA, source), CreateCluster(ServiceB, source)],
            [ResourceTypeUrls.Endpoint] = [Assignment(ServiceA, backendA), Assignment(ServiceB, backendB)],
            [ResourceTypeUrls.Listener] =
            [
                CreateListener(ListenerA, HttpPort, HttpFilter(HttpManager(RouteA, source))),
                CreateListener("listener-scoped", ScopedPort, HttpFilter(scopedManager)),
                CreateListener("listener-tcp", TcpPort, tcp),
                CreateListener("listener-extension", ExtensionPort, extension),
            ],
            [ResourceTypeUrls.Route] = [Routes(RouteA, ServiceA, rewrite), Routes(RouteB, ServiceB, rewrite)],
            [ResourceTypeUrls.ScopedRoute] =
            [
                new ScopedRouteConfiguration
                {
                    Name = Scope,
                    RouteConfigurationName = RouteB,
                    Key = new ScopedRouteConfiguration.Types.Key
                    {
                        Fragments = { new ScopedRouteConfiguration.Types.Key.Types.Fragment { StringKey = Scope } },
                    },
                },
            ],
            [ResourceTypeUrls.Runtime] =
            [
                new Runtime { Name = RuntimeLayer, Layer = new Struct { Fields = { [RuntimeKey] = Value.ForNumber(runtimeValue) } } },
            ],
            [ResourceTypeUrls.ExtensionConfig] =
            [
                new TypedExtensionConfig { Name = ExtensionName, TypedConfig = Any.Pack(extensionManager) },
            ],
        });
        snapshot.EnsureConsistent();
        return snapshot;
    }

    internal static Snapshot Replace(Snapshot snapshot, string type, string version, params IMessage[] resources)
    {
        var groups = CopyGroups(snapshot);
        groups[type] = new ResourceCollection(version, resources.Select(resource => new ResourceEntry(resource)));
        return new Snapshot(groups);
    }

    internal static Snapshot WithSystemVersion(Snapshot snapshot, string version) => new(CopyGroups(snapshot, version));

    private static Dictionary<string, ResourceCollection> CopyGroups(Snapshot snapshot, string? systemVersion = null)
    {
        var groups = new Dictionary<string, ResourceCollection>(StringComparer.Ordinal);
        foreach (var responseType in System.Enum.GetValues<ResponseType>().Where(type => type != ResponseType.Unknown))
        {
            string type = ResourceCatalog.GetTypeUrl(responseType);
            if (snapshot.GetResourcesAndTtl(type) is { } entries)
            {
                groups.Add(type, new ResourceCollection(systemVersion ?? snapshot.GetVersion(type), entries.Values));
            }
        }
        return groups;
    }

    internal static ConfigSource Source(bool ads, bool delta = false)
    {
        var source = new ConfigSource { ResourceApiVersion = ApiVersion.V3 };
        if (ads)
        {
            source.Ads = new AggregatedConfigSource();
        }
        else
        {
            source.ApiConfigSource = new ApiConfigSource
            {
                ApiType = delta ? ApiConfigSource.Types.ApiType.DeltaGrpc : ApiConfigSource.Types.ApiType.Grpc,
                TransportApiVersion = ApiVersion.V3,
                SetNodeOnFirstMessageOnly = true,
                GrpcServices = { new GrpcService { EnvoyGrpc = new GrpcService.Types.EnvoyGrpc { ClusterName = "xds" } } },
            };
        }
        return source;
    }

    internal static Cluster CreateCluster(string name, ConfigSource source, bool useHttp2 = false, string? serviceName = null)
    {
        var cluster = new Cluster
        {
            Name = name,
            Type = Cluster.Types.DiscoveryType.Eds,
            ConnectTimeout = Duration.FromTimeSpan(TimeSpan.FromSeconds(1)),
            EdsClusterConfig = new Cluster.Types.EdsClusterConfig
            {
                EdsConfig = source,
                ServiceName = serviceName ?? name,
            },
        };
        if (useHttp2)
        {
            cluster.TypedExtensionProtocolOptions.Add(HttpProtocolOptions.Descriptor.FullName,
                Any.Pack(new HttpProtocolOptions
                {
                    ExplicitHttpConfig = new HttpProtocolOptions.Types.ExplicitHttpConfig
                    {
                        Http2ProtocolOptions = new Http2ProtocolOptions(),
                    },
                }));
        }
        return cluster;
    }

    internal static ClusterLoadAssignment Assignment(string name, IPAddress backend) => new()
    {
        ClusterName = name,
        Endpoints =
        {
            new LocalityLbEndpoints
            {
                LbEndpoints =
                {
                    new LbEndpoint { Endpoint = new Endpoint { Address = Socket(backend, EnvoyBackendContainer.Port) } },
                },
            },
        },
    };

    internal static RouteConfiguration Routes(string name, string cluster, string rewrite = "") => new()
    {
        Name = name,
        VirtualHosts =
        {
            new VirtualHost
            {
                Name = name,
                Domains = { "*" },
                Routes =
                {
                    new Route
                    {
                        Match = new RouteMatch { Prefix = "/" },
                        Route_ = new RouteAction { Cluster = cluster, PrefixRewrite = rewrite },
                    },
                },
            },
        },
    };

    internal static HttpConnectionManager HttpManager(string route, ConfigSource source) => new()
    {
        StatPrefix = route,
        Rds = new Rds { ConfigSource = source, RouteConfigName = route },
        HttpFilters =
        {
            new HttpFilter { Name = WellKnownFilterNames.Router, TypedConfig = Any.Pack(new Router()) },
        },
    };

    internal static Filter HttpFilter(HttpConnectionManager manager) =>
        new() { Name = WellKnownFilterNames.HttpConnectionManager, TypedConfig = Any.Pack(manager) };

    internal static Listener CreateListener(string name, int port, Filter filter) =>
        new() { Name = name, Address = Socket(IPAddress.Any, port), FilterChains = { new FilterChain { Filters = { filter } } } };

    private static Address Socket(IPAddress address, int port) =>
        new() { SocketAddress = new SocketAddress { Address = address.ToString(), PortValue = (uint)port } };
}
