using System.Text.Json.Nodes;
using static Envoy.ControlPlane.Tools.BindingIntegrity;

namespace Envoy.ControlPlane.Tools;

internal static class ValidationGeneration
{
    internal static async Task PrepareAsync(string cache, string buf)
    {
        var corpus = JsonNode.Parse(File.ReadAllText(ValidationPaths.SourcesLock))!["corpus"]!;
        string version = await BuildProcess.RunAsync("go", ["version"]);
        Require(version.StartsWith("go version go" + corpus["goVersion"]!.GetValue<string>() + " ", StringComparison.Ordinal),
            "Go generation toolchain differs from lock");
        await VerifiedValidationSources.MaterializeEvaluatorAsync(cache);
        const string inputs = ValidationPaths.AnnotationsRoot + "/inputs";
        Directory.CreateDirectory(inputs + "/google/protobuf");
        foreach (string path in Directory.GetFiles(BindingPaths.InputsDirectory + "/inputs/google/protobuf", "*.proto"))
        {
            File.Copy(path, Path.Combine(inputs, "google/protobuf", Path.GetFileName(path)), overwrite: true);
        }
        File.WriteAllText(inputs + "/buf.yaml", "version: v2\nmodules:\n  - path: .\n");
        await BuildProcess.RunAsync(buf, ["generate", inputs, "--template", "buf.annotations.gen.yaml", "--path", inputs + "/buf/validate"]);
    }

    internal static async Task GenerateAsync(string cache, string buf)
    {
        var migration = JsonNode.Parse(File.ReadAllText(ValidationPaths.SourcesLock))!["migration"]!;
        const string root = ValidationPaths.MetadataRoot;
        string source = await VerifiedValidationSources.RetrieveAsync(cache, migration, root + "/tool-source");
        string tool = Path.GetFullPath(root + (OperatingSystem.IsWindows() ? "/protovalidate-migrate.exe" : "/protovalidate-migrate"));
        await BuildProcess.RunAsync("go", ["build", "-mod=readonly", "-o", tool, "./protovalidate-migrate"], Path.Combine(source, "tools"));
        string inputs = Path.GetFullPath(root + "/inputs");
        if (Directory.Exists(inputs))
        {
            Directory.Delete(inputs, recursive: true);
        }
        await BuildProcess.RunAsync(tool, ["--output", inputs, Path.GetFullPath(BindingPaths.InputsDirectory + "/inputs")]);
        Directory.CreateDirectory(Path.Combine(inputs, "buf/validate"));
        File.Copy(ValidationPaths.AnnotationsRoot + "/inputs/buf/validate/validate.proto", Path.Combine(inputs, "buf/validate/validate.proto"));
        File.WriteAllText(Path.Combine(inputs, "buf.yaml"), "version: v2\nmodules:\n  - path: .\n");
        MigrationCorrections.Apply(inputs);
        string migrated = Path.GetFullPath(root + "/migrated.pb");
        await BuildProcess.RunAsync(buf, ["build", "--as-file-descriptor-set", "--output", migrated], inputs);

        const string descriptor = root + "/catalog.pb";
        await BuildProcess.RunAsync("dotnet", ["run", "--no-cache", "--file", "scripts/ValidationOverlay.cs", "--",
            BindingPaths.InputsDirectory + "/catalog.pb", migrated, descriptor]);
        string generationDescriptor = Path.ChangeExtension(descriptor, "generation.pb");
        await BuildProcess.RunAsync(buf, ["generate", generationDescriptor + "#format=bin", "--template", "buf.validation.gen.yaml",
            "--exclude-path", "google/protobuf", "--exclude-path", "buf/validate"]);
        await BuildProcess.RunAsync("dotnet", ["run", "--no-cache", "--file", "scripts/PatchBindings.cs", "--",
            BindingPaths.GeneratedDirectory, ValidationPaths.BindingsLock]);
        VerifyOutputs();
    }

    internal static void VerifyOutputs()
    {
        var bindingLock = JsonNode.Parse(File.ReadAllText(ValidationPaths.BindingsLock))!;
        foreach (var (path, hash) in bindingLock["configurationSha256"]!.AsObject())
        {
            Require(GetFileSha256(path) == hash!.GetValue<string>(), "Validation generation configuration differs: " + path);
        }
        Require(GetFileSha256(ValidationPaths.MetadataRoot + "/catalog.pb") == bindingLock["descriptorSetSha256"]!.GetValue<string>(),
            "Derived validation descriptors differ from lock");
        BindingGeneration.VerifyGeneratedFiles(bindingLock, "derived Protovalidate metadata");
        VerifyGeneratedSources(ValidationPaths.AnnotationsRoot + "/generated", bindingLock["annotationSourceCount"]!.GetValue<int>(),
            bindingLock["annotationSourcesSha256"]!.GetValue<string>());
    }
}
