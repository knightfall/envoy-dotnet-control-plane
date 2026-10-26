using System.Text.Json.Nodes;
using Envoy.ControlPlane.Tools;
using Xunit;

namespace Envoy.ControlPlane.Upstream.Tests;

public sealed class SotwTraceComparerTests
{
    [Fact]
    public void GeneratedNonces_PreserveStreamWideAssociations()
    {
        var reference = Trace();
        var candidate = Trace();
        candidate["events"]![3] = "response|type/A|new|alpha";
        candidate["events"]![6] = "request|type/A|client|old|alpha|a|true";
        candidate["events"]![10] = "response|type/B|other|beta";
        candidate["events"]![11] = "send|type/B|other|beta";
        candidate["events"]![12] = "request|type/B|client|other|beta|b|false";

        DiscoveryTraceComparer.Compare("renamed", candidate, reference);
        DiscoveryTraceComparer.Compare("reverse", reference, candidate);
        Assert.Equal("response|type/A|new|alpha", candidate["events"]![3]!.GetValue<string>());
        Assert.Equal("response|type/A|new|1", reference["events"]![3]!.GetValue<string>());
    }

    [Theory]
    [InlineData(4, "send|type/A|new|changed-wire")]
    [InlineData(5, "request|type/A|client|old|other-stale|a|true")]
    [InlineData(7, "watch|type/A|old|a|a=old|false")]
    public void LiteralNoncesAndCallerVersions_RemainExact(int index, string replacement)
    {
        var candidate = Trace();
        candidate["events"]![index] = replacement;

        var error = Assert.Throws<InvalidDataException>(() => DiscoveryTraceComparer.Compare("literal", candidate, Trace()));
        Assert.Contains($"literal: SotW trace differs at event {index}", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(5, "request|type/A|client|old|1|a|true")] // Stale request incorrectly associated with the current response.
    [InlineData(6, "request|type/A|client|old|stale|a|true")] // Matching ACK/NACK loses its response association.
    [InlineData(10, "response|type/B|other|1")] // Reused nonce across resource types.
    [InlineData(11, "send|type/B|other|1")] // Writer sends another type's response nonce.
    [InlineData(6, "request|type/A|client|new|1|a|true")] // NACK incorrectly advances accepted version.
    [InlineData(6, "request|type/A|client|old|1|a|false")] // NACK flag is lost.
    [InlineData(10, "send|type/B|other|2")] // Send occurs before response callback.
    public void BrokenTrace_IsRejected(int index, string replacement)
    {
        var candidate = Trace();
        candidate["events"]![index] = replacement;

        var error = Assert.Throws<InvalidDataException>(() => DiscoveryTraceComparer.Compare("injected", candidate, Trace()));
        Assert.Contains($"injected: SotW trace differs at event {index}", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("error")]
    [InlineData("disposal")]
    [InlineData("close")]
    public void ErrorsAndWatchCleanup_RemainExact(string mutation)
    {
        var candidate = Trace();
        if (mutation == "error") candidate["error"] = "Unavailable";
        if (mutation == "disposal") candidate["disposals"]![0] = 0;
        if (mutation == "close") candidate["events"]!.AsArray().RemoveAt(14);

        var error = Assert.Throws<InvalidDataException>(() => DiscoveryTraceComparer.Compare("lifecycle", candidate, Trace()));
        Assert.Contains("lifecycle: SotW trace differs", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GeneratedNonce_CannotAliasPreviouslyObservedLiteral()
    {
        var reference = Trace();
        var candidate = Trace();
        reference["events"]![1] = "request|type/A|client|old|reserved|a|false";
        candidate["events"]![1] = "request|type/A|client|old|reserved|a|false";
        candidate["events"]![3] = "response|type/A|new|reserved";

        var error = Assert.Throws<InvalidDataException>(() => DiscoveryTraceComparer.Compare("alias", candidate, reference));
        Assert.Contains("event 3 response", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("unknown|type/A")]
    [InlineData("send|type/A")]
    public void UnknownOrMalformedEvents_AreRejected(string value)
    {
        var candidate = Trace();
        candidate["events"]![3] = value;

        Assert.Throws<InvalidDataException>(() => DiscoveryTraceComparer.Compare("format", candidate, Trace()));
    }

    private static JsonObject Trace() => JsonNode.Parse("""
        {
          "events": [
            "open|",
            "request|type/A|client|old||a|false",
            "watch|type/A|old|a||false",
            "response|type/A|new|1",
            "send|type/A|new|wire",
            "request|type/A|client|old|stale|a|true",
            "request|type/A|client|old|1|a|true",
            "watch|type/A|old|a|a=new|false",
            "request|type/B|client|||b|false",
            "watch|type/B||b||false",
            "response|type/B|other|2",
            "send|type/B|other|2",
            "request|type/B|client|other|2|b|false",
            "watch|type/B|other|b|b=other|false",
            "close|client"
          ],
          "disposals": [1, 1, 1, 1]
        }
        """)!.AsObject();
}
