using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace Envoy.ControlPlane.Integration.Tests.Smoke;

public sealed class SmokeBuildArtifacts : IAsyncLifetime
{
    internal const string HostingPackage = "Knightfall.Envoy.ControlPlane.AspNetCore";
    internal const string ArtifactsEnvironmentVariable = "ENVOY_SMOKE_ARTIFACTS";
    private static readonly string[] PackageIds =
    [
        HostingPackage, "Knightfall.Envoy.ControlPlane", "Knightfall.Envoy.ControlPlane.Protocol",
        "Knightfall.Envoy.ControlPlane.Validation.Annotations",
    ];
    private DirectoryInfo? workspace;

    internal string ExampleDirectory { get; private set; } = "";
    internal string ConsumerDirectory { get; private set; } = "";
    internal string FeedDirectory { get; private set; } = "";
    internal string PackageVersion { get; private set; } = "";
    internal Dictionary<string, string> FreshPackages { get; } = new(StringComparer.Ordinal);

    public async ValueTask InitializeAsync()
    {
        workspace = Directory.CreateTempSubdirectory("envoy-smoke-");
        string repository = FindRepository();
        string example = Path.Combine(repository, "examples", "Envoy.ControlPlane.Example");
        string? prepared = Environment.GetEnvironmentVariable(ArtifactsEnvironmentVariable);
        string artifacts = string.IsNullOrWhiteSpace(prepared) ? Path.Combine(workspace.FullName, "artifacts") : Path.GetFullPath(prepared);
        ExampleDirectory = Path.Combine(artifacts, "example");
        string packages = Path.Combine(artifacts, "packages");
        if (string.IsNullOrWhiteSpace(prepared))
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(5));
            await RunDotnetAsync(repository, timeout.Token, "restore", "Envoy.ControlPlane.slnx", "--locked-mode");
            await RunDotnetAsync(repository, timeout.Token, "pack", "Envoy.ControlPlane.slnx", "--configuration", "Release",
                "--no-restore", "-p:WarnOnPackingNonPackableProject=false", "--output", packages);
            await RunDotnetAsync(repository, timeout.Token, "publish", example, "--configuration", "Release",
                "--no-restore", "--output", ExampleDirectory);
        }
        if (!File.Exists(Path.Combine(ExampleDirectory, "Envoy.ControlPlane.Example.dll")))
        {
            throw new InvalidDataException($"The published Example assembly is missing from '{ExampleDirectory}'.");
        }

        string hosting = Directory.GetFiles(packages, HostingPackage + ".*.nupkg").Single();
        PackageVersion = Path.GetFileNameWithoutExtension(hosting)[(HostingPackage.Length + 1)..];
        FeedDirectory = workspace.CreateSubdirectory("feed").FullName;
        foreach (string package in PackageIds)
        {
            string fileName = $"{package}.{PackageVersion}.nupkg";
            string packed = Path.Combine(packages, fileName);
            FreshPackages.Add(package, packed);
            File.Copy(packed, Path.Combine(FeedDirectory, fileName));
        }
        CopyLockedDependencies(Path.Combine(example, "obj", "project.assets.json"), FeedDirectory);

        ConsumerDirectory = workspace.CreateSubdirectory("consumer").FullName;
        foreach (string name in new[] { "Program.cs", "BackendPublisher.cs", "ExampleResources.cs" })
        {
            File.Copy(Path.Combine(example, name), Path.Combine(ConsumerDirectory, name));
        }
        await File.WriteAllTextAsync(Path.Combine(ConsumerDirectory, "Consumer.csproj"), $$"""
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
                <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
                <NuGetAuditMode>all</NuGetAuditMode>
                <NuGetAuditLevel>moderate</NuGetAuditLevel>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="{{HostingPackage}}" Version="[{{PackageVersion}}]" />
              </ItemGroup>
            </Project>
            """, TestContext.Current.CancellationToken);
    }

    internal DirectoryInfo CreateSettingsDirectory() => workspace!.CreateSubdirectory(Guid.NewGuid().ToString("N"));

    public ValueTask DisposeAsync()
    {
        workspace?.Delete(recursive: true);
        return ValueTask.CompletedTask;
    }

    private static string FindRepository()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Envoy.ControlPlane.slnx")))
            {
                return directory.FullName;
            }
        }
        throw new DirectoryNotFoundException("The Envoy integration tests must run from a built repository checkout.");
    }

    private static void CopyLockedDependencies(string assetsPath, string feed)
    {
        using var assets = JsonDocument.Parse(File.ReadAllBytes(assetsPath));
        var root = assets.RootElement;
        string[] cacheRoots = root.GetProperty("packageFolders").EnumerateObject().Select(folder => folder.Name).ToArray();
        foreach (var library in root.GetProperty("libraries").EnumerateObject())
        {
            if (library.Value.GetProperty("type").GetString() != "package")
            {
                continue;
            }
            string relative = library.Value.GetProperty("path").GetString()!;
            // The locked Example restore has already audited these exact dependency versions.
            string folder = cacheRoots.Select(cache => Path.Combine(cache, relative)).First(Directory.Exists);
            string package = Directory.GetFiles(folder, "*.nupkg").Single();
            File.Copy(package, Path.Combine(feed, Path.GetFileName(package)));
        }
    }

    private static async Task RunDotnetAsync(string repository, CancellationToken cancellationToken, params string[] arguments)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = repository,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the .NET SDK.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(stdout, stderr);
            throw;
        }
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"dotnet {arguments[0]} failed with exit code {process.ExitCode}.\n{await stdout}\n{await stderr}");
        }
        await Task.WhenAll(stdout, stderr);
    }
}
