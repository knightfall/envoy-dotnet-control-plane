using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Google.Protobuf.Reflection;
using ProtoValidate;
using Pgv = Validate.ValidateExtensions;

namespace Envoy.ControlPlane.Validation.Tests.Support;

internal static class PgvConformanceCorpus
{
    private const string Prefix = "Envoy.ControlPlane.Validation.Tests.";
    private static readonly Lazy<PgvConformanceReport> Cached = new(Evaluate);
    internal static PgvConformanceReport Report => Cached.Value;

    private static PgvConformanceReport Evaluate()
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var input = JsonSerializer.Deserialize<PgvInputCase[]>(Read("PgvUpstreamCases.json"), options)
            ?? throw new InvalidOperationException("Missing upstream PGV fixture cases");
        using var manifest = JsonDocument.Parse(Read("PgvUpstreamManifest.json"));
        using var rules = JsonDocument.Parse(Read("PgvRecognizedRules.json"));
        var recognized = rules.RootElement.GetProperty("recognizedRulePaths").EnumerateArray()
            .Select(value => value.GetString()!).ToHashSet(StringComparer.Ordinal);
        var files = typeof(PgvConformanceCorpus).Assembly.GetTypes()
            .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Static))
            .Where(property => property.PropertyType == typeof(FileDescriptor))
            .Select(property => (FileDescriptor)property.GetValue(null)!)
            .Where(file => file.Name.StartsWith("tests/harness/cases/", StringComparison.Ordinal))
            .ToArray();
        var registry = TypeRegistry.FromFiles(files);
        var validator = new Validator();
        var results = new List<PgvConformanceCase>();
        foreach (var item in input)
        {
            var descriptor = registry.Find(item.Type)
                ?? throw new InvalidOperationException("Unknown upstream PGV fixture type: " + item.Type);
            var unsupported = FindUnsupported(descriptor, recognized);
            bool? actual = null;
            string? error = null;
            string status = "excluded";
            if (unsupported.Length == 0)
            {
                try
                {
                    var message = descriptor.Parser.ParseFrom(Convert.FromBase64String(item.Data));
                    actual = validator.Validate(message, failFast: false).IsSuccess;
                    status = actual == item.ExpectedValid ? "passed" : "failed";
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    status = "failed";
                    error = exception.GetType().Name + ": " + exception.Message;
                }
            }
            results.Add(new PgvConformanceCase(item.Id, item.Name, item.Type, item.ExpectedValid, actual, status, unsupported, error));
        }
        string[] selected = results.Where(item => item.UnsupportedRules.Length == 0).Select(item => item.Id).Order(StringComparer.Ordinal).ToArray();
        string selectionHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', selected))));
        var root = manifest.RootElement;
        return new PgvConformanceReport(root.GetProperty("sourceCommit").GetString()!,
            root.GetProperty("selectedCaseCount").GetInt32(), root.GetProperty("selectedIdsSha256").GetString()!, selectionHash, results);
    }

    private static string[] FindUnsupported(MessageDescriptor root, IReadOnlySet<string> recognized)
    {
        var paths = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        Visit(root);
        return paths.Except(recognized, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

        void Visit(MessageDescriptor message)
        {
            if (!visited.Add(message.FullName))
            {
                return;
            }
            var options = message.GetOptions();
            if (options?.GetExtension(Pgv.Disabled) == true)
            {
                paths.Add("message.disabled");
            }
            if (options?.GetExtension(Pgv.Ignored) == true)
            {
                paths.Add("message.ignored");
            }
            foreach (var field in message.Fields.InFieldNumberOrder())
            {
                var fieldRules = field.GetOptions()?.GetExtension(Pgv.Rules);
                if (fieldRules is not null)
                {
                    foreach (string path in Tools.PgvRuleInventory.EnumerateRulePaths(fieldRules, ""))
                    {
                        paths.Add(path);
                    }
                }
                if (field.FieldType == FieldType.Message
                    && field.MessageType.File.Package is not "google.protobuf" and not "validate" and not "buf.validate")
                {
                    Visit(field.MessageType);
                }
            }
        }
    }

    private static byte[] Read(string resource)
    {
        using var stream = typeof(PgvConformanceCorpus).Assembly.GetManifestResourceStream(Prefix + resource)
            ?? throw new InvalidOperationException("Missing PGV conformance resource: " + resource);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}

internal sealed record PgvInputCase(string Id, string Name, string Type, string Data, bool ExpectedValid);
internal sealed record PgvConformanceCase(string Id, string Name, string Type, bool ExpectedValid, bool? ActualValid,
    string Status, string[] UnsupportedRules, string? Error);
internal sealed record PgvConformanceReport(string SourceCommit, int ExpectedSelected, string ExpectedSelectionHash,
    string ActualSelectionHash, List<PgvConformanceCase> Cases);
