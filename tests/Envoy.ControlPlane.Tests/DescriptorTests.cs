using Envoy.ControlPlane.Resources;
using Envoy.ControlPlane.Tests.Support;
using Envoy.Extensions.Filters.Network.TcpProxy.V3;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Google.Protobuf.WellKnownTypes;
using Xunit;

namespace Envoy.ControlPlane.Tests;

public sealed class DescriptorTests
{
    [Fact]
    public void ApplicationTypeRegistry_RoundTripsCustomAnyWithoutGlobalRegistration()
    {
        var message = new TcpProxy { StatPrefix = "application-extension" };
        var registry = TypeRegistry.FromMessages(TcpProxy.Descriptor);
        var formatter = new JsonFormatter(JsonFormatter.Settings.Default.WithTypeRegistry(registry));
        var parser = new JsonParser(JsonParser.Settings.Default.WithTypeRegistry(registry));
        var packed = Any.Pack(message);
        string json = formatter.Format(packed);
        var roundTrip = parser.Parse<Any>(json).Unpack<TcpProxy>();
        Assert.Equal(message, roundTrip);
        Assert.Same(TcpProxy.Descriptor, registry.Find(TcpProxy.Descriptor.FullName));
        Assert.Null(TypeRegistry.Empty.Find(TcpProxy.Descriptor.FullName));
        Assert.Equal(packed.ToByteArray(), ResourceSerialization.Serialize(Any.Pack(roundTrip)));
    }

    [Fact]
    public void CustomSnapshot_ExposesArbitraryTypeThroughInterface()
    {
        var message = new NamedResource("extension", new TcpProxy { StatPrefix = "custom" });
        string typeUrl = ResourceTypeUrls.Prefix + TcpProxy.Descriptor.FullName;
        Assert.Equal(ResponseType.Unknown, ResourceCatalog.GetResponseType(typeUrl));
        IResourceSnapshot snapshot = new ApplicationSnapshot(typeUrl, message);
        Assert.Equal("application-version", snapshot.GetVersion(typeUrl));
        Assert.Same(message, snapshot.GetResources(typeUrl)!["extension"]);
        var wire = ResourceSerialization.Serialize(snapshot.GetResources(typeUrl)!["extension"]);
        Assert.Equal("custom", TcpProxy.Parser.ParseFrom(wire).StatPrefix);
        snapshot.ConstructVersionMap();
        Assert.Equal(ResourceSerialization.ComputeHash(wire), snapshot.GetVersionMap(typeUrl)!["extension"]);
    }

    private sealed class ApplicationSnapshot(string resourceTypeUrl, NamedResource message) : IResourceSnapshot
    {
        private readonly ResourceCollection collection = new("application-version", new ResourceEntry[] { new(message) });
        private Dictionary<string, string>? versions;
        public string GetVersion(string typeUrl) => typeUrl == resourceTypeUrl ? collection.Version : "";
        public IReadOnlyDictionary<string, ResourceEntry>? GetResourcesAndTtl(string typeUrl) => typeUrl == resourceTypeUrl ? collection.Items : null;
        public IReadOnlyDictionary<string, IMessage?>? GetResources(string typeUrl) =>
            GetResourcesAndTtl(typeUrl)?.ToDictionary(pair => pair.Key, pair => pair.Value.Resource, StringComparer.Ordinal);
        public void ConstructVersionMap() => versions ??= collection.Items.ToDictionary(pair => pair.Key,
            pair => ResourceSerialization.ComputeHash(ResourceSerialization.Serialize(pair.Value.Resource)), StringComparer.Ordinal);
        public IReadOnlyDictionary<string, string>? GetVersionMap(string typeUrl) => typeUrl == resourceTypeUrl ? versions : null;
    }
}
