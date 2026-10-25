using Google.Protobuf;

namespace Envoy.ControlPlane.Resources;

/// <summary>A retained protobuf resource and its optional time-to-live.</summary>
/// <param name="Resource">The message, which callers must keep stable while published.</param>
/// <param name="Ttl">The optional TTL; absence differs from a zero TTL.</param>
public sealed record ResourceEntry(IMessage? Resource, TimeSpan? Ttl = null);
