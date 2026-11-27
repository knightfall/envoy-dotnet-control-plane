using System.Runtime.ExceptionServices;
using Envoy.ControlPlane.Cache;
using Envoy.ControlPlane.Streams;
using Envoy.Service.Discovery.V3;
using Grpc.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Envoy.ControlPlane.AspNetCore;

internal sealed class SotwGrpcStream(SotwServer server, IHostApplicationLifetime lifetime, ILogger<SotwGrpcStream> logger)
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
        catch (Exception error) when (error is not (RpcException or OutOfMemoryException))
        {
            var primary = GrpcStreamFailure.Unwrap(error, logger, "Discovery stream", typeUrl);
            if (primary is RpcException)
            {
                ExceptionDispatchInfo.Throw(primary);
            }
            if (primary is SotwProtocolException protocol)
            {
                var code = protocol.Error switch
                {
                    SotwProtocolError.InvalidArgument => StatusCode.InvalidArgument,
                    SotwProtocolError.Unavailable => StatusCode.Unavailable,
                    SotwProtocolError.ResourceExhausted => StatusCode.ResourceExhausted,
                    _ => StatusCode.Unknown,
                };
                logger.LogDebug("Discovery stream rejected with status {StatusCode}", code);
                throw new RpcException(new Status(code, protocol.Message));
            }
            if (primary is OperationCanceledException &&
                (context.CancellationToken.IsCancellationRequested || lifetime.ApplicationStopping.IsCancellationRequested))
            {
                throw new RpcException(new Status(StatusCode.Cancelled, "Discovery stream canceled."));
            }
            if (ReferenceEquals(primary, error))
            {
                logger.LogError(error, "Discovery stream failed for type {TypeUrl}", typeUrl);
            }
            throw new RpcException(new Status(StatusCode.Internal, "Discovery stream failed."));
        }
        finally
        {
            logger.LogDebug("Discovery stream closed for type {TypeUrl}", typeUrl);
        }
    }
}
