using Envoy.Config.Core.V3;

namespace Envoy.ControlPlane.Cache;

/// <summary>Uses the node ID, with an empty key for an absent node.</summary>
public sealed class IdNodeHasher : INodeHasher
{
    /// <inheritdoc/>
    public string Hash(Node? node) => node?.Id ?? "";
}
