using System.Collections.Concurrent;
using Envoy.Config.Core.V3;
using Envoy.ControlPlane.Cache;
using Envoy.ControlPlane.Streams;
using Envoy.Service.Discovery.V3;

namespace Envoy.ControlPlane.EnvoyProbe;

internal sealed class ProbeCallbacks : ISotwCallbacks
{
    private int connections;
    private int closes;
    private readonly TaskCompletionSource releaseNack = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal int Connections => Volatile.Read(ref connections);
    internal int Closes => Volatile.Read(ref closes);
    internal ConcurrentQueue<RequestObservation> Requests { get; } = new();
    internal ConcurrentDictionary<string, ResponseObservation> Responses { get; } = new(StringComparer.Ordinal);
    internal TaskCompletionSource<NackObservation> Nack { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ValueTask OnStreamOpenAsync(ResponseContext context, long streamId, string typeUrl, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref connections);
        return ValueTask.CompletedTask;
    }

    public async ValueTask OnStreamRequestAsync(long streamId, DiscoveryRequest request, CancellationToken cancellationToken)
    {
        Requests.Enqueue(new RequestObservation(Connections, request.TypeUrl, request.Node.Id,
            request.VersionInfo, request.ResponseNonce, request.ErrorDetail?.Message));
        if (request.ErrorDetail is not null)
        {
            Nack.TrySetResult(new NackObservation(request.TypeUrl, request.VersionInfo, request.ResponseNonce,
                request.ErrorDetail.Message, Responses[request.TypeUrl]));
            await releaseNack.Task.WaitAsync(cancellationToken);
        }
    }

    public ValueTask OnStreamResponseAsync(ResponseContext context, long streamId, DiscoveryRequest request,
        DiscoveryResponse response, CancellationToken cancellationToken)
    {
        Responses[response.TypeUrl] = new ResponseObservation(Connections, response.TypeUrl, response.VersionInfo, response.Nonce);
        return ValueTask.CompletedTask;
    }

    public ValueTask OnStreamClosedAsync(long streamId, Node node)
    {
        Interlocked.Increment(ref closes);
        return ValueTask.CompletedTask;
    }

    internal void ReleaseNack() => releaseNack.TrySetResult();

    internal bool HasAcks(IReadOnlyDictionary<string, string> versions) => versions.All(pair =>
        Responses.TryGetValue(pair.Key, out var response) && response.Connection == Connections && response.Version == pair.Value
        && Requests.Any(request => request.Connection == response.Connection && request.Type == response.Type
            && request.Error is null && request.Version == response.Version && request.Nonce == response.Nonce));
}

internal sealed record RequestObservation(int Connection, string Type, string Node, string Version, string Nonce, string? Error);
internal sealed record ResponseObservation(int Connection, string Type, string Version, string Nonce);
internal sealed record NackObservation(string Type, string AcceptedVersion, string Nonce, string Error, ResponseObservation Response);
