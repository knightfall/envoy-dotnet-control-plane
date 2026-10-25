using Envoy.Config.Cluster.V3;
using Envoy.Config.Listener.V3;
using Envoy.ControlPlane.Resources;
using Envoy.Extensions.Filters.Network.HttpConnectionManager.V3;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Google.Protobuf.WellKnownTypes;
using ListenerFilter = Envoy.Config.Listener.V3.Filter;

namespace Envoy.ControlPlane.Tests.Support;

internal static class ResourceFixtures
{
    internal static Snapshot Snapshot(string version, params (string TypeUrl, IMessage[] Messages)[] groups) =>
        new(version, groups.ToDictionary(group => group.TypeUrl, group => (IEnumerable<IMessage?>)group.Messages, StringComparer.Ordinal));

    internal static Cluster EdsCluster(string name, string serviceName = "") => new()
    {
        Name = name,
        Type = Cluster.Types.DiscoveryType.Eds,
        EdsClusterConfig = new Cluster.Types.EdsClusterConfig { ServiceName = serviceName },
    };

    internal static FilterChain RdsChain(string route) => new()
    {
        Filters = { HcmFilter(new HttpConnectionManager { Rds = new Rds { RouteConfigName = route } }) },
    };

    internal static ListenerFilter HcmFilter(HttpConnectionManager manager) => new() { TypedConfig = Any.Pack(manager) };
}

internal sealed class NamedResource(string name, IMessage payload) : INamedResource
{
    internal NamedResource(string name, string value) : this(name, new StringValue { Value = value }) { }
    public string Name { get; } = name;
    public MessageDescriptor Descriptor => payload.Descriptor;
    public int CalculateSize() => payload.CalculateSize();
    public void MergeFrom(CodedInputStream input) => payload.MergeFrom(input);
    public void WriteTo(CodedOutputStream output) => payload.WriteTo(output);
}

internal sealed class CountingResource(string name) : INamedResource
{
    private readonly StringValue payload = new() { Value = name };
    public string Name { get; } = name;
    public int Writes { get; private set; }
    public MessageDescriptor Descriptor => StringValue.Descriptor;
    public int CalculateSize() => payload.CalculateSize();
    public void MergeFrom(CodedInputStream input) => payload.MergeFrom(input);
    public void WriteTo(CodedOutputStream output)
    {
        Writes++;
        payload.WriteTo(output);
    }
}
