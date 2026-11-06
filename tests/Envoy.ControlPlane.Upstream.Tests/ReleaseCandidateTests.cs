using System.IO.Compression;
using System.Text.Json.Nodes;
using Envoy.ControlPlane.Tools;
using Xunit;

namespace Envoy.ControlPlane.Upstream.Tests;

public sealed class ReleaseCandidateTests
{
    private const string Commit = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Theory]
    [InlineData("id")]
    [InlineData("version")]
    [InlineData("commit")]
    [InlineData("repository")]
    [InlineData("traversal")]
    public void PackageOutsideDeclaredRelease_IsRejected(string change)
    {
        using var fixture = new UpstreamFixture();
        string path = CreatePackage(fixture.Root, change);

        Assert.Throws<InvalidOperationException>(() => ReleaseCandidate.VerifyArchive(path,
            "Knightfall.Envoy.ControlPlane", "1.0.0-rc.1", Commit));
    }

    [Fact]
    public void SourceBoundPackageAndManifest_RetainIdentityAndFileDigests()
    {
        using var fixture = new UpstreamFixture();
        string path = CreatePackage(fixture.Root, "none");
        ReleaseCandidate.VerifyArchive(path, "Knightfall.Envoy.ControlPlane", "1.0.0-rc.1", Commit);
        File.WriteAllText(Path.Combine(fixture.Root, "source-input.txt"), "abc");

        var manifest = ReleaseCandidate.CreateManifest(fixture.Root, "1.0.0-rc.1", Commit);

        Assert.Equal(Commit, manifest["commit"]!.GetValue<string>());
        Assert.Equal("1.0.0-rc.1", manifest["version"]!.GetValue<string>());
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", manifest["files"]!["source-input.txt"]!.GetValue<string>());
        Assert.Contains("package.nupkg", manifest["files"]!.AsObject().Select(item => item.Key));
    }

    [Fact]
    public void SourceSbom_RecordsPinnedArchivesIncludingAdaptedEvaluator()
    {
        var upstream = JsonNode.Parse("""
            {"sources":[{"id":"envoy","commit":"envoy-commit","license":"Apache-2.0",
              "archive":{"url":"https://example.test/envoy.tar.gz","sha256":"envoy-archive-hash"}}]}
            """)!;
        var validation = JsonNode.Parse("""
            {"runtime":{"commit":"runtime-commit","archiveUrl":"https://example.test/runtime.tar.gz","archiveSha256":"runtime-archive-hash"},
             "migration":{"commit":"migration-commit","archiveUrl":"https://example.test/migration.tar.gz","archiveSha256":"migration-archive-hash"}}
            """)!;

        var document = SourceSbom.Create(upstream, validation, "1.0.0-rc.1", Commit);

        Assert.Equal("SPDX-2.2", document["spdxVersion"]!.GetValue<string>());
        var packages = document["packages"]!.AsArray();
        Assert.Equal(4, packages.Count);
        var evaluator = Assert.Single(packages, item => item!["SPDXID"]!.GetValue<string>() == "SPDXRef-Validation-runtime")!;
        Assert.Equal("runtime-commit", evaluator["versionInfo"]!.GetValue<string>());
        Assert.Equal("runtime-archive-hash", evaluator["checksums"]![0]!["checksumValue"]!.GetValue<string>());
        Assert.Equal(3, document["relationships"]!.AsArray().Count);
        Assert.All(document["relationships"]!.AsArray(), item => Assert.Equal("GENERATED_FROM", item!["relationshipType"]!.GetValue<string>()));
    }

    private static string CreatePackage(string directory, string change)
    {
        string path = Path.Combine(directory, "package.nupkg");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        using var writer = new StreamWriter(archive.CreateEntry("package.nuspec").Open());
        string id = change == "id" ? "Attacker.Package" : "Knightfall.Envoy.ControlPlane";
        string version = change == "version" ? "9.9.9" : "1.0.0-rc.1";
        string commit = change == "commit" ? "wrong-commit" : Commit;
        string repository = change == "repository" ? "https://example.test/fork" : "https://github.com/knightfall/envoy-dotnet-control-plane";
        writer.Write($"<package><metadata><id>{id}</id><version>{version}</version><repository url=\"{repository}\" commit=\"{commit}\"/></metadata></package>");
        writer.Dispose();
        if (change == "traversal") archive.CreateEntry("../outside");
        return path;
    }
}
