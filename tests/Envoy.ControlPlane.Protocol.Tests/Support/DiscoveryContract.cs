namespace Envoy.ControlPlane.Protocol.Tests;

internal readonly record struct DiscoveryContract(
    System.Type Service,
    System.Type Server,
    System.Type Client,
    string Name,
    string[] Methods);
