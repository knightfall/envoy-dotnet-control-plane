using System.Reflection;
using System.Text.Json.Nodes;
using Envoy.Config.Listener.V3;
using Google.Protobuf;

namespace Envoy.ControlPlane.PackageConsumers;

internal static class ConsumerChecks
{
    internal static void Verify(string package, bool hosting)
    {
        _ = Assembly.Load(package);
        var listener = new Listener { Name = "package-consumer" };
        if (Listener.Parser.ParseFrom(listener.ToByteArray()).Name != listener.Name)
        {
            throw new InvalidOperationException("Transitive Protocol serialization failed");
        }

        var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Envoy.ControlPlane", "Envoy.ControlPlane.Protocol", "Envoy.ControlPlane.Validation.Annotations",
            "Google.Protobuf", "Grpc.Core.Api", "Roslynator.Analyzers",
        };
        if (hosting)
        {
            expected.Add("Envoy.ControlPlane.AspNetCore");
        }
        var assets = JsonNode.Parse(File.ReadAllText("obj/project.assets.json"))!;
        var packages = assets["libraries"]!.AsObject().Select(item => item.Key.Split('/')[0]).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!packages.SetEquals(expected))
        {
            throw new InvalidOperationException("Unexpected consumer package graph: " + string.Join(", ", packages.Order()));
        }

        var runtime = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Consumer.runtimeconfig.json")))!["runtimeOptions"]!;
        var frameworks = runtime["frameworks"]?.AsArray().Select(item => item!["name"]!.GetValue<string>()).ToArray()
            ?? [runtime["framework"]!["name"]!.GetValue<string>()];
        if (frameworks.Contains("Microsoft.AspNetCore.App", StringComparer.Ordinal) != hosting)
        {
            throw new InvalidOperationException("ASP.NET shared-framework ownership differs from the package boundary");
        }
        Console.WriteLine($"{package}: loaded assembly, used transitive Protocol and verified its dependency/framework boundary.");
    }
}
