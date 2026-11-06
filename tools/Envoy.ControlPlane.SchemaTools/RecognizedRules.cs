using System.Collections;
using System.Text.Json.Nodes;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Pgv = Validate.ValidateExtensions;

namespace Envoy.ControlPlane.Tools;

internal static class RecognizedRules
{
    internal static string[] FindUnsupported(FileDescriptorSet source, IReadOnlySet<string> recognized)
    {
        var unsupported = new List<string>();
        foreach (var file in source.File)
        {
            foreach (var (name, message) in EnumerateMessages(file.Package, file.MessageType))
            {
                if (message.Options?.GetExtension(Pgv.Disabled) == true || message.Options?.GetExtension(Pgv.Ignored) == true)
                {
                    unsupported.Add(name + ": message-level disabled/ignored metadata");
                }
                foreach (var field in message.Field)
                {
                    var rules = field.Options?.GetExtension(Pgv.Rules);
                    if (rules is null)
                    {
                        continue;
                    }
                    string fieldName = name + "." + field.Name;
                    if (HasUnknownFields(rules))
                    {
                        unsupported.Add(fieldName + ": unknown encoded rule fields");
                    }
                    foreach (string path in PgvRuleInventory.EnumerateRulePaths(rules, ""))
                    {
                        if (!recognized.Contains(path))
                        {
                            unsupported.Add(fieldName + ": " + path);
                        }
                    }
                    foreach (string value in FindUnknownEnumValues(rules, ""))
                    {
                        unsupported.Add(fieldName + ": " + value);
                    }
                }
            }
        }
        return unsupported.Order(StringComparer.Ordinal).ToArray();
    }

    internal static void Verify(string descriptorPath, string manifestPath)
    {
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!;
        if (manifest["schemaVersion"]?.GetValue<int>() != 1)
        {
            throw new InvalidOperationException("Unsupported recognized-rule manifest");
        }
        var paths = manifest["recognizedRulePaths"]!.AsArray().Select(item => item!.GetValue<string>()).ToArray();
        var recognized = paths.ToHashSet(StringComparer.Ordinal);
        if (recognized.Count != paths.Length || paths.Length == 0)
        {
            throw new InvalidOperationException("Recognized-rule manifest is empty or has duplicate paths");
        }
        string[] unsupported = FindUnsupported(OverlayTransform.Read(descriptorPath), recognized);
        if (unsupported.Length != 0)
        {
            throw new InvalidOperationException("Unsupported PGV metadata or rules:\n" + string.Join("\n", unsupported));
        }
        Console.WriteLine($"Recognized all PGV rule paths in the source catalog ({recognized.Count} recognized paths; conformance remains separate).");
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

    private static bool HasUnknownFields(IMessage message)
    {
        var copy = message.Descriptor.Parser.ParseFrom(message.ToByteArray());
        foreach (var field in copy.Descriptor.Fields.InFieldNumberOrder())
        {
            object value = field.Accessor.GetValue(copy);
            if (value is IMessage child && child.Descriptor.File.Package == "validate" && HasUnknownFields(child))
            {
                return true;
            }
            if (field.IsRepeated && value is IEnumerable children)
            {
                foreach (object item in children)
                {
                    if (item is IMessage nested && nested.Descriptor.File.Package == "validate" && HasUnknownFields(nested))
                    {
                        return true;
                    }
                }
            }
        }
        // Inspect nested rules first: clearing an unset oneof member may clear
        // the selected member before its unknown fields have been checked.
        foreach (var field in copy.Descriptor.Fields.InFieldNumberOrder())
        {
            field.Accessor.Clear(copy);
        }
        return copy.CalculateSize() != 0;
    }

    private static IEnumerable<string> FindUnknownEnumValues(IMessage message, string prefix)
    {
        foreach (var field in message.Descriptor.Fields.InFieldNumberOrder())
        {
            object value = field.Accessor.GetValue(message);
            if (field.FieldType == FieldType.Enum && field.Accessor.HasValue(message))
            {
                int number = Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
                if (field.EnumType.FindValueByNumber(number) is null
                    || (field.EnumType.FullName == "validate.KnownRegex" && number == 0))
                {
                    yield return "unsupported enum value " + number + " at " + prefix + field.Name;
                }
            }
            if (value is IMessage child && child.Descriptor.File.Package == "validate")
            {
                foreach (string nested in FindUnknownEnumValues(child, prefix + field.Name + "."))
                {
                    yield return nested;
                }
            }
        }
    }
}
