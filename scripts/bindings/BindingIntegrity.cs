using System.Security.Cryptography;

namespace Envoy.ControlPlane.Tools;

internal static class BindingIntegrity
{
    internal static string GetFileSha256(string path) => ComputeSha256(File.ReadAllBytes(path));
    internal static string ComputeSha256(byte[] value) => Convert.ToHexStringLower(SHA256.HashData(value));

    internal static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
