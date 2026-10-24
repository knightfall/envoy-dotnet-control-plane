using System.Text.Json;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Pgv = Validate.ValidateExtensions;
using Protovalidate = Buf.Validate.ValidateExtensions;

namespace Envoy.ControlPlane.Validation.Proof;

internal static class OverlayComparison
{
    internal static void Run(string overlayPath, string outputPath)
    {
        var extensions = new ExtensionRegistry
        {
            Pgv.Rules, Pgv.Required, Pgv.Disabled, Pgv.Ignored,
            Protovalidate.Field, Protovalidate.Oneof, Protovalidate.Message,
        };
        var parser = FileDescriptorSet.Parser.WithExtensionRegistry(extensions);
        using var stream = typeof(Envoy.Config.Core.V3.Address).Assembly
            .GetManifestResourceStream("Envoy.ControlPlane.Protocol.catalog.pb")!;
        var original = parser.ParseFrom(stream);
        var overlay = parser.ParseFrom(File.ReadAllBytes(overlayPath));
        var files = overlay.File.ToDictionary(file => file.Name, StringComparer.Ordinal);
        int derivedFields = 0;
        int derivedOneofs = 0;

        foreach (var file in original.File)
        {
            var derived = files[file.Name].Clone();
            int addedImport = derived.Dependency.IndexOf("buf/validate/validate.proto");
            if (addedImport >= 0)
            {
                derived.Dependency.RemoveAt(addedImport);
                NormalizeIndexes(derived.PublicDependency, addedImport);
                NormalizeIndexes(derived.WeakDependency, addedImport);
            }

            foreach (var message in EnumerateMessages(derived.MessageType))
            {
                foreach (var field in message.Field)
                {
                    if (field.Options?.HasExtension(Protovalidate.Field) == true)
                    {
                        derivedFields++;
                        field.Options.ClearExtension(Protovalidate.Field);
                    }
                }
                foreach (var oneof in message.OneofDecl)
                {
                    if (oneof.Options?.HasExtension(Protovalidate.Oneof) == true)
                    {
                        derivedOneofs++;
                        oneof.Options.ClearExtension(Protovalidate.Oneof);
                    }
                }
            }

            if (!file.Equals(derived))
            {
                throw new InvalidOperationException("Overlay changed original descriptor beyond derived options: " + file.Name);
            }
        }

        var report = new
        {
            OriginalFilesCompared = original.File.Count,
            DerivedFieldOptions = derivedFields,
            DerivedOneofOptions = derivedOneofs,
            OriginalDescriptorsAndPgvOptionsPreserved = true,
            AdditionalFiles = files.Keys.Except(original.File.Select(file => file.Name)).Order(StringComparer.Ordinal).ToArray(),
        };
        File.WriteAllText(outputPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) + "\n");
        Console.WriteLine($"Compared all {original.File.Count} original descriptors; original options and contracts are unchanged.");
    }

    private static void NormalizeIndexes(Google.Protobuf.Collections.RepeatedField<int> indexes, int removed)
    {
        for (int index = 0; index < indexes.Count; index++)
        {
            if (indexes[index] > removed)
            {
                indexes[index]--;
            }
        }
    }

    private static IEnumerable<DescriptorProto> EnumerateMessages(IEnumerable<DescriptorProto> messages)
    {
        foreach (var message in messages)
        {
            yield return message;
            foreach (var nested in EnumerateMessages(message.NestedType))
            {
                yield return nested;
            }
        }
    }
}
