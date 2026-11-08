using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Envoy.ControlPlane.Tools;
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
        Assert.Equal(expected.Append("buf/validate/validate.proto").Order(StringComparer.Ordinal),
            ProtocolCatalog.Files.Value.Select(f => f.Name).Order(StringComparer.Ordinal));
        var messages = ProtocolCatalog.Files.Value.SelectMany(f => ProtocolCatalog.EnumerateMessages(f.MessageTypes)).ToArray();
        Assert.Equal(messages.Length, messages.Select(m => m.FullName).Distinct(StringComparer.Ordinal).Count());
        var loadedNames = AppDomain.CurrentDomain.GetAssemblies().SelectMany(a => a.GetTypes())
            .Where(t => t.FullName is not null).GroupBy(t => t.FullName!, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        foreach (var message in messages.Where(m => !m.IsMapEntry))
        {
            var owner = message.File.Package switch
            {
                "google.protobuf" => typeof(IMessage).Assembly,
                "buf.validate" => typeof(Buf.Validate.FieldRules).Assembly,
                _ => ProtocolCatalog.ProtocolAssembly,
            };
            Assert.Equal(owner, message.ClrType.Assembly);
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
        registry.AddRange(typeof(Buf.Validate.FieldRules).Assembly.GetTypes().SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.Static))
            .Where(f => typeof(Google.Protobuf.Extension).IsAssignableFrom(f.FieldType)).Select(f => (Google.Protobuf.Extension)f.GetValue(null)!));
        var source = FileDescriptorSet.Parser.WithExtensionRegistry(registry).ParseFrom(bytes).File.ToDictionary(f => f.Name, StringComparer.Ordinal);
        var derived = FileDescriptorSet.Parser.WithExtensionRegistry(registry)
            .ParseFrom(ProtocolCatalog.ReadEmbeddedResource("validation.catalog.pb")).File.ToDictionary(f => f.Name, StringComparer.Ordinal);
        foreach (var file in ProtocolCatalog.Files.Value)
        {
            var expected = derived[file.Name].Clone();
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
            Assert.True(DescriptorsMatch(expected, actual), "Descriptor differs: " + file.Name + "\nSOURCE: " + expected + "\nC#: " + actual);
            if (source.TryGetValue(file.Name, out var original))
            {
                OverlayTransform.RemoveDerivedMetadata(actual);
                var normalizedOriginal = original.Clone();
                DescriptorComparison.NormalizeRuntimeDescriptorMetadata(normalizedOriginal.MessageType);
                foreach (var field in normalizedOriginal.Extension)
                {
                    field.ClearJsonName();
                }
                Assert.True(DescriptorsMatch(normalizedOriginal, actual), "Original options or contract changed: " + file.Name);
            }
        }
    }

    [Fact]
    public void DescriptorRuntimeException_RejectsSourceAndRuntimeDrift()
    {
        var source = FileDescriptorSet.Parser.ParseFrom(ProtocolCatalog.ReadEmbeddedResource("validation.catalog.pb"))
            .File.Single(f => f.Name == "google/protobuf/descriptor.proto").Clone();
        var runtime = ProtocolCatalog.Files.Value.Single(f => f.Name == source.Name).ToProto();
        DescriptorComparison.NormalizeRuntimeDescriptorMetadata(source.MessageType);
        DescriptorComparison.NormalizeRuntimeDescriptorMetadata(runtime.MessageType);

        Assert.True(DescriptorsMatch(source, runtime));
        var changedSource = source.Clone();
        changedSource.MessageType[0].Field[0].Number++;
        Assert.False(DescriptorsMatch(changedSource, runtime));
        var changedRuntime = runtime.Clone();
        changedRuntime.MessageType[0].Field.RemoveAt(0);
        Assert.False(DescriptorsMatch(source, changedRuntime));
        changedRuntime = runtime.Clone();
        changedRuntime.MessageType[0].Field[0].Options = new FieldOptions { Deprecated = true };
        Assert.False(DescriptorsMatch(source, changedRuntime));
    }

    /// <summary>
    /// Pins the full normalized source and runtime descriptors independently so
    /// the 35.1 source and 3.36.2 runtime difference cannot hide further drift.
    /// </summary>
    private static bool DescriptorsMatch(FileDescriptorProto expected, FileDescriptorProto actual)
    {
        if (expected.Equals(actual))
        {
            return true;
        }

        return expected.Name == "google/protobuf/descriptor.proto"
            && actual.Name == expected.Name
            && Convert.ToHexStringLower(SHA256.HashData(expected.ToByteArray())) == "b21e5ddcc0eb5ad054804d1112a9a70dbcff0697d5b3deae9a58ed69af725d71"
            && Convert.ToHexStringLower(SHA256.HashData(actual.ToByteArray())) == "2f04a0c1e3119244e70302f150ae5f020315a07b071ed92742e21d80bdb1ab52";
    }
}
