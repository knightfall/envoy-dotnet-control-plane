using Envoy.ControlPlane.Cache;
using Envoy.ControlPlane.Fetch;
using Envoy.Service.Discovery.V3;
using Grpc.Core;
using Microsoft.Extensions.Hosting;

namespace Envoy.ControlPlane.AspNetCore;

internal sealed class FetchGrpc(FetchServer server, IHostApplicationLifetime lifetime)
{
    internal async Task<DiscoveryResponse> RunAsync(DiscoveryRequest request, ServerCallContext context, string typeUrl)
    {
        if (request is null)
        {
            throw new RpcException(new Status(StatusCode.Unavailable, "Empty request."));
        }
        request.TypeUrl = typeUrl;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken, lifetime.ApplicationStopping);
        return await server.FetchAsync(request, new ResponseContext(stop.Token, context));
    }
}
