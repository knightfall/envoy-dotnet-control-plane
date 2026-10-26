using System.Text.Json.Nodes;
using static Envoy.ControlPlane.Tools.BindingIntegrity;

namespace Envoy.ControlPlane.Tools;

internal static class ConversionVerification
{
    internal static async Task CompareWithGoAsync()
    {
        const string report = "tests/Envoy.ControlPlane.Tests/bin/Release/net10.0/conversion-corpus.json";
        var root = JsonNode.Parse(File.ReadAllText(report))!;
        var rows = root["cases"]!.AsArray();
        var symbols = root["symbols"]!.AsArray();
        Require(rows.Count == 17 && rows.Select(row => row!["input"]!["id"]!.GetValue<string>()).Distinct(StringComparer.Ordinal).Count() == 17,
            "Conversion report omitted or duplicated a case");
        Require(symbols.Count == 74 && symbols.Select(row => row!["source"]!.GetValue<string>()).Distinct(StringComparer.Ordinal).Count() == 74,
            "Helper report omitted or duplicated a symbol");
        string output = Path.GetFullPath(BindingPaths.InputsDirectory + "/results/conversions/go-results.json");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        await BuildProcess.RunAsync("go", ["run", "-mod=readonly", ".", "conversions", Path.GetFullPath(report), output], "tests/resources/GoOracle");
    }
}
