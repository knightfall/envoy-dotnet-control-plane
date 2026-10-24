using System.Text.Json.Nodes;
using Xunit;

namespace Envoy.ControlPlane.Upstream.Tests;

public sealed class UpstreamCliTests
{
    [Fact]
    public async Task Export_ValidFixture_WritesOnlySelectedVerifiedInputs()
    {
        using var fixture = new UpstreamFixture();
        var result = await fixture.Run("export", "--cache", fixture.Cache, "--offline");
        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.StandardError);
        string output = Path.Combine(fixture.Root, "src/Envoy.ControlPlane.Protocol/obj/upstream");
        string inputs = Path.Combine(output, "inputs");
        string[] expectedPaths = ["envoy/config/core/v3/base.proto", "ratelimit/config/ratelimit/v3/rls_conf.proto", "xds/type/v1/extension.proto"];
        var actualPaths = Directory.GetFiles(inputs, "*", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(inputs, p).Replace('\\', '/')).Order(StringComparer.Ordinal);
        Assert.Equal(expectedPaths, actualPaths);
        string exportedSource = await File.ReadAllTextAsync(Path.Combine(inputs, expectedPaths[0]), TestContext.Current.CancellationToken);
        Assert.Contains("message Node", exportedSource, StringComparison.Ordinal);
        Assert.Equal(4, Directory.GetFiles(Path.Combine(output, "licenses"), "LICENSE", SearchOption.AllDirectories).Length);
        string before = fixture.InputFingerprint();
        var repeated = await fixture.Run("export", "--cache", fixture.Cache, "--offline");
        Assert.Equal(0, repeated.ExitCode);
        Assert.Equal(before, fixture.InputFingerprint());
    }

    [Fact]
    public async Task Export_DriftedCatalog_RejectsBeforeWritingInputs()
    {
        using var fixture = new UpstreamFixture();
        fixture.InputLock["catalogSha256"] = new string('0', 64);
        fixture.SaveLock();
        string before = fixture.InputFingerprint();
        var result = await fixture.Run("export", "--cache", fixture.Cache, "--offline");
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Catalog checksum differs from lock", result.StandardError, StringComparison.Ordinal);
        Assert.Empty(result.StandardOutput);
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, "src")));
        Assert.Equal(before, fixture.InputFingerprint());
    }

    [Fact]
    public async Task Sync_ValidOfflineFixture_VerifiesClosureAndPreservesInputs()
    {
        using var fixture = new UpstreamFixture();
        string before = fixture.InputFingerprint();

        var result = await fixture.Sync();

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.StandardError);
        Assert.StartsWith("Source archives, licenses, module replacements, baseline and import closure agree.", result.StandardOutput, StringComparison.Ordinal);
        var actual = JsonNode.Parse(result.StandardOutput[result.StandardOutput.IndexOf('{')..])!;
        Assert.Equal(2, actual["roots"]!.GetValue<int>());
        Assert.Equal(3, actual["selected"]!.GetValue<int>());
        Assert.Equal(6, actual["inventory"]!.GetValue<int>());
        Assert.Equal(1, actual["selectedBySource"]!["xds"]!.GetValue<int>());
        Assert.Equal(before, fixture.InputFingerprint());
    }

    [Fact]
    public async Task Sync_MissingRequiredSource_IdentifiesSource()
    {
        using var fixture = new UpstreamFixture();
        fixture.InputLock["sources"]!.AsArray().Remove(fixture.Source("envoy"));
        fixture.SaveLock();

        await AssertRejected(fixture, "Missing required source: envoy");
    }

    [Fact]
    public async Task Sync_DuplicateSource_IdentifiesDuplicate()
    {
        using var fixture = new UpstreamFixture();
        fixture.InputLock["sources"]!.AsArray().Add(fixture.Source("envoy").DeepClone());
        fixture.SaveLock();

        await AssertRejected(fixture, "Duplicate source ID: envoy");
    }

    [Fact]
    public async Task Sync_AlteredSourcePin_RejectsBaselineMismatch()
    {
        using var fixture = new UpstreamFixture();
        fixture.Source("envoy")["commit"] = new string('f', 40);
        fixture.SaveLock();

        await AssertRejected(fixture, "Baseline/lock origin mismatch: envoy");
    }

    [Fact]
    public async Task Sync_FloatingSourceRevision_RejectsNonCommitInput()
    {
        using var fixture = new UpstreamFixture();
        fixture.Source("xds")["commit"] = "main";
        fixture.SaveLock();

        await AssertRejected(fixture, "Source must use a full commit: xds");
    }

    [Fact]
    public async Task Sync_AlteredArchiveChecksum_RejectsCachedArchive()
    {
        using var fixture = new UpstreamFixture();
        fixture.Source("envoy")["archive"]!["sha256"] = new string('0', 64);
        fixture.SaveLock();

        var result = await AssertRejected(fixture, "Cached checksum mismatch:");
        Assert.Contains(Path.GetFileName(fixture.ArchivePath("envoy")), result.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sync_AlteredLicenseChecksum_RejectsNotice()
    {
        using var fixture = new UpstreamFixture();
        fixture.Source("envoy")["notices"]![0]!["sha256"] = new string('0', 64);
        fixture.SaveLock();

        await AssertRejected(fixture, "License/notice checksum mismatch: envoy");
    }

    [Fact]
    public async Task Sync_MissingLicenseEntry_RejectsIncompleteNoticeInventory()
    {
        using var fixture = new UpstreamFixture();
        fixture.Source("envoy")["notices"]!.AsArray().Clear();
        fixture.SaveLock();

        await AssertRejected(fixture, "Incomplete root license/notice inventory: envoy");
    }

    [Theory]
    [InlineData("remove")]
    [InlineData("change")]
    public async Task Sync_ModifiedCatalogWithMatchingFingerprint_RejectsSemanticDrift(string mutation)
    {
        using var fixture = new UpstreamFixture();
        var files = fixture.Catalog["files"]!.AsArray();
        if (mutation == "remove") files.RemoveAt(0);
        else files[0]!["sha256"] = new string('0', 64);
        fixture.SaveCatalog(); // Keep the file and its lock fingerprint consistent.

        await AssertRejected(fixture, "Catalog differs from pinned source inventory");
    }

    [Fact]
    public async Task Sync_AlteredCatalogFingerprint_RejectsLockDrift()
    {
        using var fixture = new UpstreamFixture();
        fixture.InputLock["catalogSha256"] = new string('0', 64);
        fixture.SaveLock();

        await AssertRejected(fixture, "Catalog checksum differs from lock");
    }

    [Fact]
    public async Task Sync_UnresolvedImport_IdentifiesImportPath()
    {
        using var fixture = new UpstreamFixture();
        fixture.ReplaceSourceFile("envoy", "api/" + UpstreamFixture.RootProto,
            "syntax = \"proto3\";\npackage envoy.config.core.v3;\nimport \"missing/v1/resource.proto\";\n");

        await AssertRejected(fixture, "Unresolved import: missing/v1/resource.proto");
    }

    [Fact]
    public async Task Sync_MissingTransitiveSource_IdentifiesUnownedImport()
    {
        using var fixture = new UpstreamFixture();
        fixture.InputLock["sources"]!.AsArray().Remove(fixture.Source("xds"));
        fixture.SaveLock();

        await AssertRejected(fixture, "Unresolved import: " + UpstreamFixture.DependencyProto);
    }

    [Fact]
    public async Task Sync_MissingRegistryEvidence_RejectsManifestGap()
    {
        using var fixture = new UpstreamFixture();
        fixture.Source("xds")["evidence"]!.AsArray().Clear();
        fixture.SaveLock();

        await AssertRejected(fixture, "Registry source evidence differs from Envoy's lock: xds");
    }

    [Fact]
    public async Task Sync_MissingRegistryPatch_RejectsPatchGap()
    {
        using var fixture = new UpstreamFixture();
        fixture.Source("xds")["registryPatches"]!.AsArray().Clear();
        fixture.SaveLock();

        await AssertRejected(fixture, "Registry patch inventory differs from source manifest");
    }

    [Fact]
    public async Task Sync_AlteredModuleReplacement_RejectsModuleDrift()
    {
        using var fixture = new UpstreamFixture();
        fixture.InputLock["goModules"]![0]!["replacements"]![0] = "example.test/import => ../different";
        fixture.SaveLock();

        await AssertRejected(fixture, "Go module inventory differs from lock");
    }

    [Fact]
    public async Task Sync_MissingOfflineArchive_ReportsMissingArtifact()
    {
        using var fixture = new UpstreamFixture();
        File.Delete(fixture.ArchivePath("envoy"));

        var result = await AssertRejected(fixture, "Missing cached artifact:");
        Assert.Contains(Path.GetFileName(fixture.ArchivePath("envoy")), result.StandardError, StringComparison.Ordinal);
    }

    private static async Task<CommandResult> AssertRejected(UpstreamFixture fixture, string expectedDiagnostic)
    {
        string before = fixture.InputFingerprint();

        var result = await fixture.Sync();

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(expectedDiagnostic, result.StandardError, StringComparison.Ordinal);
        Assert.Empty(result.StandardOutput);
        Assert.Equal(before, fixture.InputFingerprint());
        return result;
    }
}
