using Envoy.Config.Cluster.V3;
using Envoy.Config.Core.V3;
using Envoy.Config.Endpoint.V3;
using Envoy.Config.Listener.V3;
using Envoy.ControlPlane.Resources;
using Envoy.ControlPlane.Tests.Support;
using Envoy.Extensions.Filters.Network.HttpConnectionManager.V3;
using Envoy.Service.Discovery.V3;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Google.Protobuf.WellKnownTypes;
using Xunit;
using static Envoy.ControlPlane.Example.ExampleResources;

namespace Envoy.ControlPlane.Tests;

public sealed class MessageConversionsTests
{
    [Fact]
    public void SourceCases_DeclareExpectedOutcomesAndExportTheCompleteSymbolInventory()
    {
        var inputs = ConversionCorpus.Cases();
        Assert.Equal(17, inputs.Length);
        Assert.Equal(17, inputs.Select(input => input.Id).Distinct(StringComparer.Ordinal).Count());
        var symbols = HelperSymbols.All();
        Assert.Equal(74, symbols.Length);
        Assert.Equal(74, symbols.Select(symbol => symbol.Source).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(48, typeof(WellKnownNames).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static).Length);
        Assert.Equal(WellKnownNames.Router, WellKnownFilterNames.Router);
        Assert.Equal(WellKnownNames.HttpConnectionManager, WellKnownFilterNames.HttpConnectionManager);
        ConversionCorpus.Write(inputs.Select(ConversionCorpus.Evaluate));
    }

    [Fact]
    public void RequestRoundTrip_UsesProtoNamesAndKeepsOriginalMessagesIndependent()
    {
        var original = new DiscoveryRequest { VersionInfo = "test", Node = new Node { Id = "proxy" } };
        var value = MessageConversions.ToStruct(original);
        Assert.Equal("test", value.Fields["version_info"].StringValue);
        Assert.Equal("proxy", value.Fields["node"].StructValue.Fields["id"].StringValue);
        Assert.False(value.Fields.ContainsKey("versionInfo"));
        var typed = MessageConversions.ToMessage<DiscoveryRequest>(value);
        var described = Assert.IsType<DiscoveryRequest>(MessageConversions.ToMessage(value, DiscoveryRequest.Descriptor));
        Assert.Equal(original, typed);
        Assert.Equal(original, described);
        Assert.NotSame(original, typed);
        Assert.NotSame(typed, described);
        typed.Node.Id = "changed";
        Assert.Equal("proxy", original.Node.Id);
        Assert.Equal("proxy", value.Fields["node"].StructValue.Fields["id"].StringValue);
    }

    [Fact]
    public void DefaultsAndPresence_RetainTheProtobufJsonContract()
    {
        Assert.Empty(MessageConversions.ToStruct(new DiscoveryRequest()).Fields);
        var node = new Node { UserAgentVersion = "" };
        var restored = MessageConversions.ToMessage<Node>(MessageConversions.ToStruct(node));
        Assert.Equal(Node.UserAgentVersionTypeOneofCase.UserAgentVersion, restored.UserAgentVersionTypeCase);
        Assert.Empty(restored.UserAgentVersion);
        var cluster = new Cluster { Type = Cluster.Types.DiscoveryType.StrictDns };
        Assert.Equal("STRICT_DNS", MessageConversions.ToStruct(cluster).Fields["type"].StringValue);
        var stats = new ClusterStats { TotalDroppedRequests = ulong.MaxValue };
        var value = MessageConversions.ToStruct(stats);
        Assert.Equal("18446744073709551615", value.Fields["total_dropped_requests"].StringValue);
        Assert.Equal(stats, MessageConversions.ToMessage<ClusterStats>(value));
    }

    [Fact]
    public void ExistingExampleGraph_RoundTripsRegisteredHcmAndRouterAny()
    {
        var graph = CreateSnapshot("example", 8080, 10000);
        var listener = Assert.IsType<Listener>(Assert.Single(graph.GetResources(ResourceTypeUrls.Listener)!).Value);
        var registry = TypeRegistry.FromMessages(Listener.Descriptor, HttpConnectionManager.Descriptor,
            Envoy.Extensions.Filters.Http.Router.V3.Router.Descriptor);
        var value = MessageConversions.ToStruct(listener, registry);
        var restored = MessageConversions.ToMessage<Listener>(value, registry);
        Assert.Equal(listener, restored);
        Assert.Equal(ResourceReferences.GetReferences([new ResourceEntry(listener)])[ResourceTypeUrls.Route],
            ResourceReferences.GetReferences([new ResourceEntry(restored)])[ResourceTypeUrls.Route]);
        Assert.Throws<InvalidOperationException>(() => MessageConversions.ToStruct(listener));
        Assert.Throws<InvalidOperationException>(() => MessageConversions.ToMessage<Listener>(value));
    }

    [Fact]
    public void UnknownMalformedAny_AndInvalidObjectFieldsAreRejected()
    {
        var registry = TypeRegistry.FromMessages(Cluster.Descriptor);
        var unknown = new Any { TypeUrl = "type.googleapis.com/unknown.Type" };
        Assert.Throws<InvalidOperationException>(() => MessageConversions.ToStruct(unknown, registry));
        var malformed = new Any { TypeUrl = ResourceTypeUrls.Cluster, Value = ByteString.CopyFrom([0xff]) };
        Assert.Throws<InvalidProtocolBufferException>(() => MessageConversions.ToStruct(malformed, registry));
        Assert.Throws<InvalidProtocolBufferException>(() => MessageConversions.ToMessage<DiscoveryRequest>(Struct.Parser.ParseJson("""{"unknown":true}""")));
        Assert.Throws<InvalidProtocolBufferException>(() => MessageConversions.ToMessage<DiscoveryRequest>(Struct.Parser.ParseJson("""{"version_info":42}""")));
    }

    [Fact]
    public void NullArgumentsAndDescriptorsWithoutClrParsers_HaveExplicitFailures()
    {
        Assert.Throws<ArgumentNullException>(() => MessageConversions.ToStruct(null!));
        Assert.Throws<ArgumentNullException>(() => MessageConversions.ToMessage<DiscoveryRequest>(null!));
        Assert.Throws<ArgumentNullException>(() => MessageConversions.ToMessage(null!, DiscoveryRequest.Descriptor));
        Assert.Throws<ArgumentNullException>(() => MessageConversions.ToMessage(new Struct(), null!));
        var mapEntry = Envoy.Config.Core.V3.Metadata.Descriptor.FindFieldByName("filter_metadata").MessageType;
        Assert.True(mapEntry.IsMapEntry);
        Assert.Null(mapEntry.Parser);
        var failure = Assert.Throws<ArgumentException>(() => MessageConversions.ToMessage(new Struct(), mapEntry));
        Assert.Equal("descriptor", failure.ParamName);
    }

    [Fact]
    public void ScalarAndArrayRoots_CannotBeRepresentedAsStruct()
    {
        Assert.Throws<InvalidProtocolBufferException>(() => MessageConversions.ToStruct(Duration.FromTimeSpan(TimeSpan.FromSeconds(1))));
        Assert.Throws<InvalidProtocolBufferException>(() => MessageConversions.ToStruct(new ListValue { Values = { Value.ForString("item") } }));
        var original = new Struct { Fields = { ["enabled"] = Value.ForBool(true), ["missing"] = Value.ForNull() } };
        Assert.Equal(original, MessageConversions.ToStruct(original));
        Assert.Equal(original, MessageConversions.ToMessage<Struct>(original));
    }
}
