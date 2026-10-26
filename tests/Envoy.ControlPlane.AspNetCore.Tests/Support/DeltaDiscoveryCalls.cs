using Envoy.Config.Core.V3;
using Envoy.ControlPlane.Resources;
using Envoy.Service.Discovery.V3;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Grpc.Core;
using Grpc.Net.Client;

namespace Envoy.ControlPlane.AspNetCore.Tests.Support;

internal static class DeltaDiscoveryCalls
{
    internal static AsyncDuplexStreamingCall<DeltaDiscoveryRequest, DeltaDiscoveryResponse> Open(GrpcChannel channel, string typeUrl,
        CancellationToken cancellationToken, Grpc.Core.Metadata? headers = null) => typeUrl switch
    {
        "" => new AggregatedDiscoveryService.AggregatedDiscoveryServiceClient(channel).DeltaAggregatedResources(headers, cancellationToken: cancellationToken),
        ResourceTypeUrls.Cluster => new Envoy.Service.Cluster.V3.ClusterDiscoveryService.ClusterDiscoveryServiceClient(channel).DeltaClusters(headers, cancellationToken: cancellationToken),
        ResourceTypeUrls.Endpoint => new Envoy.Service.Endpoint.V3.EndpointDiscoveryService.EndpointDiscoveryServiceClient(channel).DeltaEndpoints(headers, cancellationToken: cancellationToken),
        ResourceTypeUrls.Listener => new Envoy.Service.Listener.V3.ListenerDiscoveryService.ListenerDiscoveryServiceClient(channel).DeltaListeners(headers, cancellationToken: cancellationToken),
        ResourceTypeUrls.Route => new Envoy.Service.Route.V3.RouteDiscoveryService.RouteDiscoveryServiceClient(channel).DeltaRoutes(headers, cancellationToken: cancellationToken),
        ResourceTypeUrls.ScopedRoute => new Envoy.Service.Route.V3.ScopedRoutesDiscoveryService.ScopedRoutesDiscoveryServiceClient(channel).DeltaScopedRoutes(headers, cancellationToken: cancellationToken),
        ResourceTypeUrls.Secret => new Envoy.Service.Secret.V3.SecretDiscoveryService.SecretDiscoveryServiceClient(channel).DeltaSecrets(headers, cancellationToken: cancellationToken),
        ResourceTypeUrls.Runtime => new Envoy.Service.Runtime.V3.RuntimeDiscoveryService.RuntimeDiscoveryServiceClient(channel).DeltaRuntime(headers, cancellationToken: cancellationToken),
        ResourceTypeUrls.ExtensionConfig => new Envoy.Service.Extension.V3.ExtensionConfigDiscoveryService.ExtensionConfigDiscoveryServiceClient(channel).DeltaExtensionConfigs(headers, cancellationToken: cancellationToken),
        ResourceTypeUrls.VirtualHost => new Envoy.Service.Route.V3.VirtualHostDiscoveryService.VirtualHostDiscoveryServiceClient(channel).DeltaVirtualHosts(headers, cancellationToken: cancellationToken),
        ResourceTypeUrls.LbEndpoint => new Envoy.Service.Endpoint.V3.LocalityEndpointDiscoveryService.LocalityEndpointDiscoveryServiceClient(channel).DeltaLocalityEndpoints(headers, cancellationToken: cancellationToken),
        _ => throw new ArgumentOutOfRangeException(nameof(typeUrl)),
    };

    internal static DeltaDiscoveryRequest Request(string typeUrl = "", string nonce = "", string[]? additions = null,
        string[]? removals = null)
    {
        var request = new DeltaDiscoveryRequest { Node = new Node { Id = "node" }, TypeUrl = typeUrl, ResponseNonce = nonce };
        request.ResourceNamesSubscribe.Add(additions ?? []);
        request.ResourceNamesUnsubscribe.Add(removals ?? []);
        return request;
    }

    internal static Snapshot Snapshot(string typeUrl, string version, params IMessage[] messages) =>
        new(version, new Dictionary<string, IEnumerable<IMessage?>> { [typeUrl] = messages });

    internal static IMessage Message(string typeUrl, string name) => typeUrl switch
    {
        ResourceTypeUrls.VirtualHost => new Envoy.Config.Route.V3.VirtualHost { Name = name },
        ResourceTypeUrls.LbEndpoint => new NamedEndpoint(name, new Envoy.Config.Endpoint.V3.LbEndpoint
        {
            Endpoint = new Envoy.Config.Endpoint.V3.Endpoint { Hostname = name },
        }),
        _ => DiscoveryCalls.Message(typeUrl, name),
    };

    /// <summary>Supplies the LEDS envelope name while serializing the generated LbEndpoint unchanged.</summary>
    private sealed class NamedEndpoint(string name, IMessage payload) : INamedResource
    {
        public string Name => name;
        public MessageDescriptor Descriptor => payload.Descriptor;
        public int CalculateSize() => payload.CalculateSize();
        public void MergeFrom(CodedInputStream input) => payload.MergeFrom(input);
        public void WriteTo(CodedOutputStream output) => payload.WriteTo(output);
    }
}
