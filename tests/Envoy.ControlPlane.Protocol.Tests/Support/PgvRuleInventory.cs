using System.Collections;
using System.Text.Json.Nodes;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Validate;

namespace Envoy.ControlPlane.Protocol.Tests;

internal static class PgvRuleInventory
{
    internal static JsonObject Create(FileDescriptorSet source)
    {
        var fields = new JsonArray();
        var oneofs = new JsonArray();
        var messageOptions = new JsonArray();
        var anyFields = new JsonArray();
        var ruleCounts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        int messageCount = 0;

        foreach (var file in source.File.OrderBy(file => file.Name, StringComparer.Ordinal))
        {
            foreach (var (name, message) in EnumerateMessages(file.Package, file.MessageType))
            {
                messageCount++;
                AddMessageOption(name, message.Options, ValidateExtensions.Disabled, "disabled");
                AddMessageOption(name, message.Options, ValidateExtensions.Ignored, "ignored");

                foreach (var field in message.Field)
                {
                    string fieldName = name + "." + field.Name;
                    if (field.TypeName == ".google.protobuf.Any")
                    {
                        anyFields.Add(fieldName);
                    }
                    if (field.Options?.HasExtension(ValidateExtensions.Rules) != true)
                    {
                        continue;
                    }

                    var rules = field.Options.GetExtension(ValidateExtensions.Rules);
                    var paths = new JsonArray();
                    foreach (string path in EnumerateRulePaths(rules, ""))
                    {
                        paths.Add(path);
                        ruleCounts[path] = ruleCounts.GetValueOrDefault(path) + 1;
                    }
                    fields.Add(new JsonObject
                    {
                        ["field"] = fieldName,
                        ["file"] = file.Name,
                        ["rulePaths"] = paths,
                        ["rules"] = JsonNode.Parse(JsonFormatter.Default.Format(rules)),
                    });
                }

                foreach (var oneof in message.OneofDecl)
                {
                    if (oneof.Options?.HasExtension(ValidateExtensions.Required) == true)
                    {
                        oneofs.Add(new JsonObject
                        {
                            ["oneof"] = name + "." + oneof.Name,
                            ["required"] = oneof.Options.GetExtension(ValidateExtensions.Required),
                        });
                    }
                }
            }
        }

        var counts = new JsonObject();
        foreach (var (path, count) in ruleCounts)
        {
            counts.Add(path, count);
        }
        return new JsonObject
        {
            ["schemaVersion"] = 1,
            ["fileCount"] = source.File.Count,
            ["messageCount"] = messageCount,
            ["fieldRuleCount"] = fields.Count,
            ["oneofOptionCount"] = oneofs.Count,
            ["messageOptions"] = messageOptions,
            ["rulePathCounts"] = counts,
            ["fields"] = fields,
            ["oneofs"] = oneofs,
            ["anyFields"] = anyFields,
        };

        void AddMessageOption(string name, MessageOptions? options, Extension<MessageOptions, bool> extension, string option)
        {
            if (options?.HasExtension(extension) == true)
            {
                messageOptions.Add(new JsonObject
                {
                    ["message"] = name,
                    ["option"] = option,
                    ["value"] = options.GetExtension(extension),
                });
            }
        }
    }

    private static IEnumerable<string> EnumerateRulePaths(IMessage rules, string prefix)
    {
        foreach (var field in rules.Descriptor.Fields.InFieldNumberOrder())
        {
            object value = field.Accessor.GetValue(rules);
            bool populated = field.IsRepeated ? ((IList)value).Count > 0 : field.Accessor.HasValue(rules);
            if (!populated)
            {
                continue;
            }

            string path = prefix + field.Name;
            if (value is IMessage nested && nested.Descriptor.File.Package == "validate")
            {
                // Retain empty message-valued rules such as duration.gt = {}.
                string[] children = EnumerateRulePaths(nested, path + ".").ToArray();
                foreach (string child in children.Length == 0 ? [path] : children)
                {
                    yield return child;
                }
            }
            else
            {
                yield return path;
            }
        }
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
