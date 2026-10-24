using System.Text.Json.Nodes;
using static Envoy.ControlPlane.Tools.UpstreamData;

namespace Envoy.ControlPlane.Tools;

internal static class SchemaExporter
{
    internal static void ExportVerifiedInputs(string root, JsonObject inputLock, Dictionary<string, Dictionary<string, byte[]>> sources, JsonObject catalog)
    {
        // Only this tool-owned, ignored build directory is replaced. Never write
        // generated inputs into either source repository's tracked schema tree.
        string output = Path.Combine(root, "src/Envoy.ControlPlane.Protocol/obj/upstream");
        Require(RepositoryPaths.ResolveDirectoryLinks(output) == Path.GetFullPath(output), "Export directory must not contain symlinks");
        if (Directory.Exists(output))
        {
            Directory.Delete(output, recursive: true);
        }
        Directory.CreateDirectory(output);
        var selected = GetArray(catalog, "files").Where(f => GetString(f!, "disposition") is "generate" or "dependency").Select(f => f!).ToList();
        foreach (var entry in selected)
        {
            string path = GetString(entry, "path");
            string target = Path.Combine(output, "inputs", path);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllBytes(target, sources[GetString(entry, "source")][GetString(entry, "sourcePath")]);
        }
        foreach (var source in GetArray(inputLock, "sources"))
        {
            string sourceId = GetString(source!, "id");
            foreach (var notice in GetArray(source!, "notices"))
            {
                string noticePath = GetString(notice!, "path");
                string target = Path.Combine(output, "licenses", sourceId, noticePath);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllBytes(target, sources[sourceId][noticePath]);
            }
        }
        Console.WriteLine($"Exported {selected.Count} verified schema inputs to {output}");
    }
}
