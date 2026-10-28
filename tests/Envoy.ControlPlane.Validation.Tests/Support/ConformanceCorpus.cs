using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Buf.Validate;
using Buf.Validate.Conformance.Harness;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using ProtoValidate;
using ProtoValidate.Exceptions;

namespace Envoy.ControlPlane.Validation.Tests.Support;

internal static class ConformanceCorpus
{
    private const string ResourcePrefix = "Envoy.ControlPlane.Validation.Tests.";
    internal static IReadOnlyList<FileDescriptor> Files { get; } = typeof(ResultSet).Assembly.GetTypes()
        .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Static))
        .Where(property => property.PropertyType == typeof(FileDescriptor))
        .Select(property => (FileDescriptor)property.GetValue(null)!)
        .Where(file => file.Name.StartsWith("buf/validate/conformance/", StringComparison.Ordinal))
        .OrderBy(file => file.Name, StringComparer.Ordinal).ToArray();

    internal static ConformanceReport Evaluate()
    {
        byte[] data = ReadResource("Conformance.bin");
        var manifest = JsonNode.Parse(ReadResource("ConformanceManifest.json"))!;
        string hash = Convert.ToHexStringLower(SHA256.HashData(data));
        if (hash != manifest["corpus"]!["sha256"]!.GetValue<string>())
        {
            throw new InvalidOperationException("Embedded upstream conformance cases differ from lock");
        }
        var exclusions = manifest["exclusions"]!.AsArray().ToDictionary(
            item => item!["suite"]!.GetValue<string>() + "/" + item["case"]!.GetValue<string>(),
            item => item!["reason"]!.GetValue<string>(), StringComparer.Ordinal);
        var source = ResultSet.Parser.ParseFrom(data);
        var registry = TypeRegistry.FromFiles(Files);
        var validator = new Validator(new ValidatorOptions { FileDescriptors = Files.ToList() });
        var results = new List<ConformanceCaseResult>();
        foreach (var suite in source.Suites)
        {
            foreach (var item in suite.Cases)
            {
                if (item.Wanted is null || item.Wanted.ResultCase == TestResult.ResultOneofCase.None
                    || (item.Wanted.ResultCase == TestResult.ResultOneofCase.Success && !item.Wanted.Success))
                {
                    throw new InvalidOperationException("Missing expected conformance result: " + suite.Name + "/" + item.Name);
                }
                string expected = item.Wanted.ResultCase.ToString();
                string[] wanted = Diagnostics(item.Wanted.ValidationError?.Violations_ ?? []);
                if (exclusions.TryGetValue(suite.Name + "/" + item.Name, out string? reason))
                {
                    results.Add(new(suite.Name, item.Name, "excluded", expected, null, wanted, [], reason));
                    continue;
                }
                string actual;
                string? detail = null;
                string[] observed = [];
                try
                {
                    var result = validator.Validate(item.Input.Unpack(registry), failFast: false);
                    actual = result.IsSuccess ? "Success" : "ValidationError";
                    observed = Diagnostics(result.Violations);
                }
                catch (CompilationException error)
                {
                    actual = "CompilationError";
                    detail = error.Message;
                }
                catch (ExecutionException error)
                {
                    actual = "RuntimeError";
                    detail = error.Message;
                }
                catch (Exception error) when (error is not OutOfMemoryException)
                {
                    actual = "UnexpectedError";
                    detail = error.ToString();
                }
                bool matches = expected == actual && wanted.SequenceEqual(observed, StringComparer.Ordinal);
                results.Add(new(suite.Name, item.Name, matches ? "passed" : "failed", expected, actual, wanted, observed, detail));
            }
        }
        return new(manifest["sourceCommit"]!.GetValue<string>(), hash, source.Suites.Count, results);
    }

    private static string[] Diagnostics(IEnumerable<Violation> violations) => violations.Select(violation =>
        violation.RuleId + "|" + Convert.ToBase64String((violation.Field ?? new FieldPath()).ToByteArray()))
        .Order(StringComparer.Ordinal).ToArray();

    private static byte[] ReadResource(string name)
    {
        using var stream = typeof(ConformanceCorpus).Assembly.GetManifestResourceStream(ResourcePrefix + name)
            ?? throw new InvalidOperationException("Missing upstream conformance resource: " + name);
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return bytes.ToArray();
    }
}

internal sealed record ConformanceCaseResult(string Suite, string Case, string Status, string Expected, string? Actual,
    string[] ExpectedDiagnostics, string[] ActualDiagnostics, string? Detail);

internal sealed record ConformanceReport(string SourceCommit, string CorpusSha256, int Suites, IReadOnlyList<ConformanceCaseResult> Cases);
