namespace Envoy.ControlPlane.Cache;

/// <summary>Signals that polling already has the current opaque snapshot version.</summary>
public sealed class SkipFetchException() : Exception("The requested version is already current.");
