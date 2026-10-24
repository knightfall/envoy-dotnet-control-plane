using System.Text.Json.Nodes;
using static Envoy.ControlPlane.Tools.BindingPaths;
using static Envoy.ControlPlane.Tools.BindingIntegrity;

namespace Envoy.ControlPlane.Tools;

internal static class BindingGeneration
{
    internal static async Task GenerateAsync(string sourceCache)
    {
        var bindingLock = JsonNode.Parse(File.ReadAllText("schemas/bindings.lock.json"))!;
        VerifyConfiguration(bindingLock);

        // Export validates T01 provenance before replacing its own build inputs.
        await BuildProcess.RunAsync("dotnet", ["run", "--no-cache", "--file", "scripts/Upstream.cs", "--", "export", "--cache", sourceCache]);

        string buf = await BufTool.GetExecutableAsync(sourceCache, bindingLock["buf"]!);
        await BuildProcess.RunAsync(buf, ["build", "--as-file-descriptor-set", "--exclude-source-info",
            "--output", InputsDirectory + "/catalog.pb"]);
        Require(GetFileSha256(InputsDirectory + "/catalog.pb") == bindingLock["descriptorSetSha256"]!.GetValue<string>(),
            "Buf source descriptor set differs from lock");

        await ValidationGeneration.PrepareAsync(sourceCache, buf);
        await BuildProcess.RunAsync(buf, ["generate", "--template", "buf.gen.yaml"]);
        await BuildProcess.RunAsync("dotnet", ["run", "--no-cache", "--file", "scripts/PatchBindings.cs", "--", GeneratedDirectory]);
        VerifyGeneratedFiles(bindingLock);
        await ValidationGeneration.GenerateAsync(sourceCache, buf);
    }

    internal static void VerifyConfiguration(JsonNode bindingLock)
    {
        foreach (var (path, hash) in bindingLock["configurationSha256"]!.AsObject())
        {
            Require(GetFileSha256(path) == hash!.GetValue<string>(),
                "Generation configuration differs from lock: " + path);
        }
    }

    internal static void VerifyGeneratedFiles(JsonNode bindingLock, string description = "original PGV schema baseline")
    {
        int count = VerifyGeneratedSources(GeneratedDirectory, bindingLock["generatedSourceCount"]!.GetValue<int>(),
            bindingLock["generatedSourcesSha256"]!.GetValue<string>());
        Console.WriteLine($"Buf generated {count} verified C# files ({description}).");
    }
}
