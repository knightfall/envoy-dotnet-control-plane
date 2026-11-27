// Copyright 2020 Envoyproxy Authors. Licensed under Apache-2.0.
using System.Threading.Channels;
using Envoy.ControlPlane.Cache;
using Envoy.ControlPlane.Resources;
using Envoy.Service.Discovery.V3;

namespace Envoy.ControlPlane.Streams;

internal sealed partial class SotwSession
{
    private Channel<ISotwResponse>? orderedResponses;
    private Task<bool>? responseReady;

    private void InitializeOrderedResponses(string typeUrl)
    {
        if (!options.OrderedAds || typeUrl.Length > 0)
        {
            return;
        }
        orderedResponses = CreateResponseChannel((int)ResponseType.Unknown);
    }

    private static Channel<ISotwResponse> CreateResponseChannel(int capacity) =>
        Channel.CreateBounded<ISotwResponse>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
            FullMode = BoundedChannelFullMode.Wait,
        });

    private async ValueTask DrainOtherResponsesAsync(string currentType,
        Func<DiscoveryResponse, CancellationToken, ValueTask> send, CancellationToken cancellationToken)
    {
        if (orderedResponses is null)
        {
            return;
        }
        while (orderedResponses.Reader.TryRead(out var response))
        {
            var request = response?.Request ?? throw new InvalidOperationException("Missing request in queued response");
            if (request.TypeUrl != currentType)
            {
                await SendAsync(response!, send, cancellationToken);
            }
        }
    }
}
