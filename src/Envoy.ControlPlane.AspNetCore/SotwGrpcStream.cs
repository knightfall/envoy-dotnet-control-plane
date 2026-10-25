using Envoy.ControlPlane.Cache;
using Envoy.ControlPlane.Streams;
using Envoy.Service.Discovery.V3;
using Grpc.Core;
using Microsoft.Extensions.Logging;

namespace Envoy.ControlPlane.AspNetCore;

internal sealed class SotwGrpcStream(SotwServer server, ILogger<SotwGrpcStream> logger)
{
    internal async Task RunAsync(IAsyncStreamReader<DiscoveryRequest> requests,
        IServerStreamWriter<DiscoveryResponse> responses, ServerCallContext context, string typeUrl)
    {
        logger.LogDebug("Discovery stream opened for type {TypeUrl}", typeUrl);
        try
        {
            await server.StreamAsync(requests.ReadAllAsync(context.CancellationToken),
                (response, token) => new ValueTask(responses.WriteAsync(response, token)), typeUrl,
                new ResponseContext(context.CancellationToken, context));
        }
        catch (SotwProtocolException error)
        {
            var code = error.Error switch
            {
                SotwProtocolError.InvalidArgument => StatusCode.InvalidArgument,
                SotwProtocolError.Unavailable => StatusCode.Unavailable,
                _ => StatusCode.Unknown,
            };
            logger.LogDebug("Discovery stream rejected with status {StatusCode}", code);
            throw new RpcException(new Status(code, error.Message));
        }
        finally
        {
            logger.LogDebug("Discovery stream closed for type {TypeUrl}", typeUrl);
        }
    }
}
