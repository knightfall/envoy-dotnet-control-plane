using System.Text.Json;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Validate;

namespace Envoy.ControlPlane.Tools;

internal static class ValidationInventory
{
    internal static void Write(string output)
    {
        var registry = new ExtensionRegistry
        {
            ValidateExtensions.Rules, ValidateExtensions.Required,
            ValidateExtensions.Disabled, ValidateExtensions.Ignored,
        };
        var assembly = typeof(FieldRules).Assembly;
        using var stream = assembly.GetManifestResourceStream("Envoy.ControlPlane.Protocol.catalog.pb")
            ?? throw new InvalidOperationException("Protocol has no source descriptor set");
        var source = FileDescriptorSet.Parser.WithExtensionRegistry(registry).ParseFrom(stream);
        var inventory = PgvRuleInventory.Create(source);
        File.WriteAllText(output, inventory.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
        Console.WriteLine($"Inventoried {inventory["fieldRuleCount"]} annotated fields across {inventory["fileCount"]} schemas.");
    }
}
