using Envoy.ControlPlane.Tools;
using Xunit;

namespace Envoy.ControlPlane.Upstream.Tests;

public sealed class BindingIntegrityTests
{
    [Theory]
    [InlineData("/", "\n")]
    [InlineData("\\", "\n")]
    [InlineData("/", "\r\n")]
    [InlineData("\\", "\r\n")]
    public void GetGeneratedSourcesSha256_PlatformRepresentations_MatchLockedFingerprint(string separator, string newline)
    {
        // Foo/V1 sorts before FooBar with '/', but after it with '\\'.
        (string RelativePath, string Contents)[] sources =
        [
            ("Envoy/FooBar.cs".Replace("/", separator, StringComparison.Ordinal), "class Sibling {}" + newline),
            ("Envoy/Foo/V1/Foo.cs".Replace("/", separator, StringComparison.Ordinal), "class Nested {}" + newline),
            ("Envoy/Foo.cs".Replace("/", separator, StringComparison.Ordinal), "class First {}" + newline)
        ];

        string actual = BindingIntegrity.GetGeneratedSourcesSha256(sources);

        // Independently calculated from the LF, forward-slash inventory in ordinal order.
        Assert.Equal("0de98386da725a9ceaadbac103a6c5c6eb773aff8fbfe2cd6320bb787ac24f61", actual);
    }

    [Fact]
    public void GetGeneratedSourcesSha256_ChangedContent_ChangesFingerprint()
    {
        (string RelativePath, string Contents)[] sources =
        [
            ("Envoy/FooBar.cs", "class Changed {}\n"),
            ("Envoy/Foo/V1/Foo.cs", "class Nested {}\n"),
            ("Envoy/Foo.cs", "class First {}\n")
        ];

        string actual = BindingIntegrity.GetGeneratedSourcesSha256(sources);

        Assert.Equal("9ab999b3839e1fe73b7348eff01b9b19a58af0eed25ad37a0fbb58dd08684be4", actual);
    }
}
