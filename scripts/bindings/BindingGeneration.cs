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
        await BuildProcess.RunAsync("dotnet", ["run", "--file", "scripts/Upstream.cs", "--", "export", "--cache", sourceCache]);

        string buf = await BufTool.GetExecutableAsync(sourceCache, bindingLock["buf"]!);
        await BuildProcess.RunAsync(buf, ["build", "--as-file-descriptor-set", "--exclude-source-info",
            "--output", InputsDirectory + "/catalog.pb"]);
        Require(GetFileSha256(InputsDirectory + "/catalog.pb") == bindingLock["descriptorSetSha256"]!.GetValue<string>(),
            "Buf source descriptor set differs from lock");

        await BuildProcess.RunAsync(buf, ["generate", "--template", "buf.gen.yaml"]);
        await BuildProcess.RunAsync("dotnet", ["run", "--file", "scripts/PatchBindings.cs", "--", GeneratedDirectory]);
        VerifyGeneratedFiles(bindingLock);
    }

    internal static void VerifyConfiguration(JsonNode bindingLock)
    {
        foreach (var (path, hash) in bindingLock["configurationSha256"]!.AsObject())
        {
            Require(GetFileSha256(path) == hash!.GetValue<string>(),
                "Generation configuration differs from lock: " + path);
        }
    }

    internal static void VerifyGeneratedFiles(JsonNode bindingLock)
    {
        string[] files = Directory.GetFiles(GeneratedDirectory, "*.cs", SearchOption.AllDirectories);
        var sources = files.Select(path =>
            (Path.GetRelativePath(GeneratedDirectory, path), File.ReadAllText(path)));
        string fingerprint = GetGeneratedSourcesSha256(sources);

        Require(files.Length == bindingLock["generatedSourceCount"]!.GetValue<int>(), "Generated file count differs");
        Require(fingerprint == bindingLock["generatedSourcesSha256"]!.GetValue<string>(), "Generated source fingerprint differs");
        Console.WriteLine($"Buf generated {files.Length} verified C# files; original schemas are unchanged.");
    }
}
