using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using static Envoy.ControlPlane.Tools.BindingIntegrity;

namespace Envoy.ControlPlane.Tools;

internal static class PinnedReleaseTool
{
    internal static async Task<string> GetAsync(string name, string cache)
    {
        var tools = JsonNode.Parse(File.ReadAllText(".github/security/tools.json"))!;
        var tool = tools[name]![RuntimeInformation.RuntimeIdentifier]
            ?? throw new InvalidOperationException("No pinned " + name + " executable for this platform");
        string directory = Path.Combine(Path.GetFullPath(cache), name, tool["sha256"]!.GetValue<string>());
        string archive = Path.Combine(directory, "download");
        string executable = Path.Combine(directory, name);
        Directory.CreateDirectory(directory);
        if (!File.Exists(archive))
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
            byte[] bytes = await http.GetByteArrayAsync(tool["url"]!.GetValue<string>());
            Require(ComputeSha256(bytes) == tool["sha256"]!.GetValue<string>(), "Downloaded tool checksum differs: " + name);
            await File.WriteAllBytesAsync(archive, bytes);
        }
        Require(GetFileSha256(archive) == tool["sha256"]!.GetValue<string>(), "Cached tool checksum differs: " + name);
        if (tool["archiveEntry"] is { } entryName)
        {
            await using var compressed = File.OpenRead(archive);
            await using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
            await using var tar = new TarReader(gzip);
            TarEntry? entry;
            bool found = false;
            while ((entry = tar.GetNextEntry()) is not null)
            {
                if (entry.Name.TrimStart('.', '/') != entryName.GetValue<string>()) continue;
                Require(entry.EntryType is TarEntryType.RegularFile or TarEntryType.V7RegularFile, "Unexpected tool archive entry");
                await using var output = File.Create(executable);
                await entry.DataStream!.CopyToAsync(output);
                found = true;
                break;
            }
            Require(found, "Missing executable in pinned archive: " + name);
        }
        else
        {
            File.Copy(archive, executable, overwrite: true);
        }
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return executable;
    }
}
