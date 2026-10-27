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
    public void Overlay_AnyRulesOnMessage_PreservePresenceWithoutNestedValidation()
    {
        var field = ExtensionConfigSource.Descriptor.FindFieldByName("config_source");
        Assert.Equal("envoy.config.core.v3.ConfigSource", field.MessageType.FullName);
        Assert.True(field.GetOptions().GetExtension(Pgv.Rules).Any.Required);
        var translated = field.GetOptions().GetExtension(Protovalidate.Field);
        Assert.Equal(Buf.Validate.Ignore.Always, translated.Ignore);
        Assert.Null(translated.Any);
        Assert.False(translated.HasRequired);

        var message = ExtensionConfigSource.Descriptor.GetOptions().GetExtension(Protovalidate.Message);
        var presence = Assert.Single(message.Cel);
        Assert.Equal("pgv.any.required", presence.Id);
        Assert.Equal("has(this.config_source)", presence.Expression);
        OverlayTransform.VerifyOriginalDescriptors(Read("catalog.pb"), Read("validation.catalog.pb"));
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

    [Fact]
    public void RecognizedRules_NewRulePath_IsReportedWithoutTreatingItAsConformant()
    {
        var (catalog, message) = PercentCatalog();
        var recognized = new HashSet<string>(StringComparer.Ordinal) { "double.gte", "double.lte" };
        Assert.Empty(RecognizedRules.FindUnsupported(catalog, recognized));

        message.Field[0].Options.GetExtension(Pgv.Rules).Double.Const = 50;

        Assert.Equal(new[] { "envoy.type.v3.Percent.value: double.const" }, RecognizedRules.FindUnsupported(catalog, recognized));
    }

    [Fact]
    public void RecognizedRules_UnknownNestedWireField_IsReported()
    {
        var (catalog, message) = PercentCatalog();
        var rules = message.Field[0].Options.GetExtension(Pgv.Rules);
        using var bytes = new MemoryStream();
        rules.Double.WriteTo(bytes);
        using (var writer = new CodedOutputStream(bytes, leaveOpen: true))
        {
            writer.WriteTag(999, WireFormat.WireType.Varint);
            writer.WriteUInt32(1);
            writer.Flush();
        }
        rules.Double = Validate.DoubleRules.Parser.ParseFrom(bytes.ToArray());

        var unsupported = RecognizedRules.FindUnsupported(catalog, new HashSet<string> { "double.gte", "double.lte" });

        Assert.Equal(new[] { "envoy.type.v3.Percent.value: unknown encoded rule fields" }, unsupported);
    }

    [Fact]
    public void RecognizedRules_MessageLevelDisabledMetadata_IsReported()
    {
        var (catalog, message) = PercentCatalog();
        message.Options ??= new MessageOptions();
        message.Options.SetExtension(Pgv.Disabled, true);

        var unsupported = RecognizedRules.FindUnsupported(catalog, new HashSet<string> { "double.gte", "double.lte" });

        Assert.Equal(new[] { "envoy.type.v3.Percent: message-level disabled/ignored metadata" }, unsupported);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(999)]
    public void RecognizedRules_UnknownWellKnownRegexValue_IsReported(int value)
    {
        var source = Read("catalog.pb").File.Single(file => file.Name == "envoy/config/core/v3/protocol.proto");
        var message = source.MessageType.Single(item => item.Name == "UpstreamHttpProtocolOptions").Clone();
        var field = message.Field.Single(item => item.Name == "override_auto_sni_header");
        field.Options.GetExtension(Pgv.Rules).String.WellKnownRegex = (Validate.KnownRegex)value;
        var file = new FileDescriptorProto { Name = source.Name, Package = source.Package };
        file.MessageType.Add(message);
        var catalog = new FileDescriptorSet { File = { file } };

        var unsupported = RecognizedRules.FindUnsupported(catalog, new HashSet<string> { "string.well_known_regex", "string.ignore_empty" });

        Assert.Equal(new[] { $"envoy.config.core.v3.UpstreamHttpProtocolOptions.override_auto_sni_header: unsupported enum value {value} at string.well_known_regex" }, unsupported);
    }

    [Fact]
    public void RecognizedRules_Verification_RejectsUnrecognizedFieldWithActionableError()
    {
        var (catalog, message) = PercentCatalog();
        message.Field[0].Options.GetExtension(Pgv.Rules).Double.Const = 50;
        string directory = Path.Combine(Path.GetTempPath(), "envoy-recognized-rules-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string descriptors = Path.Combine(directory, "catalog.pb");
            string manifest = Path.Combine(directory, "rules.json");
            File.WriteAllBytes(descriptors, catalog.ToByteArray());
            File.WriteAllText(manifest, """{"schemaVersion":1,"recognizedRulePaths":["double.gte","double.lte"]}""");

            var error = Assert.Throws<InvalidOperationException>(() => RecognizedRules.Verify(descriptors, manifest));

            Assert.Contains("Unsupported PGV metadata or rules", error.Message);
            Assert.Contains("envoy.type.v3.Percent.value: double.const", error.Message);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static (FileDescriptorSet Catalog, DescriptorProto Message) PercentCatalog()
    {
        var source = Read("catalog.pb").File.Single(file => file.Name == "envoy/type/v3/percent.proto");
        var message = source.MessageType.Single(item => item.Name == "Percent").Clone();
        var file = new FileDescriptorProto { Name = source.Name, Package = source.Package };
        file.MessageType.Add(message);
        return (new FileDescriptorSet { File = { file } }, message);
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
