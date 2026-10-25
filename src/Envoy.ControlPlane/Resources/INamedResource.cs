using Google.Protobuf;

namespace Envoy.ControlPlane.Resources;

/// <summary>Supplies a resource name for application messages without a generated string name field.</summary>
public interface INamedResource : IMessage
{
    /// <summary>Gets the resource name used for indexing.</summary>
    string Name { get; }
}
