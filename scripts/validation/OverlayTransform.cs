using System.Text.Json;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Pgv = Validate.ValidateExtensions;
using Protovalidate = Buf.Validate.ValidateExtensions;

namespace Envoy.ControlPlane.Tools;

internal static class OverlayTransform
{
    internal static string[] PreserveConditionalOneofRequirements(FileDescriptorSet original, FileDescriptorSet derived)
    {
        var files = derived.File.ToDictionary(file => file.Name, StringComparer.Ordinal);
        var changes = new List<string>();
        foreach (var file in original.File)
        {
            var target = files[file.Name];
            foreach (var (name, message) in EnumerateMessages(file.Package, file.MessageType))
            {
                var targetMessage = EnumerateMessages(target.Package, target.MessageType).Single(item => item.Name == name).Message;
                foreach (var field in message.Field.Where(field => field.HasOneofIndex && !field.Proto3Optional))
                {
                    var rules = field.Options?.GetExtension(Pgv.Rules);
                    bool conditionallyRequired = rules?.Message?.Required == true || rules?.Any?.Required == true
                        || rules?.Duration?.Required == true || rules?.Timestamp?.Required == true;
                    if (!conditionallyRequired)
                    {
                        continue;
                    }

                    var targetField = targetMessage.Field.Single(item => item.Number == field.Number);
                    var translated = targetField.Options?.GetExtension(Protovalidate.Field)
                        ?? throw new InvalidOperationException("Missing derived rule for " + name + "." + field.Name);
                    if (!translated.Required)
                    {
                        throw new InvalidOperationException("Official required translation changed for " + name + "." + field.Name);
                    }
                    // A oneof message member cannot be selected with a null value through
                    // the C# API. PGV's conditional nil check adds no presence requirement
                    // when another member, or no member, is selected. Retain content rules.
                    translated.ClearRequired();
                    changes.Add(name + "." + field.Name);
                }
            }
        }
        return changes.Order(StringComparer.Ordinal).ToArray();
    }

    internal static void VerifyOriginalDescriptors(FileDescriptorSet original, FileDescriptorSet derived)
    {
        var files = derived.File.ToDictionary(file => file.Name, StringComparer.Ordinal);
        foreach (var expected in original.File)
        {
            var actual = files[expected.Name].Clone();
            RemoveDerivedMetadata(actual);
            actual.SourceCodeInfo = null;
            if (!expected.Equals(actual))
            {
                throw new InvalidOperationException("Derived metadata changed an original descriptor: " + expected.Name);
            }
        }
    }

    internal static void RemoveDerivedMetadata(FileDescriptorProto file)
    {
        int addedImport = file.Dependency.IndexOf("buf/validate/validate.proto");
        if (addedImport >= 0)
        {
            file.Dependency.RemoveAt(addedImport);
            NormalizeIndexes(file.PublicDependency, addedImport);
            NormalizeIndexes(file.WeakDependency, addedImport);
        }
        foreach (var (_, message) in EnumerateMessages(file.Package, file.MessageType))
        {
            foreach (var field in message.Field.Where(field => field.Options is not null))
            {
                field.Options.ClearExtension(Protovalidate.Field);
            }
            foreach (var oneof in message.OneofDecl.Where(oneof => oneof.Options is not null))
            {
                oneof.Options.ClearExtension(Protovalidate.Oneof);
            }
        }
    }

    internal static FileDescriptorSet Read(string path)
    {
        var extensions = new ExtensionRegistry
        {
            Pgv.Rules, Pgv.Required, Pgv.Disabled, Pgv.Ignored,
            Protovalidate.Field, Protovalidate.Oneof, Protovalidate.Message,
        };
        return FileDescriptorSet.Parser.WithExtensionRegistry(extensions).ParseFrom(File.ReadAllBytes(path));
    }

    internal static void Create(string originalPath, string migratedPath, string outputPath)
    {
        var original = Read(originalPath);
        var derived = Read(migratedPath);
        string[] changes = PreserveConditionalOneofRequirements(original, derived);
        VerifyOriginalDescriptors(original, derived);
        // Code generation needs upstream comments; the packaged contract image does not.
        File.WriteAllBytes(Path.ChangeExtension(outputPath, "generation.pb"), derived.ToByteArray());
        foreach (var file in derived.File)
        {
            file.SourceCodeInfo = null;
        }
        File.WriteAllBytes(outputPath, derived.ToByteArray());
        var report = new { OriginalFilesCompared = original.File.Count, ConditionalOneofRequirements = changes };
        File.WriteAllText(Path.ChangeExtension(outputPath, ".report.json"),
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) + "\n");
        Console.WriteLine($"Preserved {original.File.Count} original descriptors; corrected {changes.Length} conditional oneof requirements.");
    }

    private static IEnumerable<(string Name, DescriptorProto Message)> EnumerateMessages(string prefix, IEnumerable<DescriptorProto> messages)
    {
        foreach (var message in messages)
        {
            string name = prefix + "." + message.Name;
            yield return (name, message);
            foreach (var nested in EnumerateMessages(name, message.NestedType))
            {
                yield return nested;
            }
        }
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
}
