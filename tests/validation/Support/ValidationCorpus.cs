using System.Reflection;
using System.Text.Json;
using Envoy.Config.Core.V3;
using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace Envoy.ControlPlane.Validation.Proof;

internal static class ValidationCorpus
{
    internal static readonly FileDescriptor[] Files = LoadFiles();
    internal static readonly TypeRegistry Registry = TypeRegistry.FromFiles(Files);

    internal static IEnumerable<(ValidationCase Case, IMessage Message)> Read(string path)
    {
        var cases = JsonSerializer.Deserialize<ValidationCase[]>(File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("Missing validation cases");
        var parser = new JsonParser(JsonParser.Settings.Default.WithTypeRegistry(Registry));
        foreach (var item in cases)
        {
            var descriptor = Registry.Find(item.Type)
                ?? throw new InvalidOperationException("Unknown corpus type: " + item.Type);
            yield return (item, parser.Parse(item.Input.GetRawText(), descriptor));
        }
    }

    private static FileDescriptor[] LoadFiles()
    {
        return typeof(Address).Assembly.GetTypes()
            .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Static))
            .Where(property => property.PropertyType == typeof(FileDescriptor))
            .Select(property => (FileDescriptor)property.GetValue(null)!)
            .OrderBy(file => file.Name, StringComparer.Ordinal)
            .ToArray();
    }
}

internal sealed record ValidationCase(string Id, string Category, string Type, JsonElement Input, bool Valid);
