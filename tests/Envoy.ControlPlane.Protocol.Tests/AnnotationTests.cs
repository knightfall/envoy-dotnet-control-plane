using System.Text.Json.Nodes;
using Envoy.Extensions.Filters.Network.HttpConnectionManager.V3;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Validate;
using Xunit;

namespace Envoy.ControlPlane.Protocol.Tests;

[Collection(ProtocolBindingCollection.Name)]
public sealed class AnnotationTests
{
    [Fact]
    public void OriginalPgvOptions_TypedAccessAndDefinitions_RemainAvailable()
    {
        var prefix = HttpConnectionManager.Descriptor.FindFieldByName("stat_prefix");
        var rules = prefix.GetOptions().GetExtension(ValidateExtensions.Rules);
        Assert.Equal(1UL, rules.String.MinLen);
        var route = HttpConnectionManager.Descriptor.Oneofs.Single(o => o.Name == "route_specifier");
        Assert.True(route.GetOptions().GetExtension(ValidateExtensions.Required));
        Assert.Equal(1071, ValidateExtensions.Rules.FieldNumber);
        Assert.Equal("validate.FieldRules", FieldRules.Descriptor.FullName);
        Assert.Same(ProtocolCatalog.ProtocolAssembly, FieldRules.Descriptor.ClrType.Assembly);

        var registry = new ExtensionRegistry { ValidateExtensions.Rules, ValidateExtensions.Required, ValidateExtensions.Disabled, ValidateExtensions.Ignored };
        var source = FileDescriptorSet.Parser.WithExtensionRegistry(registry).ParseFrom(ProtocolCatalog.ReadEmbeddedResource("catalog.pb"));
        var actualMessages = ProtocolCatalog.Files.Value.SelectMany(f => ProtocolCatalog.EnumerateMessages(f.MessageTypes)).ToDictionary(m => m.FullName, StringComparer.Ordinal);
        int fieldRules = 0, requiredOneofs = 0;
        foreach (var file in source.File)
        {
            foreach (var (name, message) in ProtocolCatalog.EnumerateSourceMessages(file.Package, file.MessageType))
            {
                var actual = actualMessages[name];
                foreach (var field in message.Field.Where(f => f.Options?.HasExtension(ValidateExtensions.Rules) == true))
                {
                    Assert.Equal(field.Options.GetExtension(ValidateExtensions.Rules), actual.FindFieldByNumber(field.Number).GetOptions().GetExtension(ValidateExtensions.Rules));
                    fieldRules++;
                }
                foreach (var oneof in message.OneofDecl.Where(o => o.Options?.HasExtension(ValidateExtensions.Required) == true))
                {
                    Assert.Equal(oneof.Options.GetExtension(ValidateExtensions.Required), actual.Oneofs.Single(o => o.Name == oneof.Name).GetOptions().GetExtension(ValidateExtensions.Required));
                    requiredOneofs++;
                }
            }
        }
        Assert.True(fieldRules > 1000, "Expected the complete PGV field-option corpus");
        Assert.True(requiredOneofs > 100, "Expected the complete PGV required-oneof corpus");
        var inventory = AnnotationInventory.Create(source);
        var expectedInventory = JsonNode.Parse(ProtocolCatalog.ReadEmbeddedResource("annotations.json"));
        Assert.True(JsonNode.DeepEquals(expectedInventory, inventory), "Annotation inventory differs: " + inventory.ToJsonString());
    }
}
