using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using static Envoy.ControlPlane.Tools.UpstreamData;

namespace Envoy.ControlPlane.Tools;

internal static partial class GoModuleInventory
{
    internal static JsonArray Create(Dictionary<string, byte[]> files)
    {
        var result = new JsonArray();
        foreach (var (path, data) in files.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            if (Path.GetFileName(path) != "go.mod")
            {
                continue;
            }
            string text = DecodeUtf8(data);
            var blocks = new Dictionary<string, List<string>> { ["require"] = [], ["replace"] = [] };
            string? active = null;
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Split("//", 2)[0].Trim();
                if (line is "require (" or "replace (")
                {
                    active = line.Split(' ')[0];
                }
                else if (line == ")")
                {
                    active = null;
                }
                else if (active is not null && line.Length > 0)
                {
                    blocks[active].Add(line);
                }
                else if (line.StartsWith("require ", StringComparison.Ordinal) || line.StartsWith("replace ", StringComparison.Ordinal))
                {
                    blocks[line.Split(' ', 2)[0]].Add(line.Split(' ', 2)[1]);
                }
            }
            string sumPath = path[..^6] + "go.sum";
            result.Add(new JsonObject
            {
                ["path"] = path,
                ["sha256"] = ComputeSha256(data),
                ["module"] = ModulePattern().Match(text).Groups[1].Value,
                ["go"] = GoVersionPattern().Match(text).Groups[1].Value,
                ["requirements"] = ToJsonArray(blocks["require"]),
                ["replacements"] = ToJsonArray(blocks["replace"]),
                ["sumPath"] = sumPath,
                ["sumSha256"] = files.TryGetValue(sumPath, out var sum) ? ComputeSha256(sum) : null,
            });
        }
        return result;
    }

    [GeneratedRegex(@"^module\s+(\S+)", RegexOptions.Multiline)]
    internal static partial Regex ModulePattern();

    [GeneratedRegex(@"^go\s+(\S+)", RegexOptions.Multiline)]
    internal static partial Regex GoVersionPattern();
}
