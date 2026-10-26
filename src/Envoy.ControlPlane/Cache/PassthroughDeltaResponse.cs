using Envoy.Service.Discovery.V3;

namespace Envoy.ControlPlane.Cache;

/// <summary>A caller-supplied Delta envelope and its complete next returned-resource metadata.</summary>
/// <remarks>Retain stable request, response and metadata values for the response lifetime.</remarks>
public sealed class PassthroughDeltaResponse : IDeltaResponse
{
    private readonly DeltaDiscoveryResponse response;

    /// <summary>Retains a ready Delta response without serializing or interpreting its payloads.</summary>
    /// <param name="request">The originating request.</param>
    /// <param name="response">The complete discovery envelope.</param>
    /// <param name="returnedResources">Complete next resource versions, rather than only changed resources.</param>
    /// <param name="context">The response creation context.</param>
    public PassthroughDeltaResponse(DeltaDiscoveryRequest request, DeltaDiscoveryResponse response,
        IReadOnlyDictionary<string, string> returnedResources, ResponseContext context = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(returnedResources);
        Request = request;
        this.response = response;
        ReturnedResources = returnedResources;
        Context = context;
    }

    /// <inheritdoc/>
    public DeltaDiscoveryRequest Request { get; }
    /// <inheritdoc/>
    public string ResponseVersion => response.SystemVersionInfo;
    /// <inheritdoc/>
    public IReadOnlyDictionary<string, string> ReturnedResources { get; }
    /// <inheritdoc/>
    public ResponseContext Context { get; }
    /// <inheritdoc/>
    public DeltaDiscoveryResponse GetDeltaDiscoveryResponse() => response;
}
