using Envoy.Config.Core.V3;
using Envoy.ControlPlane.Tools;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Xunit;
using Pgv = Validate.ValidateExtensions;
using Protovalidate = Buf.Validate.ValidateExtensions;

namespace Envoy.ControlPlane.Validation.Tests;

public sealed class OverlayTests
{
    [Fact]
    public void Overlay_OptionalOneofMemberRequirement_IsConditional_OriginalPgvRemains()
    {
        var original = Read("catalog.pb");
        var derived = Read("validation.catalog.pb");
        const string fileName = "envoy/extensions/access_loggers/file/v3/file.proto";
        var originalFile = original.File.Single(file => file.Name == fileName);
        var derivedFile = derived.File.Single(file => file.Name == fileName);
        var originalField = originalFile.MessageType.Single().Field.Single(field => field.Name == "log_format");
        var derivedField = derivedFile.MessageType.Single().Field.Single(field => field.Name == "log_format");
        Assert.True(originalField.Options.GetExtension(Pgv.Rules).Message.Required);
        Assert.False(derivedField.Options.GetExtension(Protovalidate.Field).Required);

        // Reconstruct this member's official migration output on real source descriptors.
        derivedField.Options.GetExtension(Protovalidate.Field).Required = true;
        var originalSubset = new FileDescriptorSet { File = { originalFile } };
        var derivedSubset = new FileDescriptorSet { File = { derivedFile } };
        string[] changes = OverlayTransform.PreserveConditionalOneofRequirements(originalSubset, derivedSubset);
        Assert.Equal(new[] { "envoy.extensions.access_loggers.file.v3.FileAccessLog.log_format" }, changes);
        Assert.False(derivedField.Options.GetExtension(Protovalidate.Field).HasRequired);
        Assert.True(derivedField.Options.GetExtension(Pgv.Rules).Message.Required);
        OverlayTransform.VerifyOriginalDescriptors(originalSubset, derivedSubset);
    }

    [Fact]
    public void Overlay_RequiredOneofAndOrdinaryRequiredMessage_RetainRequirements()
    {
        var oneof = SocketAddress.Descriptor.Oneofs.Single(item => item.Name == "port_specifier");
        Assert.True(oneof.GetOptions().GetExtension(Pgv.Required));
        Assert.True(oneof.GetOptions().GetExtension(Protovalidate.Oneof).Required);
        var header = HeaderValueOption.Descriptor.FindFieldByName("header");
        Assert.True(header.GetOptions().GetExtension(Pgv.Rules).Message.Required);
        Assert.True(header.GetOptions().GetExtension(Protovalidate.Field).Required);
    }

    [Fact]
    public void Overlay_AllOriginalDescriptorsRemainEqual_ContractDriftIsRejected()
    {
        var original = Read("catalog.pb");
        var derived = Read("validation.catalog.pb");
        Assert.Equal(523, original.File.Count);
        Assert.Equal(524, derived.File.Count);
        OverlayTransform.VerifyOriginalDescriptors(original, derived);
        var file = derived.File.Single(item => item.Name == "envoy/type/v3/percent.proto");
        file.MessageType.Single(item => item.Name == "Percent").Field[0].Number = 99;
        var error = Assert.Throws<InvalidOperationException>(() => OverlayTransform.VerifyOriginalDescriptors(original, derived));
        Assert.Contains(file.Name, error.Message);
    }

    private static FileDescriptorSet Read(string name)
    {
        var extensions = new ExtensionRegistry
        {
            Pgv.Rules, Pgv.Required, Pgv.Disabled, Pgv.Ignored,
            Protovalidate.Field, Protovalidate.Oneof, Protovalidate.Message,
        };
        using var stream = typeof(Address).Assembly.GetManifestResourceStream("Envoy.ControlPlane.Protocol." + name)!;
        return FileDescriptorSet.Parser.WithExtensionRegistry(extensions).ParseFrom(stream);
    }
}
