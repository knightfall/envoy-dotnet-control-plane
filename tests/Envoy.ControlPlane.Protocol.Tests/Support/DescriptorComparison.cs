using Google.Protobuf;
using Google.Protobuf.Reflection;
using Xunit;

namespace Envoy.ControlPlane.Protocol.Tests;

internal static class DescriptorComparison
{
    internal static void CompareJsonNames(IEnumerable<DescriptorProto> source, IList<MessageDescriptor> actual)
    {
        foreach (var message in source)
        {
            var descriptor = actual.Single(m => m.Name == message.Name);
            foreach (var field in message.Field)
            {
                Assert.Equal(field.JsonName, descriptor.FindFieldByNumber(field.Number).JsonName);
            }
            CompareExtensionJsonNames(message.Extension, descriptor.Extensions.UnorderedExtensions);
            CompareJsonNames(message.NestedType, descriptor.NestedTypes);
        }
    }

    internal static void NormalizeRuntimeDescriptorMetadata(IEnumerable<DescriptorProto> messages)
    {
        foreach (var message in messages)
        {
            foreach (var field in message.Field)
            {
                field.ClearJsonName();
            }
            foreach (var field in message.Extension)
            {
                field.ClearJsonName();
            }
            // protoc intentionally removes RETENTION_SOURCE declaration values
            // from runtime descriptors. The unmodified source set is embedded.
            foreach (var range in message.ExtensionRange.Where(r => r.Options is not null))
            {
                var options = range.Options!;
                options.Declaration.Clear();
                options.ClearVerification();
                if (options.CalculateSize() == 0)
                {
                    range.Options = null;
                }
            }
            NormalizeRuntimeDescriptorMetadata(message.NestedType);
        }
    }

    internal static void CompareExtensionJsonNames(IEnumerable<FieldDescriptorProto> source, IList<FieldDescriptor> actual)
    {
        foreach (var extension in source)
        {
            Assert.Equal(extension.JsonName, actual.Single(f => f.Name == extension.Name && "." + f.ExtendeeType.FullName == extension.Extendee).JsonName);
        }
    }
}
