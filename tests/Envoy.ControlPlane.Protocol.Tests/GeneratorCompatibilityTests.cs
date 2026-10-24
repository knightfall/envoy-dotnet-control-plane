using Google.Protobuf;
using Xunit;

namespace Envoy.ControlPlane.Protocol.Tests;

[Collection(ProtocolBindingCollection.Name)]
public sealed class GeneratorCompatibilityTests
{
    [Fact]
    public void GeneratorNameCorrections_PreserveDescriptorsAndOneofBehavior()
    {
        var expression = new Extensions.RateLimitDescriptors.Expr.V3.Descriptor { DescriptorKey = "user", Text = "request.user" };
        Assert.Equal(expression, Extensions.RateLimitDescriptors.Expr.V3.Descriptor.Parser.ParseFrom(expression.ToByteArray()));
        Assert.Equal("envoy.extensions.rate_limit_descriptors.expr.v3.Descriptor", ((IMessage)expression).Descriptor.FullName);
        var jwt = new Extensions.RateLimitDescriptors.JwtClaim.V3.Descriptor { DescriptorKey = "subject" };
        Assert.Equal(jwt, Extensions.RateLimitDescriptors.JwtClaim.V3.Descriptor.Parser.ParseFrom(jwt.ToByteArray()));
        Assert.Equal("envoy.extensions.rate_limit_descriptors.jwt_claim.v3.Descriptor", ((IMessage)jwt).Descriptor.FullName);
        var body = new Service.ExtProc.V3.BodyMutation { Body = ByteString.CopyFromUtf8("body") };
        body.ClearBodyValue();
        Assert.Equal(Service.ExtProc.V3.BodyMutation.MutationOneofCase.None, body.MutationCase);
        body.Body = ByteString.CopyFromUtf8("replacement");
        Service.ExtProc.V3.BodyMutation.Descriptor.FindFieldByName("body").Accessor.Clear(body);
        Assert.False(body.HasBody);
        body.ClearBody = true;
        Assert.True(Service.ExtProc.V3.BodyMutation.Parser.ParseFrom(body.ToByteArray()).ClearBody);
        Assert.Equal(2, Service.ExtProc.V3.BodyMutation.Descriptor.FindFieldByName("clear_body").FieldNumber);
    }
}
