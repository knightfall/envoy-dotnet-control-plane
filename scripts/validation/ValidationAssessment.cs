using System.Text.Json.Nodes;
using static Envoy.ControlPlane.Tools.BindingIntegrity;

namespace Envoy.ControlPlane.Tools;

// Historical published-package assessment; not included by the maintained entry point.
internal static class ValidationAssessment
{
    internal const string EvidenceDirectory = "tests/validation/obj/evidence";
    internal const string CorpusPath = "tests/validation/cases.json";
    internal const string LockPath = "schemas/validation/assessment.lock.json";

    internal static async Task RunAsync()
    {
        var proofLock = JsonNode.Parse(File.ReadAllText(LockPath))!;
        Require(GetFileSha256(CorpusPath) == proofLock["corpusSha256"]!.GetValue<string>(), "Validation corpus differs from lock");
        Directory.CreateDirectory(EvidenceDirectory);

        const string inventoryPath = EvidenceDirectory + "/pgv-rules.json";
        await BuildProcess.RunAsync("dotnet", ["run", "--file", "scripts/ValidationInventory.cs", "--", inventoryPath]);
        var inventorySummary = JsonNode.Parse(File.ReadAllText("schemas/validation/rules.summary.json"))!;
        Require(GetFileSha256(inventoryPath) == inventorySummary["fullInventorySha256"]!.GetValue<string>(),
            "Full PGV rule inventory differs from the reviewed summary");

        var candidates = proofLock["packages"]!.AsArray();
        var reports = new List<JsonNode>();
        var fixtures = JsonNode.Parse(File.ReadAllText(CorpusPath))!.AsArray()
            .ToDictionary(row => row!["id"]!.GetValue<string>(), StringComparer.Ordinal);
        foreach (var candidate in candidates)
        {
            string name = candidate!["id"]!.GetValue<string>() switch
            {
                "ProtoValidate" => "ProtoValidate",
                "Celly.Protovalidate" => "Celly",
                _ => throw new InvalidOperationException("Unknown validation candidate"),
            };
            string project = $"tests/validation/{name}.Proof/{name}.Proof.csproj";
            string output = EvidenceDirectory + "/" + name + ".json";
            await BuildProcess.RunAsync("dotnet", ["restore", project, "--locked-mode"]);
            await BuildProcess.RunAsync("dotnet", ["run", "--project", project, "--configuration", "Release", "--no-restore",
                "--", CorpusPath, output]);
            var report = JsonNode.Parse(File.ReadAllText(output))!;
            VerifyCandidateObservations(report, candidate, proofLock["corpusCaseCount"]!.GetValue<int>());
            VerifyFixtureIdentity(report, fixtures);
            reports.Add(report);
        }

        string goVersion = await BuildProcess.RunAsync("go", ["version"]);
        Require(goVersion.StartsWith("go version go" + proofLock["goVersion"]!.GetValue<string>() + " ", StringComparison.Ordinal),
            "Go toolchain differs from the assessment lock");
        string oracleOutput = Path.GetFullPath(EvidenceDirectory + "/go-results.json");
        await BuildProcess.RunAsync("go", ["run", "-mod=readonly", ".",
            Path.GetFullPath(EvidenceDirectory + "/ProtoValidate.json"), oracleOutput], "tests/validation/GoOracle");

        var oracle = JsonNode.Parse(File.ReadAllText(oracleOutput))!.AsArray()
            .ToDictionary(row => row!["id"]!.GetValue<string>(), row => row!["valid"]!.GetValue<bool>(), StringComparer.Ordinal);
        foreach (var report in reports)
        {
            foreach (var row in report["Results"]!.AsArray())
            {
                Require(oracle[row!["Id"]!.GetValue<string>()] == row["ExpectedValid"]!.GetValue<bool>(),
                    "Candidate fixture differs from the independent Go oracle");
            }
        }

        Console.WriteLine("Assessment reproduced: both unmodified candidates accept 15 configurations rejected by Go PGV.");
        Console.WriteLine("G1 remains OPEN. This command verifies recorded findings; it does not pass validation feasibility.");
    }

    private static void VerifyFixtureIdentity(JsonNode report, Dictionary<string, JsonNode?> fixtures)
    {
        var rows = report["Results"]!.AsArray();
        string[] ids = rows.Select(row => row!["Id"]!.GetValue<string>()).Order(StringComparer.Ordinal).ToArray();
        Require(ids.SequenceEqual(fixtures.Keys.Order(StringComparer.Ordinal)), "Candidate omitted or duplicated a fixture");
        foreach (var row in rows)
        {
            var fixture = fixtures[row!["Id"]!.GetValue<string>()]!;
            Require(row["Type"]!.GetValue<string>() == fixture["type"]!.GetValue<string>()
                && row["ExpectedValid"]!.GetValue<bool>() == fixture["valid"]!.GetValue<bool>(),
                "Candidate report changed the independent fixture contract");
        }
    }

    private static void VerifyCandidateObservations(JsonNode report, JsonNode candidate, int expectedCount)
    {
        var rows = report["Results"]!.AsArray();
        Require(rows.Count == expectedCount, "Candidate ran an incomplete corpus");
        Require(rows.All(row => row!["Error"] is null), "Candidate encountered evaluator faults; inspect its report");
        string[] mismatches = rows.Where(row => row!["ActualValid"]!.GetValue<bool>() != row["ExpectedValid"]!.GetValue<bool>())
            .Select(row => row!["Id"]!.GetValue<string>()).Order(StringComparer.Ordinal).ToArray();
        string[] expectedMismatches = candidate["expectedMismatchIds"]!.AsArray()
            .Select(value => value!.GetValue<string>()).Order(StringComparer.Ordinal).ToArray();
        Require(mismatches.SequenceEqual(expectedMismatches), "Candidate behavior changed; reassess the G1 findings");

        string[] duplicates = report["DuplicateMessages"]!.AsArray()
            .Select(row => row!["ProtoName"]!.GetValue<string>()).ToArray();
        string[] expectedDuplicates = candidate["expectedDuplicateMessages"]!.AsArray()
            .Select(value => value!.GetValue<string>()).ToArray();
        Require(duplicates.SequenceEqual(expectedDuplicates), "Candidate descriptor ownership changed; reassess the G1 findings");
    }
}
