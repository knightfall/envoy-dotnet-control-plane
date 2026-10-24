using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using static Envoy.ControlPlane.Tools.BindingPaths;
using static Envoy.ControlPlane.Tools.BindingIntegrity;

namespace Envoy.ControlPlane.Tools;

internal static class BufTool
{
    internal static async Task<string> GetExecutableAsync(string sourceCache, JsonNode tool)
    {
        string platform = RuntimeInformation.RuntimeIdentifier;
        Require(tool["executables"]!.AsObject().TryGetPropertyValue(platform, out var artifact),
            "No pinned Buf executable for " + platform);

        var url = new Uri(artifact!["url"]!.GetValue<string>());
        string executable = Path.Combine(sourceCache, "tools", Path.GetFileName(url.AbsolutePath));
        string expectedHash = artifact["sha256"]!.GetValue<string>();

        if (!File.Exists(executable))
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
            byte[] bytes = await http.GetByteArrayAsync(url);
            Require(ComputeSha256(bytes) == expectedHash, "Downloaded Buf checksum differs");
            Directory.CreateDirectory(Path.GetDirectoryName(executable)!);
            await File.WriteAllBytesAsync(executable, bytes);
        }

        Require(GetFileSha256(executable) == expectedHash, "Cached Buf checksum differs");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        string version = await BuildProcess.RunAsync(executable, ["--version"]);
        Require(version.Trim() == tool["version"]!.GetValue<string>(), "Unexpected Buf version");
        return executable;
    }
}
