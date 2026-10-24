#!/usr/bin/env -S dotnet --
#:property PublishAot=false
#:property TreatWarningsAsErrors=true
#:package Roslynator.Analyzers

using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Envoy.ControlPlane.Tools;

internal static class PatchBindings
{
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length != 1)
            {
                throw new ArgumentException("Expected the generated C# output directory");
            }

            var bindingLock = JsonNode.Parse(File.ReadAllText("schemas/bindings.lock.json"))!;
            foreach (var correction in bindingLock["generatedCodeCorrections"]!.AsArray())
            {
                string path = Path.Combine(args[0], correction!["path"]!.GetValue<string>());
                string source = File.ReadAllText(path).ReplaceLineEndings("\n");
                string sourceHash = Sha(source);
                string expectedOutputHash = correction["outputSha256"]!.GetValue<string>();

                if (sourceHash == expectedOutputHash)
                {
                    continue;
                }
                if (sourceHash != correction["inputSha256"]!.GetValue<string>())
                {
                    throw new InvalidOperationException("Unrecognized generated source; review the compiler/input change: " + path);
                }

                foreach (var edit in correction["replacements"]!.AsArray())
                {
                    string before = edit!["before"]!.GetValue<string>();
                    int occurrences = source.Split(before, StringSplitOptions.None).Length - 1;
                    if (occurrences != edit["occurrences"]!.GetValue<int>())
                    {
                        throw new InvalidOperationException("Generated-code replacement count differs: " + path);
                    }
                    source = source.Replace(before, edit["after"]!.GetValue<string>(), StringComparison.Ordinal);
                }

                if (Sha(source) != expectedOutputHash)
                {
                    throw new InvalidOperationException("Corrected generated-code fingerprint differs: " + path);
                }
                File.WriteAllText(path, source);
            }
            return 0;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            Console.Error.WriteLine(error.Message);
            return 1;
        }
    }

    private static string Sha(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
