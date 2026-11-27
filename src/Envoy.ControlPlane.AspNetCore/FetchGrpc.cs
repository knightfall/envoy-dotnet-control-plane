using Envoy.ControlPlane.Cache;
using Envoy.ControlPlane.Fetch;
using Envoy.Service.Discovery.V3;
using Grpc.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Envoy.ControlPlane.AspNetCore;

internal sealed class FetchGrpc(FetchServer server, IHostApplicationLifetime lifetime, ILogger<FetchGrpc> logger)
{
    internal async Task<DiscoveryResponse> RunAsync(DiscoveryRequest request, ServerCallContext context, string typeUrl)
    {
        if (request is null)
        {
            throw new RpcException(new Status(StatusCode.Unavailable, "Empty request."));
        }
        request.TypeUrl = typeUrl;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken, lifetime.ApplicationStopping);
        try
        {
            return await server.FetchAsync(request, new ResponseContext(stop.Token, context));
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            throw new RpcException(new Status(StatusCode.Cancelled, "Discovery fetch canceled."));
        }
        catch (Exception error) when (error is not (RpcException or OutOfMemoryException))
        {
            if (error is SkipFetchException or KeyNotFoundException)
            {
                logger.LogDebug(error, "Discovery fetch returned no response for type {TypeUrl}", typeUrl);
            }
            else
            {
                logger.LogError(error, "Discovery fetch failed for type {TypeUrl}", typeUrl);
            }
            // Keep the existing gRPC status for missing, unchanged, unsupported and other producer failures.
            throw new RpcException(new Status(StatusCode.Unknown, "Discovery fetch failed."));
        }
    }
}
