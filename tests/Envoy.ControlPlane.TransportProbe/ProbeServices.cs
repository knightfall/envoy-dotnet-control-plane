using Envoy.Service.Discovery.V3;
using Envoy.Service.Endpoint.V3;
using Grpc.Core;

namespace Envoy.ControlPlane.TransportProbe;

sealed class AdsProbe(ProbeState state) : AggregatedDiscoveryService.AggregatedDiscoveryServiceBase
{
    public override async Task StreamAggregatedResources(IAsyncStreamReader<DiscoveryRequest> requestStream,
        IServerStreamWriter<DiscoveryResponse> responseStream, ServerCallContext context)
    {
        Interlocked.Increment(ref state.Active);
        try
        {
            await context.WriteResponseHeadersAsync(new Metadata { { "probe-header", "present" } });
            context.ResponseTrailers.Add("probe-trailer", "present");
            var response = state.Response(context);
            await foreach (var request in requestStream.ReadAllAsync(context.CancellationToken))
            {
                if (request.VersionInfo == "invalid")
                {
                    throw new RpcException(new Status(StatusCode.InvalidArgument, "probe rejection"));
                }
                if (request.VersionInfo == "hold")
                {
                    await Task.Delay(Timeout.Infinite, context.CancellationToken);
                }
                if (request.VersionInfo == "burst")
                {
                    Interlocked.Exchange(ref state.BurstStarted, 1);
                    for (int index = 0; index < 20; index++)
                    {
                        Interlocked.Increment(ref state.AttemptedWrites);
                        await responseStream.WriteAsync(response, context.CancellationToken);
                        Interlocked.Increment(ref state.CompletedWrites);
                    }
                }
                else
                {
                    await responseStream.WriteAsync(response, context.CancellationToken);
                }
            }
        }
        finally
        {
            Interlocked.Decrement(ref state.Active);
        }
    }
}

sealed class EndpointProbe(ProbeState state) : EndpointDiscoveryService.EndpointDiscoveryServiceBase
{
    public override Task<DiscoveryResponse> FetchEndpoints(DiscoveryRequest request, ServerCallContext context) => Task.FromResult(state.Response(context));
}
