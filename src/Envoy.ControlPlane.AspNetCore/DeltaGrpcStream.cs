using Envoy.ControlPlane.Cache;
using Envoy.ControlPlane.Streams;
using Envoy.Service.Discovery.V3;
using Grpc.Core;
using Microsoft.Extensions.Logging;

namespace Envoy.ControlPlane.AspNetCore;

internal sealed class DeltaGrpcStream(DeltaServer server, ILogger<DeltaGrpcStream> logger)
{
    internal async Task RunAsync(IAsyncStreamReader<DeltaDiscoveryRequest> requests,
        IServerStreamWriter<DeltaDiscoveryResponse> responses, ServerCallContext context, string typeUrl)
    {
        logger.LogDebug("Delta discovery stream opened for type {TypeUrl}", typeUrl);
        try
        {
            await server.StreamAsync(requests.ReadAllAsync(context.CancellationToken),
                (response, token) => new ValueTask(responses.WriteAsync(response, token)), typeUrl,
                new ResponseContext(context.CancellationToken, context));
        }
        catch (DeltaProtocolException error)
        {
            var code = error.Error switch
            {
                DeltaProtocolError.InvalidArgument => StatusCode.InvalidArgument,
                DeltaProtocolError.Unavailable => StatusCode.Unavailable,
                _ => StatusCode.Unknown,
            };
            logger.LogDebug("Delta discovery stream rejected with status {StatusCode}", code);
            throw new RpcException(new Status(code, error.Message));
        }
        finally
        {
            logger.LogDebug("Delta discovery stream closed for type {TypeUrl}", typeUrl);
        }
    }
}
