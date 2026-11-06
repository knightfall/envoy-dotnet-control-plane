using Envoy.Config.Core.V3;
using Envoy.ControlPlane.Validation.Tests.Support;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using ProtoValidate;
using Xunit;

namespace Envoy.ControlPlane.Validation.Tests;

public sealed class CatalogValidationTests
{
    [Fact]
    public void Catalog_AllGeneratedMessages_CompileAndEvaluateWithoutFaults()
    {
        var assembly = typeof(Address).Assembly;
        var messages = ValidationCorpus.Files.SelectMany(file => Enumerate(file.MessageTypes))
            .Where(message => message.ClrType?.Assembly == assembly).ToArray();
        Assert.Equal(1502, messages.Length);
        Assert.Equal(messages.Length, messages.Select(message => message.FullName).Distinct(StringComparer.Ordinal).Count());

        var validator = new Validator();
        var faults = new List<string>();
        foreach (var descriptor in messages)
        {
            try
            {
                // Required fields may reject defaults; compilation/execution must still succeed.
                validator.Validate(descriptor.Parser.ParseFrom(ByteString.Empty), failFast: false);
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                faults.Add(descriptor.FullName + ": " + error);
            }
        }
        Assert.Empty(faults);
    }

    private static IEnumerable<MessageDescriptor> Enumerate(IEnumerable<MessageDescriptor> messages)
    {
        foreach (var message in messages)
        {
            yield return message;
            foreach (var nested in Enumerate(message.NestedTypes))
            {
                yield return nested;
            }
        }
    }
}
