using System.Security.Cryptography;
using Google.Protobuf;

namespace Envoy.ControlPlane.Resources;

/// <summary>Deterministic protobuf materialization and upstream-style resource hashing.</summary>
public static class ResourceSerialization
{
    /// <summary>Serializes a stable message deterministically; a null resource produces empty bytes.</summary>
    /// <param name="resource">The retained stable message.</param>
    public static byte[] Serialize(IMessage? resource)
    {
        if (resource is null)
        {
            return [];
        }
        byte[] bytes = new byte[resource.CalculateSize()];
        using var output = new CodedOutputStream(bytes) { Deterministic = true };
        resource.WriteTo(output);
        output.CheckNoSpaceLeft();
        return bytes;
    }

    /// <summary>Transfers the private serialized buffer to a ByteString without copying it.</summary>
    /// <param name="resource">The retained stable message.</param>
    internal static ByteString SerializeByteString(IMessage? resource) => UnsafeByteOperations.UnsafeWrap(Serialize(resource));

    /// <summary>Returns a lowercase SHA256 digest; deterministic bytes are not canonical across languages.</summary>
    /// <param name="bytes">The materialized protobuf bytes.</param>
    public static string ComputeHash(ReadOnlySpan<byte> bytes)
    {
        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(bytes, hash);
        return Convert.ToHexStringLower(hash);
    }
}
