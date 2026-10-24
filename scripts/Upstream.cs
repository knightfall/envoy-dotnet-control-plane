#!/usr/bin/env -S dotnet --
#:property PublishAot=false
#:property TreatWarningsAsErrors=true
#:package Google.Protobuf
#:package Roslynator.Analyzers
#:include upstream/UpstreamData.cs
#:include upstream/RepositoryPaths.cs
#:include upstream/SchemaExporter.cs
#:include upstream/SourceArchives.cs
#:include upstream/GoModuleInventory.cs
#:include upstream/SchemaCatalog.cs
#:include upstream/SchemaInspector.cs
#:include upstream/UpstreamOptions.cs
#:include upstream/SourceBaseline.cs

using System.Text.Json.Nodes;
using static Envoy.ControlPlane.Tools.UpstreamData;

namespace Envoy.ControlPlane.Tools;

internal static class Upstream
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length == 0 || args.Contains("--help"))
            {
                Console.WriteLine("Upstream.cs <sync|inspect|export> --cache <external-directory> [--offline] [--write-catalog]");
                return 0;
            }
            var options = UpstreamOptions.Parse(args);
            string root = RepositoryPaths.FindRepositoryRoot();
            string cache = RepositoryPaths.ResolveDirectoryLinks(Path.GetFullPath(options.CacheDirectory));
            foreach (string repository in new[] { root, Path.Combine(Path.GetDirectoryName(root)!, "go-control-plane") })
            {
                Require(!RepositoryPaths.IsWithinDirectory(cache, RepositoryPaths.ResolveDirectoryLinks(repository)),
                    "Use a cache outside both repositories");
            }

            var inputLock = ReadJsonObject(Path.Combine(root, "schemas/upstream.lock.json"));
            var lockedSources = SourceBaseline.Validate(root, inputLock);
            var sources = await SourceArchives.RetrieveAsync(inputLock, cache, options.Offline);
            var envoy = lockedSources["envoy"];
            Require(DecodeUtf8(sources["go-control-plane"]["envoy/COMMIT"]).Trim() == GetString(envoy, "commit"), "Go's Envoy pin differs from lock");
            Require(JsonNode.DeepEquals(GoModuleInventory.Create(sources["go-control-plane"]), inputLock["goModules"]), "Go module inventory differs from lock");
            var catalog = SchemaCatalog.Create(inputLock, sources);
            string catalogPath = Path.Combine(root, "schemas/catalog.json");
            if (options.WriteCatalog)
            {
                await File.WriteAllBytesAsync(catalogPath, SerializeJson(catalog));
                Console.WriteLine("Wrote schemas/catalog.json; review it and update catalogSha256 in upstream.lock.json.");
            }
            else
            {
                Require(JsonNode.DeepEquals(catalog, ReadJsonObject(catalogPath)), "Catalog differs from pinned source inventory");
                Require(ComputeSha256(await File.ReadAllBytesAsync(catalogPath)) == GetString(inputLock, "catalogSha256"), "Catalog checksum differs from lock");
                Console.WriteLine("Source archives, licenses, module replacements, baseline and import closure agree.");
            }
            Console.WriteLine(catalog["counts"]!.ToJsonString(SerializationOptions));
            if (options.Command == "inspect")
            {
                await SchemaInspector.InspectAsync(inputLock, sources, catalog, cache, options.Offline);
            }
            if (options.Command == "export")
            {
                SchemaExporter.ExportVerifiedInputs(root, inputLock, sources, catalog);
            }
            return 0;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }
}
