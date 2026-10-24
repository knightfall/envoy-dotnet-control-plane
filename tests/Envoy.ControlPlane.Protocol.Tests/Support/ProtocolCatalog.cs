using System.Reflection;
using Envoy.Config.Listener.V3;
using Google.Protobuf.Reflection;
using Xunit;

namespace Envoy.ControlPlane.Protocol.Tests;

internal static class ProtocolCatalog
{
    internal static readonly Assembly ProtocolAssembly = typeof(Listener).Assembly;
    internal static readonly Lazy<FileDescriptor[]> Files = new(LoadFiles);

    internal static FileDescriptor[] LoadFiles()
    {
        var files = new Dictionary<string, FileDescriptor>(StringComparer.Ordinal);
        void Add(FileDescriptor file)
        {
            if (files.TryGetValue(file.Name, out var existing)) { Assert.Same(existing, file); return; }
            files.Add(file.Name, file);
            foreach (var dependency in file.Dependencies)
            {
                Add(dependency);
            }
        }
        foreach (var property in ProtocolAssembly.GetTypes().SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Static))
                     .Where(p => p.PropertyType == typeof(FileDescriptor)))
        {
            Add((FileDescriptor)property.GetValue(null)!);
        }
        return files.Values.OrderBy(f => f.Name, StringComparer.Ordinal).ToArray();
    }

    internal static IEnumerable<MessageDescriptor> EnumerateMessages(IList<MessageDescriptor> messages) =>
            messages.SelectMany(m => new[] { m }.Concat(EnumerateMessages(m.NestedTypes)));
    internal static IEnumerable<(string Name, DescriptorProto Message)> EnumerateSourceMessages(string prefix, IEnumerable<DescriptorProto> messages) =>
            messages.SelectMany(m => new[] { (prefix + "." + m.Name, m) }.Concat(EnumerateSourceMessages(prefix + "." + m.Name, m.NestedType)));

    internal static byte[] ReadEmbeddedResource(string name)
    {
        using var stream = ProtocolAssembly.GetManifestResourceStream("Envoy.ControlPlane.Protocol." + name) ?? throw new InvalidOperationException("Missing proof resource: " + name);
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return bytes.ToArray();
    }
}
