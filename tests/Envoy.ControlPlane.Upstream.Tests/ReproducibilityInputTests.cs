using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Envoy.ControlPlane.Tools;
using Xunit;

namespace Envoy.ControlPlane.Upstream.Tests;

public sealed class ReproducibilityInputTests
{
    [Fact]
    public void MaintainedInputs_RecordExactNormalizedHashesAndExcludeBuildOutputs()
    {
        using var fixture = new UpstreamFixture();
        string[] paths = ["src/Runtime.cs", "src/Runtime.csproj", "src/Directory.Build.props", "src/Build.targets",
            "src/NestedZ.cs", "src/Nested/Source.cs", "tests/Case.cs", "tests/resources/corpus.json",
            "tests/resources/GoOracle/main.go", "tests/resources/GoOracle/go.mod", "tests/resources/GoOracle/go.sum",
            "tests/consumers/Core/Consumer.csproj.template", "tests/consumers/AspNetCore/Consumer.csproj.template",
            "tools/Tool.cs", "examples/Example.cs", ".github/workflows/check.yml"];
        foreach (string path in paths) Write(path, "first\r\nsecond\r\n");
        Write("src/obj/Generated.cs", "generated");
        Write("tests/bin/result.json", "output");
        Write("tools/obj/Nested/Generated.cs", "generated");
        Write("tests/resources/envoy-images.lock.json", "{}");
        var imagePin = new JsonObject { ["version"] = "structured-lock" };
        var inputs = new JsonObject { ["tests/resources/envoy-images.lock.json"] = imagePin };

        ReproducibilityReport.RecordMaintainedInputs(inputs, fixture.Root);

        Assert.Equal(paths.Append("tests/resources/envoy-images.lock.json").Order(StringComparer.Ordinal), inputs.Select(pair => pair.Key).Order(StringComparer.Ordinal));
        string expected = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("first\nsecond\n")));
        Assert.All(paths, path => Assert.Equal(expected, inputs[path]!.GetValue<string>()));
        Assert.Equal(new[] { "src/Build.targets", "src/Directory.Build.props", "src/Nested/Source.cs", "src/NestedZ.cs", "src/Runtime.cs", "src/Runtime.csproj" },
            inputs.Select(pair => pair.Key).Where(path => path.StartsWith("src/", StringComparison.Ordinal)));
        Assert.Same(imagePin, inputs["tests/resources/envoy-images.lock.json"]);
        Write("src/Runtime.cs", "first\nsecond\n");
        var unix = new JsonObject();
        ReproducibilityReport.RecordMaintainedInputs(unix, fixture.Root);
        Assert.Equal(expected, unix["src/Runtime.cs"]!.GetValue<string>());
        Write("src/Runtime.cs", "changed\n");
        var changed = new JsonObject();
        ReproducibilityReport.RecordMaintainedInputs(changed, fixture.Root);
        string changedHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("changed\n")));
        Assert.Equal(changedHash, changed["src/Runtime.cs"]!.GetValue<string>());
        Write("tests/consumers/Core/Consumer.csproj.template", "changed\n");
        var changedConsumer = new JsonObject();
        ReproducibilityReport.RecordMaintainedInputs(changedConsumer, fixture.Root);
        Assert.Equal(changedHash, changedConsumer["tests/consumers/Core/Consumer.csproj.template"]!.GetValue<string>());

        void Write(string path, string contents)
        {
            string file = Path.Combine(fixture.Root, path);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, contents);
        }
    }
}
