using Envoy.Config.Core.V3;
using Envoy.ControlPlane.Tools;
using Envoy.Type.V3;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Google.Protobuf.WellKnownTypes;
using ProtoValidate;
using ProtoValidate.Exceptions;
using Xunit;
using Pgv = Validate.ValidateExtensions;
using Protovalidate = Buf.Validate.ValidateExtensions;

namespace Envoy.ControlPlane.Validation.Tests;

public sealed class PgvMetadataGuardTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Validate_PgvOnlyField_ThrowsEvenAfterMigratedDescriptorWasCached(bool warm)
    {
        var validator = new Validator();
        if (warm)
        {
            Assert.True(validator.Validate(new Percent { Value = 50 }, failFast: false).IsSuccess);
            Assert.False(validator.Validate(new Percent { Value = 101 }, failFast: false).IsSuccess);
        }
        var legacy = ChangedDescriptors(new Percent { Value = 101 }, files =>
            OverlayTransform.RemoveDerivedMetadata(files.Single(file => file.Name == Percent.Descriptor.File.Name)));

        var error = Assert.Throws<CompilationException>(() => validator.Validate(legacy, failFast: false));

        Assert.Contains("envoy.type.v3.Percent.value", error.Message, StringComparison.Ordinal);
        Assert.Contains("Regenerate", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_PgvOnlyNestedField_IdentifiesTheUnmigratedField()
    {
        var legacy = ChangedDescriptors(new HeaderValueOption { Header = new HeaderValue { Key = "x" } }, files =>
        {
            var file = files.Single(item => item.Name == HeaderValue.Descriptor.File.Name);
            file.MessageType.Single(item => item.Name == "HeaderValue").Field.Single(item => item.Name == "key")
                .Options.ClearExtension(Protovalidate.Field);
        });

        var error = Assert.Throws<CompilationException>(() => new Validator().Validate(legacy, failFast: false));

        Assert.Contains("envoy.config.core.v3.HeaderValue.key", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_PgvOnlyRequiredOneof_IdentifiesTheOneof()
    {
        var legacy = ChangedDescriptors(new SocketAddress { Address = "127.0.0.1", PortValue = 80 }, files =>
        {
            var file = files.Single(item => item.Name == SocketAddress.Descriptor.File.Name);
            file.MessageType.Single(item => item.Name == "SocketAddress").OneofDecl
                .Single(item => item.Name == "port_specifier").Options.ClearExtension(Protovalidate.Oneof);
        });

        var error = Assert.Throws<CompilationException>(() => new Validator().Validate(legacy, failFast: false));

        Assert.Contains("envoy.config.core.v3.SocketAddress.port_specifier", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Validate_LegacyMessageOptionWithoutMigration_IdentifiesTheMessage(bool ignored)
    {
        var legacy = ChangedDescriptors(new Percent(), files =>
        {
            var message = files.Single(item => item.Name == Percent.Descriptor.File.Name).MessageType
                .Single(item => item.Name == "Percent");
            message.Options.SetExtension(ignored ? Pgv.Ignored : Pgv.Disabled, true);
        });

        var error = Assert.Throws<CompilationException>(() => new Validator().Validate(legacy, failFast: false));

        Assert.Contains("PGV-only validation metadata on envoy.type.v3.Percent.", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SkippedNestedMessage_DoesNotRequireItsRulesToBeMigrated()
    {
        var message = JsonParser.Default.Parse<KeyValueAppend>("{\"entry\":{}}");
        var legacy = ChangedDescriptors(message, files =>
        {
            var file = files.Single(item => item.Name == KeyValue.Descriptor.File.Name);
            foreach (var field in file.MessageType.Single(item => item.Name == "KeyValue").Field)
            {
                field.Options?.ClearExtension(Protovalidate.Field);
            }
        });
        var validator = new Validator();

        Assert.True(validator.Validate(message, failFast: false).IsSuccess);
        // The guard skips the child; compiling the metadata-only root still requires a CLR parser.
        var error = Assert.Throws<CompilationException>(() => validator.GetEvaluatorDebugString(legacy));
        Assert.Contains("No CLR parser available for envoy.config.core.v3.KeyValueAppend", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnconstrainedMessage_RemainsValid()
    {
        var validator = new Validator();

        Assert.True(validator.Validate(new StringValue(), failFast: false).IsSuccess);
        Assert.True(validator.Validate(new StringValue { Value = "anything" }, failFast: false).IsSuccess);
    }

    private static IMessage ChangedDescriptors(IMessage message, Action<List<FileDescriptorProto>> change)
    {
        var files = new List<FileDescriptorProto>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        Add(message.Descriptor.File);
        change(files);
        var extensions = new ExtensionRegistry
        {
            Pgv.Rules, Pgv.Required, Pgv.Disabled, Pgv.Ignored,
            Protovalidate.Field, Protovalidate.Oneof, Protovalidate.Message,
        };
        var descriptors = FileDescriptor.BuildFromByteStrings(files.Select(file => file.ToByteString()), extensions);
        var descriptor = descriptors.Single(file => file.Name == message.Descriptor.File.Name).MessageTypes
            .Single(item => item.FullName == message.Descriptor.FullName);
        return new DescriptorMessage(message, descriptor);

        void Add(FileDescriptor file)
        {
            if (!visited.Add(file.Name))
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

    /// <summary>Supplies the upstream descriptor without a second generated message owner.</summary>
    private sealed class DescriptorMessage(IMessage message, MessageDescriptor descriptor) : IMessage
    {
        public MessageDescriptor Descriptor => descriptor;
        public int CalculateSize() => message.CalculateSize();
        public void MergeFrom(CodedInputStream input) => message.MergeFrom(input);
        public void WriteTo(CodedOutputStream output) => message.WriteTo(output);
    }
}
