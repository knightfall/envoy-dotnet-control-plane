using System.Text.Json;
using System.Text.Json.Nodes;
using Envoy.Config.Cluster.V3;
using Envoy.Config.Core.V3;
using Envoy.Config.Endpoint.V3;
using Envoy.ControlPlane.Resources;
using Envoy.Service.Discovery.V3;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Google.Protobuf.WellKnownTypes;
using Xunit;

namespace Envoy.ControlPlane.Tests.Support;

internal static class ConversionCorpus
{
    private static readonly TypeRegistry Types = TypeRegistry.FromMessages(DiscoveryRequest.Descriptor, DiscoveryResponse.Descriptor,
        Node.Descriptor, Cluster.Descriptor, ClusterStats.Descriptor, Any.Descriptor, Duration.Descriptor, ListValue.Descriptor,
        Value.Descriptor, Struct.Descriptor, Google.Rpc.Status.Descriptor);

    internal static ConversionInput[] Cases() =>
    [
        Roundtrip("request", new DiscoveryRequest { VersionInfo = "test", Node = new Node { Id = "proxy" } },
            """{"version_info":"test","node":{"id":"proxy"}}"""),
        Roundtrip("default", new DiscoveryRequest(), "{}"),
        Roundtrip("oneof-default-present", new Node { UserAgentVersion = "" }, """{"user_agent_version":""}"""),
        Roundtrip("enum", new Cluster { Name = "a", Type = Cluster.Types.DiscoveryType.StrictDns }, """{"name":"a","type":"STRICT_DNS"}"""),
        Roundtrip("uint64", new ClusterStats { TotalDroppedRequests = ulong.MaxValue }, """{"total_dropped_requests":"18446744073709551615"}"""),
        Roundtrip("struct", Struct.Parser.ParseJson("""{"enabled":true,"number":1.5,"missing":null,"list":["item",false]}"""),
            """{"enabled":true,"number":1.5,"missing":null,"list":["item",false]}"""),
        Roundtrip("registered-any", Any.Pack(new Cluster { Name = "a" }),
            """{"@type":"type.googleapis.com/envoy.config.cluster.v3.Cluster","name":"a"}"""),
        Roundtrip("other-descriptor-any", Any.Pack(new Google.Rpc.Status { Code = 14, Message = "test" }),
            """{"@type":"type.googleapis.com/google.rpc.Status","code":14,"message":"test"}"""),
        Roundtrip("unknown-any", new Any { TypeUrl = "type.googleapis.com/unknown.Type" }, null) with { ExpectedError = "to-struct" },
        Roundtrip("malformed-any", new Any { TypeUrl = ResourceTypeUrls.Cluster, Value = ByteString.CopyFrom([0xff]) }, null) with { ExpectedError = "to-struct" },
        Roundtrip("scalar-root", Duration.FromTimeSpan(TimeSpan.FromSeconds(1)), null) with { ExpectedError = "to-struct" },
        Roundtrip("array-root", new ListValue { Values = { Value.ForString("item") } }, null) with { ExpectedError = "to-struct" },
        Roundtrip("null-message", new DiscoveryRequest(), null) with { NullInput = true, ExpectedError = "to-struct" },
        From("from-struct", """{"version_info":"restored","node":{"id":"proxy"}}""",
            new DiscoveryRequest { VersionInfo = "restored", Node = new Node { Id = "proxy" } }),
        From("from-unknown-field", """{"unknown":true}""", new DiscoveryRequest()) with { ExpectedError = "to-message" },
        From("from-invalid-type", """{"version_info":42}""", new DiscoveryRequest()) with { ExpectedError = "to-message" },
        From("from-null", "{}", new DiscoveryRequest()) with { NullInput = true, ExpectedError = "to-message" },
    ];

    internal static ConversionResult Evaluate(ConversionInput input)
    {
        var descriptor = Types.Find(input.TypeName) ?? throw new InvalidOperationException("Unknown fixture descriptor: " + input.TypeName);
        IMessage? converted = null;
        JsonElement? json = null;
        string error = "";
        string stage = input.Mode == "from" ? "to-message" : "to-struct";
        try
        {
            Struct value;
            if (input.Mode == "from")
            {
                value = input.NullInput ? null! : Struct.Parser.ParseJson(input.StructJson!);
            }
            else
            {
                var message = input.NullInput ? null! : descriptor.Parser.ParseFrom(Convert.FromBase64String(input.Data));
                value = MessageConversions.ToStruct(message, Types);
                string actual = JsonFormatter.Default.Format(value);
                json = JsonSerializer.Deserialize<JsonElement>(actual);
            }
            stage = "to-message";
            converted = MessageConversions.ToMessage(value, descriptor, Types);
        }
        catch (Exception failure) when (failure is ArgumentException or InvalidOperationException or InvalidProtocolBufferException)
        {
            error = stage;
        }
        Assert.Equal(input.ExpectedError, error);
        if (input.ExpectedError.Length == 0)
        {
            Assert.NotNull(converted);
            Assert.Equal(descriptor.Parser.ParseFrom(Convert.FromBase64String(input.Data)), converted);
            if (input.Mode != "from")
            {
                Assert.True(JsonNode.DeepEquals(JsonNode.Parse(input.ExpectedStruct!), JsonNode.Parse(json!.Value.GetRawText())), input.Id + ": Struct differs");
            }
        }
        return new ConversionResult(input, new ConversionObservation(error, json,
            converted is null ? null : Convert.ToBase64String(ResourceSerialization.Serialize(converted))));
    }

    internal static void Write(IEnumerable<ConversionResult> results)
    {
        var report = new ConversionReport((int)ApiVersion.V3, HelperSymbols.All(), results.ToArray());
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "conversion-corpus.json"),
            JsonSerializer.Serialize(report, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
    }

    private static ConversionInput Roundtrip(string id, IMessage message, string? expected) =>
        new(id, "roundtrip", message.Descriptor.FullName, Convert.ToBase64String(ResourceSerialization.Serialize(message)), ExpectedStruct: expected);

    private static ConversionInput From(string id, string json, IMessage expected) =>
        new(id, "from", expected.Descriptor.FullName, Convert.ToBase64String(ResourceSerialization.Serialize(expected)), StructJson: json);
}

internal sealed record ConversionInput(string Id, string Mode, string TypeName, string Data, string? StructJson = null,
    string? ExpectedStruct = null, bool NullInput = false, string ExpectedError = "");
internal sealed record ConversionObservation(string Error, JsonElement? Struct, string? Message);
internal sealed record ConversionResult(ConversionInput Input, ConversionObservation Observed);
internal sealed record ConversionReport(int ApiVersion, HelperSymbol[] Symbols, ConversionResult[] Cases);
