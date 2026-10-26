using System.Text.Json.Nodes;
using Envoy.ControlPlane.Tools;
using Xunit;

namespace Envoy.ControlPlane.Upstream.Tests;

public sealed class DeltaTraceComparerTests
{
    [Fact]
    public void GeneratedNonces_CanChangeWhileAssociationsStayStreamWide()
    {
        var candidate = Trace();
        candidate["events"]![3] = "response|A|v1|alpha";
        candidate["events"]![4] = "send|A|v1|alpha";
        candidate["events"]![5] = "request|A|node|alpha|b|a|true";
        candidate["events"]![7] = "response|B|v2|beta";
        candidate["events"]![8] = "send|B|v2|beta";
        candidate["events"]![9] = "request|B|node|beta|||false";
        DiscoveryTraceComparer.CompareDelta("renamed", candidate, Trace());
        DiscoveryTraceComparer.CompareDelta("reverse", Trace(), candidate);
        Assert.Equal("response|A|v1|alpha", candidate["events"]![3]!.GetValue<string>());
    }

    [Theory]
    [InlineData(4, "send|A|v1|wrong-wire")]
    [InlineData(5, "request|A|node|other|b|a|true")]
    [InlineData(5, "request|A|node|1|b|a|false")]
    [InlineData(5, "request|A|node|1|a|b|true")]
    [InlineData(6, "watch|A|b||a=advanced|false")]
    [InlineData(7, "response|B|v2|1")]
    [InlineData(8, "send|B|v2|1")]
    [InlineData(7, "send|B|v2|2")]
    public void BrokenAssociationOrProtocolFields_AreRejected(int index, string changed)
    {
        var candidate = Trace();
        candidate["events"]![index] = changed;
        Assert.Contains("injected: Delta trace differs", Assert.Throws<InvalidDataException>(() =>
            DiscoveryTraceComparer.CompareDelta("injected", candidate, Trace())).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LiteralCallbackNonceAndLifecycleMetadata_RemainExact()
    {
        var reference = Trace();
        reference["events"]![4] = "send|A|v1|callback";
        var candidate = reference.DeepClone().AsObject();
        candidate["events"]![4] = "send|A|v1|different";
        Assert.Throws<InvalidDataException>(() => DiscoveryTraceComparer.CompareDelta("literal", candidate, reference));
        candidate = reference.DeepClone().AsObject();
        candidate["disposals"]![0] = 0;
        Assert.Throws<InvalidDataException>(() => DiscoveryTraceComparer.CompareDelta("cleanup", candidate, reference));
    }

    private static JsonObject Trace() => JsonNode.Parse("""
        {"events":["open|","request|A|node||a||false","watch|A|a||a=initial|false",
        "response|A|v1|1","send|A|v1|1","request|A|node|1|b|a|true","watch|A|b||a=v1|false",
        "response|B|v2|2","send|B|v2|2","request|B|node|2|||false","close|node"],"disposals":[1,1]}
        """)!.AsObject();
}
