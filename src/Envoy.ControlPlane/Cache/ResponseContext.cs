namespace Envoy.ControlPlane.Cache;

/// <summary>Retains cancellation and caller state associated with response creation.</summary>
/// <param name="CancellationToken">The publication or request cancellation token.</param>
/// <param name="State">Optional caller state forwarded to response callbacks.</param>
public readonly record struct ResponseContext(CancellationToken CancellationToken, object? State = null);
