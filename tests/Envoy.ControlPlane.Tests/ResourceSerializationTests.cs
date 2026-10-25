using System.Security.Cryptography;
using Envoy.Config.Core.V3;
using Envoy.Config.Listener.V3;
using Envoy.ControlPlane.Resources;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Xunit;

namespace Envoy.ControlPlane.Tests;

public sealed class ResourceSerializationTests
{
    [Fact]
    public void DeterministicMapOrder_ProducesSameBytesAndHash()
    {
        var first = new Metadata();
        first.FilterMetadata.Add("z", new Struct());
        first.FilterMetadata.Add("a", new Struct());
        var second = new Metadata();
        second.FilterMetadata.Add("a", new Struct());
        second.FilterMetadata.Add("z", new Struct());
        byte[] bytes = ResourceSerialization.Serialize(first);
        Assert.Equal(bytes, ResourceSerialization.Serialize(second));
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), ResourceSerialization.ComputeHash(bytes));
    }

    [Fact]
    public void UnknownFields_AffectVersionAndSurviveSerialization()
    {
        var original = new Listener { Name = "listener" };
        // Field 500, varint value 7: independent unknown-wire fixture.
        byte[] wire = [.. original.ToByteArray(), 0xa0, 0x1f, 0x07];
        var withUnknown = Listener.Parser.ParseFrom(wire);
        Assert.Equal(wire, ResourceSerialization.Serialize(withUnknown));
        Assert.NotEqual(ResourceSerialization.ComputeHash(original.ToByteArray()),
            ResourceSerialization.ComputeHash(ResourceSerialization.Serialize(withUnknown)));
    }

    [Fact]
    public void EmptyResource_UsesKnownSha256Digest()
    {
        Assert.Empty(ResourceSerialization.Serialize(null));
        Assert.Equal("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
            ResourceSerialization.ComputeHash([]));
    }
}
