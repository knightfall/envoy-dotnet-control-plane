using System.Runtime.ExceptionServices;
using Envoy.ControlPlane.Cache;
using Envoy.ControlPlane.Streams;
using Envoy.Service.Discovery.V3;
using Grpc.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Envoy.ControlPlane.AspNetCore;

internal sealed class DeltaGrpcStream(DeltaServer server, IHostApplicationLifetime lifetime, ILogger<DeltaGrpcStream> logger)
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
        catch (Exception error) when (error is not (RpcException or OutOfMemoryException))
        {
            var primary = GrpcStreamFailure.Unwrap(error, logger, "Delta discovery stream", typeUrl);
            if (primary is RpcException)
            {
                ExceptionDispatchInfo.Throw(primary);
            }
            if (primary is DeltaProtocolException protocol)
            {
                var code = protocol.Error switch
                {
                    DeltaProtocolError.InvalidArgument => StatusCode.InvalidArgument,
                    DeltaProtocolError.Unavailable => StatusCode.Unavailable,
                    DeltaProtocolError.ResourceExhausted => StatusCode.ResourceExhausted,
                    _ => StatusCode.Unknown,
                };
                logger.LogDebug("Delta discovery stream rejected with status {StatusCode}", code);
                throw new RpcException(new Status(code, protocol.Message));
            }
            if (primary is OperationCanceledException &&
                (context.CancellationToken.IsCancellationRequested || lifetime.ApplicationStopping.IsCancellationRequested))
            {
                throw new RpcException(new Status(StatusCode.Cancelled, "Delta discovery stream canceled."));
            }
            if (ReferenceEquals(primary, error))
            {
                logger.LogError(error, "Delta discovery stream failed for type {TypeUrl}", typeUrl);
            }
            throw new RpcException(new Status(StatusCode.Internal, "Delta discovery stream failed."));
        }
        finally
        {
            logger.LogDebug("Delta discovery stream closed for type {TypeUrl}", typeUrl);
        }
    }
}
