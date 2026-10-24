using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Envoy.ControlPlane.Tools;
using Xunit;

namespace Envoy.ControlPlane.Validation.Tests;

public sealed class SourceIntegrityTests
{
    [Theory]
    [InlineData("original", true)]
    [InlineData("changed", false)]
    [InlineData("extra", false)]
    [InlineData("missing", false)]
    public void MaterializedSource_ExactInventoryAndContent_AreRequired(string scenario, bool accepted)
    {
        string directory = Path.Combine(Path.GetTempPath(), "envoy-source-integrity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            const string source = "class UpstreamFixture {}\n";
            File.WriteAllText(Path.Combine(directory, "Validator.cs"), source);
            var manifest = new JsonObject
            {
                ["csharpSources"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["path"] = "Validator.cs",
                        ["adaptedSha256"] = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(source))),
                    },
                },
            };
            switch (scenario)
            {
                case "changed":
                    File.WriteAllText(Path.Combine(directory, "Validator.cs"), source + "// changed\n");
                    break;
                case "extra":
                    File.WriteAllText(Path.Combine(directory, "Unexpected.cs"), source);
                    break;
                case "missing":
                    File.Delete(Path.Combine(directory, "Validator.cs"));
                    break;
            }

            if (accepted)
            {
                VerifiedValidationSources.VerifyMaterialized(directory, manifest);
                Assert.Equal(source, File.ReadAllText(Path.Combine(directory, "Validator.cs")));
            }
            else
            {
                var error = Assert.Throws<InvalidOperationException>(() => VerifiedValidationSources.VerifyMaterialized(directory, manifest));
                Assert.Contains(scenario == "changed" ? "source differs: Validator.cs" : "inventory differs", error.Message);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
