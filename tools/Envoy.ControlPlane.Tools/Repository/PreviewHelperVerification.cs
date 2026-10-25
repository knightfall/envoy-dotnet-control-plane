using System.Text.Json.Nodes;
using static Envoy.ControlPlane.Tools.BindingIntegrity;

namespace Envoy.ControlPlane.Tools;

internal static class PreviewHelperVerification
{
    internal static async Task CompareWithGoAsync()
    {
        const string report = "tests/Envoy.ControlPlane.Tests/bin/Release/net10.0/preview-helper-corpus.json";
        var observed = JsonNode.Parse(File.ReadAllText(report))!;
        var fixtures = JsonNode.Parse(File.ReadAllText("tests/Envoy.ControlPlane.Tests/Fixtures/preview-helper-cases.json"))!.AsArray();
        Require(observed["Cases"]!.AsArray().Select(row => row!["Id"]!.GetValue<string>()).Order(StringComparer.Ordinal)
            .SequenceEqual(fixtures.Select(row => row!["id"]!.GetValue<string>()).Order(StringComparer.Ordinal)),
            "Preview helper report omitted or duplicated a declared case");
        string output = Path.GetFullPath(BindingPaths.InputsDirectory + "/results/helpers/go-preview-results.json");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        await BuildProcess.RunAsync("go", ["run", "-mod=readonly", ".", "helpers", Path.GetFullPath(report), output], "tests/resources/GoOracle");
    }
}
