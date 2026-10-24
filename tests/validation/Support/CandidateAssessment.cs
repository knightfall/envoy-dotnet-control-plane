using System.Reflection;
using System.Text.Json;
using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace Envoy.ControlPlane.Validation.Proof;

internal static class CandidateAssessment
{
    internal static int Run(string[] args, Assembly provider, Func<IMessage, int> validate)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("Usage: <candidate-proof> <cases-json> <output-json>");
            return 1;
        }

        var results = new List<object>();
        foreach (var (item, message) in ValidationCorpus.Read(args[0]))
        {
            try
            {
                int count = validate(message);
                results.Add(new { item.Id, item.Category, item.Type, ExpectedValid = item.Valid,
                    ActualValid = count == 0, Violations = count, Error = (string?)null,
                    Data = Convert.ToBase64String(message.ToByteArray()) });
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                results.Add(new { item.Id, item.Category, item.Type, ExpectedValid = item.Valid,
                    ActualValid = (bool?)null, Violations = (int?)null,
                    Error = error.GetType().FullName + ": " + error.Message,
                    Data = Convert.ToBase64String(message.ToByteArray()) });
            }
        }

        // Traverse the provider's loaded dependencies as well as Protocol: different CLR
        // namespaces can still define the same protobuf message name.
        var assemblies = new Dictionary<string, Assembly>(StringComparer.Ordinal);
        AddAssembly(provider);
        AddAssembly(typeof(Envoy.Config.Core.V3.Address).Assembly);
        var definitions = assemblies.Values.SelectMany(GetMessageDefinitions).ToArray();
        var duplicates = definitions.GroupBy(definition => definition.ProtoName, StringComparer.Ordinal)
            .Where(group => group.Select(item => item.ClrName).Distinct(StringComparer.Ordinal).Count() > 1)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new { ProtoName = group.Key, Owners = group.Select(item => item.ClrName).ToArray() })
            .ToArray();
        var report = new { Provider = provider.GetName().Name, Results = results, DuplicateMessages = duplicates };
        File.WriteAllText(args[1], JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) + "\n");
        Console.WriteLine($"Assessed {results.Count} cases; {duplicates.Length} duplicate protobuf message names. See {args[1]}.");
        return 0;

        void AddAssembly(Assembly assembly)
        {
            if (!assemblies.TryAdd(assembly.FullName!, assembly))
            {
                return;
            }
            foreach (var reference in assembly.GetReferencedAssemblies())
            {
                if (reference.Name?.StartsWith("System", StringComparison.Ordinal) != true
                    && reference.Name is not "netstandard" and not "mscorlib")
                {
                    AddAssembly(Assembly.Load(reference));
                }
            }
        }
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
