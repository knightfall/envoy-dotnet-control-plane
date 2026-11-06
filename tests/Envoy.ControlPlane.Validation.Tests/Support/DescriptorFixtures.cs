using Envoy.Type.V3;
using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace Envoy.ControlPlane.Validation.Tests.Support;

internal static class DescriptorFixtures
{
    internal static FileDescriptor PercentFile(Action<FileDescriptorProto> change)
    {
        var file = Percent.Descriptor.File.ToProto();
        change(file);
        var types = new GeneratedClrTypeInfo(null, null,
        [
            new(typeof(Percent), Percent.Parser, [nameof(Percent.Value)], null, null, null, null),
            new(typeof(FractionalPercent), FractionalPercent.Parser,
                [nameof(FractionalPercent.Numerator), nameof(FractionalPercent.Denominator)], null,
                [typeof(FractionalPercent.Types.DenominatorType)], null, null),
        ]);
        return FileDescriptor.FromGeneratedCode(file.ToByteArray(), Percent.Descriptor.File.Dependencies.ToArray(), types);
    }
}
