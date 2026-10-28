using Envoy.Type.V3;
using Envoy.ControlPlane.Validation.Tests.Support;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using ProtoValidate;
using ProtoValidate.Exceptions;
using Xunit;
using Pgv = Validate.ValidateExtensions;
using Protovalidate = Buf.Validate.ValidateExtensions;

namespace Envoy.ControlPlane.Validation.Tests;

public sealed class EvaluatorDescriptorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Validate_MetadataOnlyDescriptor_ThrowsWithoutChangingGeneratedRules(bool warm)
    {
        var validator = new Validator();
        if (warm)
        {
            AssertGeneratedRules(validator);
        }
        var metadataOnly = MetadataOnlyPercent();
        Assert.Null(metadataOnly.Descriptor.Parser);
        Assert.Equal(Percent.Descriptor.Fields[1].GetOptions(), metadataOnly.Descriptor.Fields[1].GetOptions());

        var error = Assert.Throws<CompilationException>(() => validator.Validate(metadataOnly, failFast: false));

        Assert.Contains("envoy.type.v3.Percent", error.Message, StringComparison.Ordinal);
        Assert.Contains("CLR parser", error.Message, StringComparison.Ordinal);
        AssertGeneratedRules(validator);
    }

    [Fact]
    public void Preload_MetadataOnlyDescriptor_ThrowsCompilationException()
    {
        var metadataOnly = MetadataOnlyPercent();

        var error = Assert.Throws<CompilationException>(() => new Validator(new ValidatorOptions
        {
            FileDescriptors = [metadataOnly.Descriptor.File],
            PreLoadDescriptors = true,
            DisableLazy = true,
        }));

        Assert.Contains("envoy.type.v3.Percent", error.Message, StringComparison.Ordinal);
        Assert.Contains("CLR parser", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(200)]
    public void Preload_SameNameDescriptor_DoesNotChangeGeneratedRules(double upperBound)
    {
        var alternate = ClrBackedPercentFile(upperBound);
        Assert.NotSame(Percent.Descriptor, alternate.MessageTypes[0]);
        Assert.Equal(Percent.Descriptor.FullName, alternate.MessageTypes[0].FullName);
        var validator = new Validator(new ValidatorOptions
        {
            FileDescriptors = [alternate],
            PreLoadDescriptors = true,
        });

        AssertGeneratedRules(validator);
    }

    [Fact]
    public void ClosedDescriptorSet_SameNameDescriptor_DoesNotRegisterGeneratedDescriptor()
    {
        var validator = new Validator(new ValidatorOptions
        {
            FileDescriptors = [ClrBackedPercentFile(1)],
            PreLoadDescriptors = true,
            DisableLazy = true,
        });

        var error = Assert.Throws<CompilationException>(() => validator.Validate(new Percent { Value = 50 }, failFast: false));

        Assert.Contains("No evaluator available for envoy.type.v3.Percent", error.Message, StringComparison.Ordinal);
    }

    private static void AssertGeneratedRules(Validator validator)
    {
        Assert.True(validator.Validate(new Percent { Value = 50 }, failFast: false).IsSuccess);
        Assert.Contains(validator.Validate(new Percent { Value = 101 }, failFast: false).Violations,
            violation => violation.RuleId == "double.gte_lte" && violation.Field.GetPath() == "value");
    }

    private static IMessage MetadataOnlyPercent()
    {
        var files = new List<FileDescriptorProto>();
        var visited = new HashSet<FileDescriptor>();
        Add(Percent.Descriptor.File);
        var extensions = new ExtensionRegistry
        {
            Pgv.Rules, Pgv.Required, Pgv.Disabled, Pgv.Ignored,
            Protovalidate.Field, Protovalidate.Oneof, Protovalidate.Message,
        };
        var descriptors = FileDescriptor.BuildFromByteStrings(files.Select(file => file.ToByteString()), extensions);
        var descriptor = descriptors.Single(file => file.Name == Percent.Descriptor.File.Name).MessageTypes[0];
        return new DescriptorMessage(new Percent { Value = 101 }, descriptor);

        void Add(FileDescriptor file)
        {
            if (!visited.Add(file))
            {
                return;
            }
            foreach (var dependency in file.Dependencies)
            {
                Add(dependency);
            }
            files.Add(file.ToProto());
        }
    }

    private static FileDescriptor ClrBackedPercentFile(double upperBound)
    {
        // Rebind only test metadata to the existing CLR types; no second message owner is generated.
        return DescriptorFixtures.PercentFile(file =>
            file.MessageType[0].Field[0].Options.GetExtension(Protovalidate.Field).Double.Lte = upperBound);
    }

    private sealed class DescriptorMessage(IMessage message, MessageDescriptor descriptor) : IMessage
    {
        public MessageDescriptor Descriptor => descriptor;
        public int CalculateSize() => message.CalculateSize();
        public void MergeFrom(CodedInputStream input) => message.MergeFrom(input);
        public void WriteTo(CodedOutputStream output) => message.WriteTo(output);
    }
}
