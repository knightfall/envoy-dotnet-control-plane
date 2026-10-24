using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Validate;

namespace Envoy.ControlPlane.Protocol.Tests;

internal static class AnnotationInventory
{
    internal static JsonObject Create(FileDescriptorSet source)
    {
        var definitions = new JsonArray();
        var ruleCases = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var values = new List<string>();
        int fields = 0;
        int oneofs = 0;

        foreach (var file in source.File.OrderBy(f => f.Name, StringComparer.Ordinal))
        {
            AddDefinitions(file.Package, file.Name, file.Extension);
            foreach (var (name, message) in ProtocolCatalog.EnumerateSourceMessages(file.Package, file.MessageType))
            {
                AddDefinitions(name, file.Name, message.Extension);
                foreach (var field in message.Field.Where(f => f.Options?.HasExtension(ValidateExtensions.Rules) == true))
                {
                    var rules = field.Options.GetExtension(ValidateExtensions.Rules);
                    string kind = rules.TypeCase.ToString();
                    ruleCases[kind] = ruleCases.GetValueOrDefault(kind) + 1;
                    values.Add(name + "." + field.Name + ":" + Convert.ToHexStringLower(SHA256.HashData(rules.ToByteArray())));
                    fields++;
                }
                foreach (var oneof in message.OneofDecl.Where(o => o.Options?.HasExtension(ValidateExtensions.Required) == true))
                {
                    values.Add(name + "." + oneof.Name + ":required=" + oneof.Options.GetExtension(ValidateExtensions.Required));
                    oneofs++;
                }
            }
        }

        var cases = new JsonObject();
        foreach (var (name, count) in ruleCases)
        {
            cases.Add(name, count);
        }
        byte[] canonicalValues = System.Text.Encoding.UTF8.GetBytes(string.Join('\n', values.Order(StringComparer.Ordinal)));
        return new JsonObject
        {
            ["schemaVersion"] = 1,
            ["sourceDescriptorSetSha256"] = Convert.ToHexStringLower(SHA256.HashData(ProtocolCatalog.ReadEmbeddedResource("catalog.pb"))),
            ["extensionDefinitions"] = definitions,
            ["pgv"] = new JsonObject
            {
                ["fieldRuleCount"] = fields,
                ["requiredOneofOptionCount"] = oneofs,
                ["fieldRuleCases"] = cases,
                ["originalValuesSha256"] = Convert.ToHexStringLower(SHA256.HashData(canonicalValues)),
            },
        };

        void AddDefinitions(string prefix, string file, IEnumerable<FieldDescriptorProto> extensions)
        {
            foreach (var extension in extensions)
            {
                definitions.Add(new JsonObject
                {
                    ["name"] = prefix + "." + extension.Name,
                    ["file"] = file,
                    ["extendee"] = extension.Extendee,
                    ["number"] = extension.Number,
                    ["type"] = extension.TypeName.Length > 0 ? extension.TypeName : extension.Type.ToString(),
                });
            }
        }
    }
}
