using System.IO.Compression;
using System.Xml.Linq;
using static Envoy.ControlPlane.Tools.BindingIntegrity;

namespace Envoy.ControlPlane.Tools;

internal static class PackageContentsVerification
{
    internal static void Verify()
    {
        var versions = XDocument.Load("Directory.Packages.props").Descendants("PackageVersion")
            .ToDictionary(item => item.Attribute("Include")!.Value, item => item.Attribute("Version")!.Value, StringComparer.Ordinal);
        string[] packages = ["Envoy.ControlPlane", "Envoy.ControlPlane.AspNetCore", "Envoy.ControlPlane.Protocol",
            "Envoy.ControlPlane.Validation.Annotations", "Envoy.ControlPlane.Validation"];
        foreach (string package in packages)
        {
            string path = Path.Combine(BindingPaths.PackageDirectory, package + "." + versions[package]);
            using var archive = ZipFile.OpenRead(path + ".nupkg");
            foreach (string file in new[] { $"lib/net10.0/{package}.dll", "README.md", "licenses/LICENSE" })
            {
                Require(archive.GetEntry(file) is not null, "Missing package content: " + package + "/" + file);
            }
            // The fingerprinted evaluator source retains its upstream documentation policy.
            if (package != "Envoy.ControlPlane.Validation")
            {
                Require(archive.GetEntry($"lib/net10.0/{package}.xml") is not null, "Missing XML documentation: " + package);
            }
            using var manifest = archive.Entries.Single(entry => entry.FullName.EndsWith(".nuspec", StringComparison.Ordinal)).Open();
            var metadata = XDocument.Load(manifest);
            Require(metadata.Descendants().Single(element => element.Name.LocalName == "license").Value == "Apache-2.0",
                "Unexpected package license: " + package);
            using var symbols = ZipFile.OpenRead(path + ".snupkg");
            Require(symbols.GetEntry($"lib/net10.0/{package}.pdb") is not null, "Missing portable symbols: " + package);
        }
        Console.WriteLine("All five package archives contain their assembly, readme, license and separate symbols; authored/generated contracts include XML documentation.");
    }
}
