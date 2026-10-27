using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using static Envoy.ControlPlane.Tools.BindingIntegrity;

namespace Envoy.ControlPlane.Tools;

internal static class PackageContentsVerification
{
    internal static void Verify()
    {
        var versions = XDocument.Load("Directory.Packages.props").Descendants("PackageVersion")
            .ToDictionary(item => item.Attribute("Include")!.Value, item => item.Attribute("Version")!.Value, StringComparer.Ordinal);
        string[] assemblies = ["Envoy.ControlPlane", "Envoy.ControlPlane.AspNetCore", "Envoy.ControlPlane.Protocol",
            "Envoy.ControlPlane.Validation.Annotations", "Envoy.ControlPlane.Validation"];
        foreach (string assembly in assemblies)
        {
            string packageId = "Knightfall." + assembly;
            string version = versions[packageId];
            string path = Path.Combine(BindingPaths.PackageDirectory, packageId + "." + version);
            using var archive = ZipFile.OpenRead(path + ".nupkg");
            foreach (string file in new[] { $"lib/net10.0/{assembly}.dll", "README.md", "licenses/LICENSE" })
            {
                Require(archive.GetEntry(file) is not null, "Missing package content: " + packageId + "/" + file);
            }
            // The fingerprinted evaluator source retains its upstream documentation policy.
            if (assembly != "Envoy.ControlPlane.Validation")
            {
                Require(archive.GetEntry($"lib/net10.0/{assembly}.xml") is not null, "Missing XML documentation: " + packageId);
            }
            using var manifest = archive.Entries.Single(entry => entry.FullName.EndsWith(".nuspec", StringComparison.Ordinal)).Open();
            var metadata = XDocument.Load(manifest);
            Require(metadata.Descendants().Single(element => element.Name.LocalName == "id").Value == packageId,
                "Unexpected package identity: " + packageId);
            Require(metadata.Descendants().Single(element => element.Name.LocalName == "version").Value == version,
                "Unexpected package version: " + packageId);
            Require(metadata.Descendants().Single(element => element.Name.LocalName == "license").Value == "Apache-2.0",
                "Unexpected package license: " + packageId);
            VerifyNotices(archive, assembly);
            using var symbols = ZipFile.OpenRead(path + ".snupkg");
            Require(symbols.GetEntry($"lib/net10.0/{assembly}.pdb") is not null, "Missing portable symbols: " + packageId);
        }
        Console.WriteLine("All five package archives contain their assembly, readme, exact locked notices and separate symbols; authored/generated contracts include XML documentation.");
    }

    private static void VerifyNotices(ZipArchive archive, string package)
    {
        VerifyFile(archive, "licenses/LICENSE", GetFileSha256("LICENSE"));
        var sources = JsonNode.Parse(File.ReadAllText("schemas/upstream.lock.json"))!["sources"]!.AsArray();
        if (package == "Envoy.ControlPlane.Protocol")
        {
            foreach (var source in sources)
            {
                foreach (var notice in source!["notices"]!.AsArray())
                {
                    VerifyFile(archive, "licenses/" + source["id"]!.GetValue<string>() + "/" + notice!["path"]!.GetValue<string>(),
                        notice["sha256"]!.GetValue<string>());
                }
            }
            foreach (string name in new[] { "NOTICES.md", "upstream.lock.json", "catalog.json", "bindings.lock.json", "annotations.json" })
            {
                VerifyFile(archive, "schemas/" + name, GetFileSha256("schemas/" + name));
            }
        }
        if (package == "Envoy.ControlPlane")
        {
            var source = sources.Single(source => source!["id"]!.GetValue<string>() == "go-control-plane")!;
            VerifyFile(archive, "licenses/upstream/LICENSE", source["notices"]![0]!["sha256"]!.GetValue<string>());
        }
        if (package is "Envoy.ControlPlane.Validation" or "Envoy.ControlPlane.Validation.Annotations")
        {
            var runtime = JsonNode.Parse(File.ReadAllText(ValidationPaths.SourcesLock))!["runtime"]!;
            VerifyFile(archive, "licenses/LICENSE.upstream", runtime["licenseSha256"]!.GetValue<string>());
            VerifyFile(archive, "schemas/sources.lock.json", GetFileSha256(ValidationPaths.SourcesLock));
            if (package == "Envoy.ControlPlane.Validation.Annotations")
            {
                VerifyFile(archive, "schemas/buf/validate/validate.proto", runtime["annotationSchemaSha256"]!.GetValue<string>());
            }
        }
    }

    internal static void VerifyFile(ZipArchive archive, string path, string expectedHash)
    {
        var entry = archive.GetEntry(path) ?? throw new InvalidOperationException("Missing packaged notice/input: " + path);
        using var stream = entry.Open();
        Require(Convert.ToHexStringLower(SHA256.HashData(stream)) == expectedHash, "Packaged notice/input differs from source lock: " + path);
    }
}
