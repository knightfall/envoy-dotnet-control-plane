using System.Security.Cryptography;
using System.Text;

namespace Envoy.ControlPlane.Tools;

internal static class BindingIntegrity
{
    internal static string GetFileSha256(string path) => ComputeSha256(File.ReadAllBytes(path));
    internal static string ComputeSha256(byte[] value) => Convert.ToHexStringLower(SHA256.HashData(value));

    internal static string GetGeneratedSourcesSha256(IEnumerable<(string RelativePath, string Contents)> sources)
    {
        // Sort the portable inventory, since native separators change ordinal order.
        var inventory = sources.Select(source =>
        {
            string relativePath = source.RelativePath.Replace('\\', '/');
            string contents = source.Contents.ReplaceLineEndings("\n");
            string sourceHash = ComputeSha256(Encoding.UTF8.GetBytes(contents));
            return relativePath + ":" + sourceHash;
        }).Order(StringComparer.Ordinal);

        return ComputeSha256(Encoding.UTF8.GetBytes(string.Join('\n', inventory)));
    }

    internal static int VerifyGeneratedSources(string directory, int expectedCount, string expectedHash)
    {
        string[] files = Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories);
        var sources = files.Select(path => (Path.GetRelativePath(directory, path), File.ReadAllText(path)));
        Require(files.Length == expectedCount, "Generated file count differs: " + directory);
        Require(GetGeneratedSourcesSha256(sources) == expectedHash, "Generated source fingerprint differs: " + directory);
        return files.Length;
    }

    internal static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
