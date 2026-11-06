using Buf.Validate;
using Envoy.Config.Core.V3;
using Envoy.ControlPlane.Validation.Tests.Support;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Google.Protobuf.WellKnownTypes;
using ProtoValidate;
using ProtoValidate.Exceptions;
using Xunit;
using Protovalidate = Buf.Validate.ValidateExtensions;

namespace Envoy.ControlPlane.Validation.Tests;

public sealed class ValidationFaultTests
{
    [Theory]
    [InlineData("field")]
    [InlineData("nested")]
    [InlineData("message")]
    [InlineData("enum")]
    public void UnsupportedAnnotation_IsCompilationFault(string kind)
    {
        var file = DescriptorFixtures.PercentFile(source =>
        {
            var field = source.MessageType[0].Field[0].Options.GetExtension(Protovalidate.Field);
            switch (kind)
            {
                case "field":
                    source.MessageType[0].Field[0].Options.SetExtension(Protovalidate.Field, AppendUnknown(field, FieldRules.Parser));
                    break;
                case "nested":
                    field.Double = AppendUnknown(field.Double, DoubleRules.Parser);
                    break;
                case "message":
                    source.MessageType[0].Options.SetExtension(Protovalidate.Message, AppendUnknown(new MessageRules(), MessageRules.Parser));
                    break;
                case "enum":
                    field.Ignore = (Ignore)999;
                    break;
            }
        });

        var error = Assert.Throws<CompilationException>(() => Preload(file));

        Assert.Contains("Unsupported validation", error.Message, StringComparison.Ordinal);
        Assert.Contains("envoy.type.v3.Percent", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MismatchedRuleType_IsCompilationFault()
    {
        var file = DescriptorFixtures.PercentFile(source =>
            source.MessageType[0].Field[0].Options.GetExtension(Protovalidate.Field).Uint32 = new UInt32Rules { Gte = 1 });

        var error = Assert.Throws<CompilationException>(() => Preload(file));

        Assert.Contains("mismatched rule type", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MalformedCel_IsCompilationFaultWithCause()
    {
        var file = DescriptorFixtures.PercentFile(source =>
            source.MessageType[0].Field[0].Options.GetExtension(Protovalidate.Field).Cel.Add(
                new Rule { Id = "test.invalid", Expression = "this >" }));

        var error = Assert.Throws<CompilationException>(() => Preload(file));

        Assert.Contains("envoy.type.v3.Percent", error.Message, StringComparison.Ordinal);
        Assert.NotNull(error.InnerException);
    }

    [Fact]
    public void Any_UnknownOpaquePayload_IsNotImplicitlyDecodedOrValidated()
    {
        var payload = new Any { TypeUrl = "type.googleapis.com/example.Unregistered", Value = ByteString.CopyFrom(255) };
        var message = new TypedExtensionConfig { Name = "custom", TypedConfig = payload };
        byte[] before = message.ToByteArray();

        var result = new Validator().Validate(message, failFast: false);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Violations);
        Assert.Equal(before, message.ToByteArray());
        Assert.Null(payload.Unpack(TypeRegistry.Empty));
        var malformed = new Any { TypeUrl = "type.googleapis.com/google.protobuf.Duration", Value = payload.Value };
        Assert.Throws<InvalidProtocolBufferException>(() => malformed.Unpack(TypeRegistry.FromMessages(Duration.Descriptor)));
    }

    private static Validator Preload(FileDescriptor file) => new(new ValidatorOptions
    {
        FileDescriptors = [file], PreLoadDescriptors = true,
    });

    private static T AppendUnknown<T>(T rules, MessageParser<T> parser) where T : IMessage<T>
    {
        using var bytes = new MemoryStream();
        using (var output = new CodedOutputStream(bytes, leaveOpen: true))
        {
            rules.WriteTo(output);
            output.WriteTag(999, WireFormat.WireType.Varint);
            output.WriteInt32(1);
        }
        return parser.ParseFrom(bytes.ToArray());
    }
}
