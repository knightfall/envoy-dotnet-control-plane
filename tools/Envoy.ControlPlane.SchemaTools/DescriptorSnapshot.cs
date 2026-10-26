using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Protovalidate = Buf.Validate.ValidateExtensions;

namespace Envoy.ControlPlane.Tools;

internal static class DescriptorSnapshot
{
    internal static JsonObject Create(FileDescriptorSet original, FileDescriptorSet derived)
    {
        OverlayTransform.VerifyOriginalDescriptors(original, derived);
        var inventory = PgvRuleInventory.Create(original);
        var pgv = new JsonObject();
        AddRules("fields", "field", "field:");
        AddRules("oneofs", "oneof", "oneof:");
        foreach (var row in inventory["messageOptions"]!.AsArray())
        {
            pgv.Add("message:" + row!["message"]!.GetValue<string>() + ":" + row["option"]!.GetValue<string>(), row.DeepClone());
        }

        var translated = new JsonObject();
        foreach (var file in derived.File.OrderBy(file => file.Name, StringComparer.Ordinal))
        {
            foreach (var (name, message) in EnumerateMessages(file.Package, file.MessageType))
            {
                if (message.Options?.HasExtension(Protovalidate.Message) == true)
                {
                    AddTranslated("message:" + name, message.Options.GetExtension(Protovalidate.Message));
                }
                foreach (var field in message.Field.Where(field => field.Options?.HasExtension(Protovalidate.Field) == true))
                {
                    AddTranslated("field:" + name + "." + field.Name, field.Options.GetExtension(Protovalidate.Field));
                }
                foreach (var oneof in message.OneofDecl.Where(oneof => oneof.Options?.HasExtension(Protovalidate.Oneof) == true))
                {
                    AddTranslated("oneof:" + name + "." + oneof.Name, oneof.Options.GetExtension(Protovalidate.Oneof));
                }
            }
        }

        return new JsonObject
        {
            ["files"] = FingerprintFiles(original),
            ["derivedFiles"] = FingerprintFiles(derived),
            ["pgvRules"] = pgv,
            ["derivedRules"] = translated,
            ["rulePathCounts"] = inventory["rulePathCounts"]!.DeepClone(),
        };

        void AddRules(string collection, string key, string prefix)
        {
            foreach (var row in inventory[collection]!.AsArray())
            {
                pgv.Add(prefix + row![key]!.GetValue<string>(), row.DeepClone());
            }
        }

        void AddTranslated(string name, IMessage rules) =>
            translated.Add(name, JsonNode.Parse(JsonFormatter.Default.Format(rules)));
    }

    private static JsonObject FingerprintFiles(FileDescriptorSet descriptors)
    {
        var files = new JsonObject();
        foreach (var file in descriptors.File.OrderBy(file => file.Name, StringComparer.Ordinal))
        {
            var contract = file.Clone();
            contract.SourceCodeInfo = null;
            files.Add(file.Name, Convert.ToHexStringLower(SHA256.HashData(contract.ToByteArray())));
        }
        return files;
    }

    private static IEnumerable<(string Name, DescriptorProto Message)> EnumerateMessages(
        string prefix, IEnumerable<DescriptorProto> messages)
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
}
