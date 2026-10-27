using System.Collections.Concurrent;
using Envoy.Config.Core.V3;
using Envoy.ControlPlane.Cache;
using Envoy.ControlPlane.Streams;
using Envoy.Service.Discovery.V3;

namespace Envoy.ControlPlane.EnvoyProbe;

internal sealed class DeltaProbeCallbacks : IDeltaCallbacks
{
    private int connections;
    private int closes;
    internal int Connections => Volatile.Read(ref connections);
    internal int Closes => Volatile.Read(ref closes);
    internal ConcurrentQueue<DeltaRequestObservation> Requests { get; } = new();
    internal ConcurrentQueue<DeltaResponseObservation> History { get; } = new();
    internal ConcurrentDictionary<string, DeltaResponseObservation> Responses { get; } = new(StringComparer.Ordinal);
    internal Action<DeltaDiscoveryResponse>? ResponseObserved { get; set; }

    public ValueTask OnStreamOpenAsync(ResponseContext context, long streamId, string typeUrl, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref connections);
        return ValueTask.CompletedTask;
    }

    public ValueTask OnStreamRequestAsync(long streamId, DeltaDiscoveryRequest request, CancellationToken cancellationToken)
    {
        Requests.Enqueue(new DeltaRequestObservation(Connections, request.TypeUrl, request.Node?.Id ?? "",
            request.ResponseNonce, request.ErrorDetail?.Message,
            new Dictionary<string, string>(request.InitialResourceVersions, StringComparer.Ordinal),
            request.ResourceNamesSubscribe.ToArray(), request.ResourceNamesUnsubscribe.ToArray()));
        return ValueTask.CompletedTask;
    }

    public ValueTask OnStreamResponseAsync(long streamId, DeltaDiscoveryRequest request,
        DeltaDiscoveryResponse response, CancellationToken cancellationToken)
    {
        var observed = new DeltaResponseObservation(Connections, response.TypeUrl, response.SystemVersionInfo, response.Nonce,
            response.Resources.ToDictionary(resource => resource.Name, resource => resource.Version, StringComparer.Ordinal),
            response.RemovedResources.ToArray());
        History.Enqueue(observed);
        Responses[response.TypeUrl] = observed;
        ResponseObserved?.Invoke(response);
        return ValueTask.CompletedTask;
    }

    public ValueTask OnStreamClosedAsync(long streamId, Node node)
    {
        Interlocked.Increment(ref closes);
        return ValueTask.CompletedTask;
    }

    internal bool HasAcks(IReadOnlyDictionary<string, string> versions) => versions.All(pair =>
        Responses.TryGetValue(pair.Key, out var response) && response.Connection == Connections && response.Version == pair.Value
        && Requests.Any(request => request.Connection == response.Connection && request.Type == response.Type
            && request.Error is null && request.Nonce == response.Nonce));
}

internal sealed record DeltaRequestObservation(int Connection, string Type, string Node, string Nonce, string? Error,
    IReadOnlyDictionary<string, string> InitialVersions, string[] Added, string[] Removed);
internal sealed record DeltaResponseObservation(int Connection, string Type, string Version, string Nonce,
    IReadOnlyDictionary<string, string> Resources, string[] Removed);
