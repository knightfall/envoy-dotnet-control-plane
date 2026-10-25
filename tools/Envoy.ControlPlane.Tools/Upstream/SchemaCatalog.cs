using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using static Envoy.ControlPlane.Tools.UpstreamData;

namespace Envoy.ControlPlane.Tools;

internal static partial class SchemaCatalog
{
    internal static JsonObject Create(JsonObject inputLock, Dictionary<string, Dictionary<string, byte[]>> sources)
    {
        var schemasByPath = IndexSchemas(inputLock, sources);
        var mirroredSchemas = FindMirroredSchemas(sources);
        var generationRoots = SelectGenerationRoots(inputLock, mirroredSchemas);
        var selectedSchemas = ResolveImports(schemasByPath, generationRoots);
        return CreateDocument(inputLock, schemasByPath, mirroredSchemas, generationRoots, selectedSchemas);
    }

    [GeneratedRegex("\"(?:\\\\.|[^\"\\\\])*\"|'(?:\\\\.|[^'\\\\])*'|/\\*.*?\\*/|//[^\\n]*", RegexOptions.Singleline)]
    internal static partial Regex CommentTokensPattern();

    [GeneratedRegex(@"\bpackage\s+([\w.]+)\s*;")]
    internal static partial Regex PackagePattern();

    [GeneratedRegex("\\bimport\\s+(?:(?:public|weak)\\s+)?[\"']([^\"']+)[\"']\\s*;")]
    internal static partial Regex ImportsPattern();

    [GeneratedRegex("^// source: (.+)$", RegexOptions.Multiline)]
    internal static partial Regex SourceHeaderPattern();

    [GeneratedRegex("/v3(?:alpha)?/")]
    internal static partial Regex MaintainedVersionPattern();
    private static SortedDictionary<string, JsonObject> IndexSchemas(JsonObject inputLock, Dictionary<string, Dictionary<string, byte[]>> sources)
    {
        var lookup = new SortedDictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (var source in GetArray(inputLock, "sources").Select(s => s!))
        {
            foreach (string root in GetArray(source, "protoRoots").Select(r => r!.GetValue<string>()))
            {
                string prefix = root.Length > 0 ? root + "/" : "";
                foreach (var (path, data) in sources[GetString(source, "id")])
                {
                    if (!path.StartsWith(prefix, StringComparison.Ordinal) || !path.EndsWith(".proto", StringComparison.Ordinal))
                    {
                        continue;
                    }
                    string text = CommentTokensPattern().Replace(DecodeUtf8(data),
                        m => m.Value.StartsWith("//", StringComparison.Ordinal) || m.Value.StartsWith("/*", StringComparison.Ordinal) ? "" : m.Value);
                    var info = new JsonObject
                    {
                        ["source"] = GetString(source, "id"),
                        ["sourcePath"] = path,
                        ["sha256"] = ComputeSha256(data),
                        ["package"] = PackagePattern().Match(text).Groups[1].Value,
                        ["imports"] = ToJsonArray(ImportsPattern().Matches(text).Select(m => m.Groups[1].Value)),
                    };
                    Require(lookup.TryAdd(path[prefix.Length..], info), "Duplicate import owner: " + path[prefix.Length..]);
                }
            }
        }
        return lookup;
    }
    private static HashSet<string> FindMirroredSchemas(Dictionary<string, Dictionary<string, byte[]>> sources)
    {
        var mirrored = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (path, data) in sources["go-control-plane"])
        {
            if (path.StartsWith("envoy/", StringComparison.Ordinal) && path.EndsWith(".pb.go", StringComparison.Ordinal))
            {
                var match = SourceHeaderPattern().Match(DecodeUtf8(data));
                if (match.Success)
                {
                    mirrored.Add(match.Groups[1].Value);
                }
            }
        }
        return mirrored;
    }
    private static HashSet<string> SelectGenerationRoots(JsonObject inputLock, HashSet<string> mirrored)
    {
        var seeds = mirrored.Where(p => MaintainedVersionPattern().IsMatch(p) || p.StartsWith("envoy/annotations/", StringComparison.Ordinal)).ToHashSet(StringComparer.Ordinal);
        seeds.UnionWith(GetArray(inputLock["ratelimit"]!, "paths").Select(p => p!.GetValue<string>()));
        return seeds;
    }
    private static HashSet<string> ResolveImports(SortedDictionary<string, JsonObject> lookup, HashSet<string> seeds)
    {
        var selected = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>(seeds);
        while (pending.TryPop(out string? path))
        {
            if (!selected.Add(path))
            {
                continue;
            }
            Require(lookup.ContainsKey(path), "Unresolved import: " + path);
            foreach (string dependency in GetArray(lookup[path], "imports").Select(p => p!.GetValue<string>()))
            {
                pending.Push(dependency);
            }
        }
        return selected;
    }
    private static JsonObject CreateDocument(JsonObject inputLock, SortedDictionary<string, JsonObject> lookup, HashSet<string> mirrored, HashSet<string> seeds, HashSet<string> selected)
    {
        var records = new JsonArray();
        foreach (var (path, info) in lookup)
        {
            if (GetString(info, "source") != "envoy" && !selected.Contains(path))
            {
                continue;
            }
            string disposition, reason;
            if (seeds.Contains(path))
            {
                (disposition, reason) = ("generate", "Selected maintained standard catalog or rate-limit configuration");
            }
            else if (selected.Contains(path))
            {
                (disposition, reason) = ("dependency", "Transitive import of a selected schema");
            }
            else if (path.StartsWith("contrib/", StringComparison.Ordinal))
            {
                (disposition, reason) = ("deferred", "T28: broad contrib catalog requires a separate scope decision");
            }
            else if (mirrored.Contains(path))
            {
                (disposition, reason) = ("deferred", "T28: legacy catalog requires a separate scope decision");
            }
            else
            {
                (disposition, reason) = ("excluded", "Outside the selected mirrored standard catalog");
            }
            string category;
            if (path.StartsWith("contrib/", StringComparison.Ordinal))
            {
                category = "contrib";
            }
            else if (disposition == "deferred")
            {
                category = "legacy";
            }
            else if (GetString(info, "source") == "ratelimit")
            {
                category = "ratelimit-configuration";
            }
            else if (path.Contains("annotations/", StringComparison.Ordinal) || path == "validate/validate.proto")
            {
                category = "annotations";
            }
            else if (GetString(info, "source") != "envoy")
            {
                category = "external-import";
            }
            else
            {
                category = "standard";
            }
            var record = new JsonObject { ["path"] = path };
            foreach (var entry in info)
            {
                record.Add(entry.Key, entry.Value?.DeepClone());
            }
            record.Add("category", category); record.Add("disposition", disposition); record.Add("reason", reason);
            records.Add(record);
        }
        var selectedCounts = new JsonObject();
        foreach (var group in selected.GroupBy(p => GetString(lookup[p], "source")).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            selectedCounts.Add(group.Key, group.Count());
        }
        return new JsonObject
        {
            ["schemaVersion"] = 1,
            ["selection"] = inputLock["selection"]!.DeepClone(),
            ["counts"] = new JsonObject { ["roots"] = seeds.Count, ["selected"] = selected.Count, ["inventory"] = records.Count, ["selectedBySource"] = selectedCounts },
            ["customExtensions"] = new JsonObject
            {
                ["selectedInputs"] = new JsonArray(),
                ["policy"] = "Application-supplied descriptors/Any payloads are supported by T05. No third-party custom schema is a release generation input until explicitly locked.",
            },
            ["files"] = records,
        };

    }
}
