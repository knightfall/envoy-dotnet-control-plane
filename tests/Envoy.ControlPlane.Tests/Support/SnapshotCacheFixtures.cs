using Envoy.Config.Core.V3;
using Envoy.ControlPlane.Cache;
using Envoy.ControlPlane.Resources;
using Envoy.Service.Discovery.V3;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Google.Protobuf.WellKnownTypes;

namespace Envoy.ControlPlane.Tests.Support;

internal static class SnapshotCacheFixtures
{
    internal static DiscoveryRequest Request(string typeUrl, string version = "", string nodeId = "node", params string[] names)
    {
        var request = new DiscoveryRequest { TypeUrl = typeUrl, VersionInfo = version, Node = new Node { Id = nodeId } };
        request.ResourceNames.Add(names);
        return request;
    }

    internal static TestSubscription Subscription(IEnumerable<string>? names = null, bool wildcard = false,
        IReadOnlyDictionary<string, string>? returned = null, IEnumerable<string>? prefixes = null) => new(
            returned ?? new Dictionary<string, string>(), new HashSet<string>(names ?? [], StringComparer.Ordinal),
            new HashSet<string>(prefixes ?? [], StringComparer.Ordinal), wildcard);
}

internal sealed class FixedNodeHasher(string key) : INodeHasher
{
    public string Hash(Node? node) => key;
}

internal sealed class TestTimeProvider(DateTimeOffset current) : TimeProvider
{
    public DateTimeOffset Current { get; set; } = current;
    public int Reads { get; private set; }
    public override DateTimeOffset GetUtcNow()
    {
        Reads++;
        return Current;
    }
}

internal sealed class ObservedSnapshot(IResourceSnapshot inner, Action beforeRead) : IResourceSnapshot
{
    public string GetVersion(string typeUrl) => inner.GetVersion(typeUrl);
    public IReadOnlyDictionary<string, ResourceEntry>? GetResourcesAndTtl(string typeUrl)
    {
        beforeRead();
        return inner.GetResourcesAndTtl(typeUrl);
    }
    public IReadOnlyDictionary<string, IMessage?>? GetResources(string typeUrl) => inner.GetResources(typeUrl);
    public void ConstructVersionMap() => inner.ConstructVersionMap();
    public IReadOnlyDictionary<string, string>? GetVersionMap(string typeUrl) => inner.GetVersionMap(typeUrl);
}

internal sealed class SingleResourceSnapshot(string resourceTypeUrl, ResourceEntry entry) : IResourceSnapshot
{
    private readonly ResourceCollection collection = new("application/v1", new[] { entry });
    public string GetVersion(string typeUrl) => typeUrl == resourceTypeUrl ? collection.Version : "";
    public IReadOnlyDictionary<string, ResourceEntry>? GetResourcesAndTtl(string typeUrl) => typeUrl == resourceTypeUrl ? collection.Items : null;
    public IReadOnlyDictionary<string, IMessage?>? GetResources(string typeUrl) =>
        GetResourcesAndTtl(typeUrl)?.ToDictionary(pair => pair.Key, pair => pair.Value.Resource, StringComparer.Ordinal);
    public void ConstructVersionMap() { }
    public IReadOnlyDictionary<string, string>? GetVersionMap(string typeUrl) => null;
}

internal sealed class ThrowingResource(Exception failure) : INamedResource
{
    public string Name => "broken";
    public MessageDescriptor Descriptor => StringValue.Descriptor;
    public int Attempts { get; private set; }
    public int CalculateSize()
    {
        Attempts++;
        throw failure;
    }
    public void WriteTo(CodedOutputStream output) => throw failure;
    public void MergeFrom(CodedInputStream input) => throw failure;
}
