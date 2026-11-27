using Microsoft.Extensions.Logging;

namespace Envoy.ControlPlane.AspNetCore;

internal static class GrpcStreamFailure
{
    internal static Exception Unwrap(Exception error, ILogger logger, string streamName, string typeUrl)
    {
        if (error is not AggregateException { InnerException: { } primary })
        {
            return error;
        }

        logger.LogError(error, "{StreamName} failed for type {TypeUrl}", streamName, typeUrl);
        // Core records the primary failure before cleanup failures. Only that branch determines the status.
        while (primary is AggregateException { InnerException: { } inner })
        {
            primary = inner;
        }
        return primary;
    }
}
