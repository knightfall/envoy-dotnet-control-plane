using Xunit;

namespace Envoy.ControlPlane.Protocol.Tests;

/// <summary>
/// Preserves the original single-class suite's sequential execution while
/// organizing its checks into focused classes.
/// </summary>
[CollectionDefinition(Name)]
public sealed class ProtocolBindingCollection
{
    public const string Name = "Protocol bindings";
}
