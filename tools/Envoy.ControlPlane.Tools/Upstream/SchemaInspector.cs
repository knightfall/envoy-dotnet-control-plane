using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using static Envoy.ControlPlane.Tools.UpstreamData;

namespace Envoy.ControlPlane.Tools;

internal static partial class SchemaInspector
{
    internal static async Task InspectAsync(JsonObject inputLock, Dictionary<string, Dictionary<string, byte[]>> sources, JsonObject catalog, string cache, bool offline)
    {
        if (!OperatingSystem.IsMacOS() || System.Runtime.InteropServices.RuntimeInformation.OSArchitecture != System.Runtime.InteropServices.Architecture.Arm64)
        {
            throw new InvalidOperationException("The T01 inspection compiler is pinned for macOS arm64");
        }
        JsonNode protobuf = inputLock["inspectionTools"]!["googleProtobuf"]!;
        await SourceArchives.FetchArtifactAsync(protobuf, Path.Combine(cache, "tools", new Uri(GetString(protobuf, "url")).Segments[^1]), offline);
        JsonNode roslynator = inputLock["inspectionTools"]!["roslynatorAnalyzers"]!;
        await SourceArchives.FetchArtifactAsync(roslynator, Path.Combine(cache, "tools", new Uri(GetString(roslynator, "url")).Segments[^1]), offline);
        Require(ComputeSha256(await File.ReadAllBytesAsync(typeof(IMessage).Assembly.Location)) == GetString(protobuf, "assemblySha256"),
                "The loaded Google.Protobuf assembly differs from the locked package");
        JsonNode tool = inputLock["inspectionTools"]!["protoc"]!;
        string archive = Path.Combine(cache, "tools", new Uri(GetString(tool, "url")).Segments[^1]);
        await SourceArchives.FetchArtifactAsync(tool, archive, offline);
        string working = Path.Combine(cache, "inspection-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(working);
        try
        {
            ZipFile.ExtractToDirectory(archive, Path.Combine(working, "protoc"));
            string protoc = Path.Combine(working, "protoc/bin/protoc");
            File.SetUnixFileMode(protoc, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            string version = await RunCompilerAsync(protoc, ["--version"]);
            Require(version.Trim() == "libprotoc " + GetString(tool, "version"), "Unexpected protoc");
            var selected = GetArray(catalog, "files").Where(f => GetString(f!, "disposition") is "generate" or "dependency").Select(f => f!).ToList();
            string inputs = Path.Combine(working, "inputs"), output = Path.Combine(working, "catalog.pb");
            foreach (var entry in selected)
            {
                string target = Path.Combine(inputs, GetString(entry, "path"));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await File.WriteAllBytesAsync(target, sources[GetString(entry, "source")][GetString(entry, "sourcePath")]);
            }
            await RunCompilerAsync(protoc, ["-I" + inputs, "--include_imports", "--descriptor_set_out=" + output, .. selected.Select(e => GetString(e, "path"))]);
            var descriptors = FileDescriptorSet.Parser.ParseFrom(await File.ReadAllBytesAsync(output));
            var compiled = descriptors.File.ToDictionary(f => f.Name, StringComparer.Ordinal);
            Require(compiled.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(selected.Select(e => GetString(e, "path"))), "Descriptor closure differs from catalog");
            Console.WriteLine(version.Trim() + "; Google.Protobuf " + GetString(inputLock["inspectionTools"]!["googleProtobuf"]!, "version"));
            Console.WriteLine($"Compiled {compiled.Count} locked schemas with original options.");
            JsonNode ratelimit = inputLock["ratelimit"]!;
            foreach (string path in GetArray(ratelimit, "paths").Select(p => p!.GetValue<string>()))
            {
                string generated = DecodeUtf8(sources["go-control-plane"][path.Replace(".proto", ".pb.go", StringComparison.Ordinal)]);
                var match = RawDescriptorPattern().Match(generated);
                Require(match.Success, "Unsupported Go raw descriptor encoding");
                byte[] raw = HexBytePattern().Matches(match.Groups[1].Value).Select(m => Convert.ToByte(m.Groups[1].Value, 16)).ToArray();
                Require(ComputeSha256(raw) == GetString(ratelimit["goDescriptorSha256"]!, path), "Go descriptor fingerprint differs from lock");
                var reference = FileDescriptorProto.Parser.ParseFrom(raw);
                string originalName = reference.Name;
                Require(originalName == GetString(ratelimit["goDescriptorNames"]!, path), "Unrecorded descriptor filename difference");
                reference.Name = path; // The sole explicitly locked normalization.
                Require(reference.Equals(compiled[path]), "Rate-limit schema definition mismatch: " + path);
                Console.WriteLine(path + ": definitions match; Go filename=" + originalName);
            }
        }
        finally { Directory.Delete(working, recursive: true); }
    }

    internal static async Task<string> RunCompilerAsync(string executable, IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo(executable) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start " + executable);
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        string error = await stderr;
        Require(process.ExitCode == 0, executable + " failed: " + error);
        if (error.Length > 0)
        {
            Console.Error.Write(error);
        }
        return await stdout;
    }

    [GeneratedRegex(@"var \w+_rawDesc = \[\]byte\{(.*?)\n\}", RegexOptions.Singleline)]
    internal static partial Regex RawDescriptorPattern();

    [GeneratedRegex("0x([0-9a-fA-F]{2})")]
    internal static partial Regex HexBytePattern();
}
