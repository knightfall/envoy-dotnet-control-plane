using System.Text.Json.Nodes;
using Envoy.ControlPlane.Tools;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Validate;
using Xunit;

namespace Envoy.ControlPlane.Protocol.Tests;

[Collection(ProtocolBindingCollection.Name)]
public sealed class ValidationInventoryTests
{
    [Fact]
    public void Inventory_RealCatalog_IncludesNestedCollectionAndCombinedMessageRules()
    {
        var inventory = CreateInventory();
        Assert.Equal(523, inventory["fileCount"]!.GetValue<int>());
        Assert.Equal(1624, inventory["messageCount"]!.GetValue<int>());
        Assert.Equal(1244, inventory["fieldRuleCount"]!.GetValue<int>());
        Assert.Equal(127, inventory["oneofOptionCount"]!.GetValue<int>());
        Assert.Equal(59, inventory["rulePathCounts"]!.AsObject().Count);

        var map = FindField(inventory, "envoy.config.core.v3.Metadata.filter_metadata");
        Assert.Equal("map.keys.string.min_len", Assert.Single(map["rulePaths"]!.AsArray())!.GetValue<string>());
        Assert.Equal("1", map["rules"]!["map"]!["keys"]!["string"]!["minLen"]!.GetValue<string>());

        var identifier = FindField(inventory, "envoy.config.core.v3.Http2ProtocolOptions.SettingsParameter.identifier");
        Assert.Equal(new[] { "uint32.lte", "uint32.gte", "message.required" },
            identifier["rulePaths"]!.AsArray().Select(path => path!.GetValue<string>()));
    }

    [Fact]
    public void Inventory_ExplicitFalseAndEmptyDurationBoundary_PreserveRulePresence()
    {
        var inventory = CreateInventory();
        var header = FindField(inventory, "envoy.config.core.v3.HeaderValue.key");
        Assert.False(header["rules"]!["string"]!["strict"]!.GetValue<bool>());
        Assert.Contains(header["rulePaths"]!.AsArray(), path => path!.GetValue<string>() == "string.strict");

        var interval = FindField(inventory, "envoy.config.core.v3.BackoffStrategy.max_interval");
        Assert.Equal("duration.gt", Assert.Single(interval["rulePaths"]!.AsArray())!.GetValue<string>());
        Assert.Equal("0s", interval["rules"]!["duration"]!["gt"]!.GetValue<string>());
    }

    [Fact]
    public void Inventory_AnyAndSkippedMessageRules_RemainExplicit()
    {
        var inventory = CreateInventory();
        Assert.Empty(inventory["messageOptions"]!.AsArray());
        Assert.Equal(94, inventory["anyFields"]!.AsArray().Count);
        Assert.Contains(inventory["anyFields"]!.AsArray(),
            path => path!.GetValue<string>() == "envoy.config.core.v3.TypedExtensionConfig.typed_config");
        var skipped = FindField(inventory, "envoy.config.core.v3.KeyValueAppend.entry");
        Assert.True(skipped["rules"]!["message"]!["skip"]!.GetValue<bool>());
        Assert.Equal(1, inventory["rulePathCounts"]!["message.skip"]!.GetValue<int>());
        Assert.Equal(3, inventory["rulePathCounts"]!["any.required"]!.GetValue<int>());
    }

    private static JsonObject CreateInventory()
    {
        var extensions = new ExtensionRegistry
        {
            ValidateExtensions.Rules, ValidateExtensions.Required,
            ValidateExtensions.Disabled, ValidateExtensions.Ignored,
        };
        var source = FileDescriptorSet.Parser.WithExtensionRegistry(extensions)
            .ParseFrom(ProtocolCatalog.ReadEmbeddedResource("catalog.pb"));
        return PgvRuleInventory.Create(source);
    }

    private static JsonNode FindField(JsonObject inventory, string name) =>
        inventory["fields"]!.AsArray().Single(field => field!["field"]!.GetValue<string>() == name)!;
}
