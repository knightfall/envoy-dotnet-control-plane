using System.Reflection;
using System.Text.Json;
using Envoy.Config.Core.V3;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using ProtoValidate;

namespace Envoy.ControlPlane.Validation.Tests.Support;

internal static class ValidationCorpus
{
    private const string ResourceName = "Envoy.ControlPlane.Validation.Tests.PgvCases.json";
    internal static IReadOnlyList<FileDescriptor> Files { get; } = LoadFiles();
    private static readonly TypeRegistry Registry = TypeRegistry.FromFiles(Files);

    internal static ValidationCaseResult[] Evaluate(Validator validator)
    {
        var results = new List<ValidationCaseResult>();
        foreach (var (item, message) in Read())
        {
            var result = validator.Validate(message, failFast: false);
            results.Add(new ValidationCaseResult(item.Id, item.Type, item.Valid,
                result.IsSuccess, result.Violations.Count, Convert.ToBase64String(message.ToByteArray())));
        }
        return results.ToArray();
    }

    private static IEnumerable<(ValidationCase Case, IMessage Message)> Read()
    {
        using var stream = typeof(ValidationCorpus).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("Missing embedded validation corpus: " + ResourceName);
        var cases = JsonSerializer.Deserialize<ValidationCase[]>(stream,
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

internal sealed record ValidationCase(string Id, string Type, JsonElement Input, bool Valid);

internal sealed record ValidationCaseResult(
    string Id, string Type, bool ExpectedValid, bool ActualValid, int Violations, string Data);
