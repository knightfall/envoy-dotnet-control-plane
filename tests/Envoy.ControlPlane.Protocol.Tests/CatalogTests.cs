using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Xunit;

namespace Envoy.ControlPlane.Protocol.Tests;

[Collection(ProtocolBindingCollection.Name)]
public sealed class CatalogTests
{
    [Fact]
    public void Catalog_SelectedInputs_HaveExactlyOneDescriptorAndClrOwner()
    {
        var manifest = JsonNode.Parse(ProtocolCatalog.ReadEmbeddedResource("catalog.json"))!;
        string[] expected = manifest["files"]!.AsArray().Where(f => f!["disposition"]!.GetValue<string>() is "generate" or "dependency")
            .Select(f => f!["path"]!.GetValue<string>()).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(523, expected.Length);
        Assert.Equal(expected, ProtocolCatalog.Files.Value.Select(f => f.Name).Order(StringComparer.Ordinal));
        var messages = ProtocolCatalog.Files.Value.SelectMany(f => ProtocolCatalog.EnumerateMessages(f.MessageTypes)).ToArray();
        Assert.Equal(messages.Length, messages.Select(m => m.FullName).Distinct(StringComparer.Ordinal).Count());
        var loadedNames = AppDomain.CurrentDomain.GetAssemblies().SelectMany(a => a.GetTypes())
            .Where(t => t.FullName is not null).GroupBy(t => t.FullName!, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        foreach (var message in messages.Where(m => !m.IsMapEntry))
        {
            Assert.Equal(message.File.Package == "google.protobuf" ? typeof(IMessage).Assembly : ProtocolCatalog.ProtocolAssembly, message.ClrType.Assembly);
            Assert.Equal(1, loadedNames[message.ClrType.FullName!]);
        }
        Assert.DoesNotContain(AppDomain.CurrentDomain.GetAssemblies(), a => a.GetName().Name == "Google.Api.CommonProtos");
    }

    [Fact]
    public void Descriptors_AllSelectedInputs_MatchPinnedSourceIncludingOptions()
    {
        byte[] bytes = ProtocolCatalog.ReadEmbeddedResource("catalog.pb");
        var bindingLock = JsonNode.Parse(ProtocolCatalog.ReadEmbeddedResource("bindings.lock.json"))!;
        Assert.Equal(bindingLock["catalogSha256"]!.GetValue<string>(), Convert.ToHexStringLower(SHA256.HashData(ProtocolCatalog.ReadEmbeddedResource("catalog.json"))));
        Assert.Equal(bindingLock["descriptorSetSha256"]!.GetValue<string>(), Convert.ToHexStringLower(SHA256.HashData(bytes)));
        var registry = new ExtensionRegistry();
        registry.AddRange(ProtocolCatalog.ProtocolAssembly.GetTypes().SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.Static))
            .Where(f => typeof(Google.Protobuf.Extension).IsAssignableFrom(f.FieldType)).Select(f => (Google.Protobuf.Extension)f.GetValue(null)!));
        var source = FileDescriptorSet.Parser.WithExtensionRegistry(registry).ParseFrom(bytes).File.ToDictionary(f => f.Name, StringComparer.Ordinal);
        foreach (var file in ProtocolCatalog.Files.Value)
        {
            var expected = source[file.Name].Clone();
            var actual = FileDescriptorProto.Parser.WithExtensionRegistry(registry).ParseFrom(file.ToProto().ToByteArray());
            // C# omits inferred json_name strings from its compact descriptors.
            // Compare each effective JSON name before normalizing that presence.
            DescriptorComparison.CompareJsonNames(expected.MessageType, file.MessageTypes);
            DescriptorComparison.CompareExtensionJsonNames(expected.Extension, file.Extensions.UnorderedExtensions);
            DescriptorComparison.NormalizeRuntimeDescriptorMetadata(expected.MessageType);
            DescriptorComparison.NormalizeRuntimeDescriptorMetadata(actual.MessageType);
            foreach (var field in expected.Extension)
            {
                field.ClearJsonName();
            }
            foreach (var field in actual.Extension)
            {
                field.ClearJsonName();
            }
            Assert.True(expected.Equals(actual), "Descriptor differs: " + file.Name + "\nSOURCE: " + expected + "\nC#: " + actual);
        }
    }
}
