using Envoy.Config.Core.V3;

namespace Envoy.ControlPlane.Cache;

/// <summary>Maps a request node to its cache key.</summary>
public interface INodeHasher
{
    /// <summary>Returns the key for the optional request node.</summary>
    /// <param name="node">The retained node metadata.</param>
    string Hash(Node? node);
}
