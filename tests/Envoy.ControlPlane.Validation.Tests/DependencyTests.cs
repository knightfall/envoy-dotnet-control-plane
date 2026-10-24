using System.Reflection;
using Envoy.Config.Core.V3;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using ProtoValidate;
using Xunit;

namespace Envoy.ControlPlane.Validation.Tests;

public sealed class DependencyTests
{
    [Fact]
    public void Dependencies_MessageDefinitions_HaveSingleOwner()
    {
        var assemblies = new Dictionary<string, Assembly>(StringComparer.Ordinal);
        var pending = new Stack<Assembly>([typeof(Validator).Assembly, typeof(Address).Assembly]);
        while (pending.TryPop(out var assembly))
        {
            if (!assemblies.TryAdd(assembly.FullName!, assembly))
            {
                continue;
            }
            foreach (var reference in assembly.GetReferencedAssemblies())
            {
                if (reference.Name?.StartsWith("System", StringComparison.Ordinal) != true
                    && reference.Name is not "netstandard" and not "mscorlib")
                {
                    pending.Push(Assembly.Load(reference));
                }
            }
        }

        var duplicates = assemblies.Values.SelectMany(GetMessageDefinitions)
            .GroupBy(definition => definition.ProtoName, StringComparer.Ordinal)
            .Where(group => group.Select(definition => definition.ClrName).Distinct(StringComparer.Ordinal).Count() > 1)
            .Select(group => group.Key + ": " + string.Join(", ", group.Select(definition => definition.ClrName)));
        Assert.Empty(duplicates);
    }

    [Fact]
    public void Dependencies_ProtocolAndAnnotations_DoNotReferenceEvaluatorOrCel()
    {
        var protocol = typeof(Address).Assembly;
        var annotations = typeof(Buf.Validate.FieldRules).Assembly;
        Assert.Equal("Envoy.ControlPlane.Validation", typeof(Validator).Assembly.GetName().Name);
        Assert.Equal("Envoy.ControlPlane.Validation.Annotations", annotations.GetName().Name);
        Assert.Contains(protocol.GetReferencedAssemblies(), reference => reference.Name == "Envoy.ControlPlane.Validation.Annotations");
        foreach (var assembly in new[] { protocol, annotations })
        {
            Assert.DoesNotContain(assembly.GetReferencedAssemblies(), reference => reference.Name is
                "Envoy.ControlPlane.Validation" or "Cel" or "ProtoValidate" or "Celly" or "Celly.Protovalidate");
        }
        Assert.DoesNotContain(typeof(Validator).Assembly.GetReferencedAssemblies(), reference => reference.Name is
            "ProtoValidate" or "Celly.Protovalidate" or "Envoy.ControlPlane.Protocol");
    }

    private static IEnumerable<(string ProtoName, string ClrName)> GetMessageDefinitions(Assembly assembly)
    {
        foreach (var type in assembly.GetTypes().Where(type => typeof(IMessage).IsAssignableFrom(type) && !type.IsAbstract))
        {
            var property = type.GetProperties(BindingFlags.Public | BindingFlags.Static)
                .SingleOrDefault(property => property.PropertyType == typeof(MessageDescriptor));
            if (property?.GetValue(null) is MessageDescriptor descriptor)
            {
                yield return (descriptor.FullName, type.FullName + ", " + assembly.GetName().Name);
            }
        }
    }
}
