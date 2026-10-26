using Envoy.ControlPlane.Tools;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Xunit;

namespace Envoy.ControlPlane.Protocol.Tests;

[Collection(ProtocolBindingCollection.Name)]
public sealed class DescriptorSnapshotTests
{
    [Fact]
    public void RealCatalog_ReportsOriginalRulesAndDerivedOptionsWithoutConflatingThem()
    {
        var original = Read("catalog.pb");
        var derived = Read("validation.catalog.pb");

        var report = DescriptorSnapshot.Create(original, derived);

        Assert.Equal(523, report["files"]!.AsObject().Count);
        Assert.Equal(524, report["derivedFiles"]!.AsObject().Count);
        Assert.Equal(1371, report["pgvRules"]!.AsObject().Count);
        Assert.Equal(59, report["rulePathCounts"]!.AsObject().Count);
        var header = report["pgvRules"]!["field:envoy.config.core.v3.HeaderValue.key"]!;
        Assert.False(header["rules"]!["string"]!["strict"]!.GetValue<bool>());
        var translated = report["derivedRules"]!["field:envoy.config.core.v3.HeaderValue.key"]!;
        Assert.False(translated["string"]!["strict"]!.GetValue<bool>());
        Assert.Equal("1", translated["string"]!["minLen"]!.GetValue<string>());
        Assert.True(report["pgvRules"]!["oneof:envoy.config.core.v3.Address.address"]!["required"]!.GetValue<bool>());
        Assert.True(report["derivedRules"]!["oneof:envoy.config.core.v3.Address.address"]!["required"]!.GetValue<bool>());
    }

    [Fact]
    public void DerivedContractDrift_IsRejectedBeforeProducingAnUpdateSnapshot()
    {
        var original = Read("catalog.pb");
        var derived = Read("validation.catalog.pb");
        derived.File.Single(file => file.Name == "envoy/config/core/v3/address.proto").Package = "changed.contract";

        var error = Assert.Throws<InvalidOperationException>(() => DescriptorSnapshot.Create(original, derived));

        Assert.Contains("changed an original descriptor: envoy/config/core/v3/address.proto", error.Message, StringComparison.Ordinal);
    }

    private static FileDescriptorSet Read(string resource)
    {
        var registry = new ExtensionRegistry
        {
            Validate.ValidateExtensions.Rules, Validate.ValidateExtensions.Required,
            Validate.ValidateExtensions.Disabled, Validate.ValidateExtensions.Ignored,
            Buf.Validate.ValidateExtensions.Field, Buf.Validate.ValidateExtensions.Oneof, Buf.Validate.ValidateExtensions.Message,
        };
        return FileDescriptorSet.Parser.WithExtensionRegistry(registry).ParseFrom(ProtocolCatalog.ReadEmbeddedResource(resource));
    }
}
