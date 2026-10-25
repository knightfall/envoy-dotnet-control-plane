using Envoy.Config.Core.V3;

namespace Envoy.ControlPlane.Cache;

/// <summary>A synchronized live view of a node's indexed cache watches.</summary>
public interface IStatusInfo
{
    /// <summary>Gets the node retained when this status entry was created.</summary>
    Node? Node { get; }

    /// <summary>Gets the number of open SotW watches.</summary>
    int NumWatches { get; }

    /// <summary>Gets the number of open Delta watches.</summary>
    int NumDeltaWatches { get; }

    /// <summary>Gets the last SotW watch request time.</summary>
    DateTimeOffset LastWatchRequestTime { get; }

    /// <summary>Gets the last Delta request time, or its default before a Delta request.</summary>
    DateTimeOffset LastDeltaWatchRequestTime { get; }
}
