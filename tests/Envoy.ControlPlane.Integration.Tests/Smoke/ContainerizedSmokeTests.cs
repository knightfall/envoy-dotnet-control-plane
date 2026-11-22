using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Envoy.ControlPlane.Integration.Tests.Harness;
using Xunit;
using static Envoy.ControlPlane.Integration.Tests.Harness.LiveEnvoyScenario;

namespace Envoy.ControlPlane.Integration.Tests.Smoke;

public sealed class ContainerizedSmokeTests(SmokeBuildArtifacts artifacts, ITestOutputHelper output) : IClassFixture<SmokeBuildArtifacts>
{
    private const int ManagementPort = 18000;
    private const int ListenerPort = 10000;
    private const string ConfigDirectory = "/smoke-settings";
    private const string ConsumerSource = "/smoke/consumer";
    private const string ConsumerNuGet = "/smoke/nuget";
    private const string ProbePath = "/smoke";

    [Fact]
    public Task PublishedExample_ReloadsTheBackendAddressAndStopsCleanly() => RunAsync(usePackages: false);

    [Fact]
    public Task PackedAspNetCoreConsumer_RestoresFreshPackagesAndReloadsTheBackendAddress() => RunAsync(usePackages: true);

    private async Task RunAsync(bool usePackages)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(4));
        var token = timeout.Token;
        var images = EnvoyImageLock.Load();
        await using var network = new NetworkBuilder().Build();
        await network.CreateAsync(token);
        await using var backendA = new EnvoyBackendContainer(images, network, "A");
        await using var backendB = new EnvoyBackendContainer(images, network, "B");
        await Task.WhenAll(backendA.StartAsync(token), backendB.StartAsync(token));

        var settings = artifacts.CreateSettingsDirectory();
        await WriteSettingsAsync(settings, backendA.Address, token);
        string alias = usePackages ? "packed-control-plane" : "example-control-plane";
        await using var management = CreateControlPlane(images, network, alias, settings, usePackages);
        string bootstrap = EnvoyProxyContainer.RenderAdsBootstrap("node", ManagementPort, managementHost: alias);
        await using var proxy = new EnvoyProxyContainer(images, network, bootstrap, [ListenerPort],
            listenerDrainSeconds: null, connectToHost: false);
        using var client = new HttpClient();
        client.DefaultRequestHeaders.ConnectionClose = true;
        try
        {
            await management.StartAsync(token);
            if (usePackages)
            {
                await VerifyFreshPackagesAsync(management, token);
            }

            await proxy.RunWithDiagnosticsAsync(output, async () =>
            {
                var address = new Uri(proxy.ListenerAddress(ListenerPort), ProbePath);
                await ExpectTrafficAsync(client, address, "A:/smoke", token);
                await VerifyManagementAddressAsync(proxy, alias, token);
                await VerifyEndpointsAsync(proxy, backendA.Address, token);

                // Both backends use the same port. Only the configured backend address changes.
                await WriteSettingsAsync(settings, backendB.Address, token);
                await ExpectTrafficAsync(client, address, "B:/smoke", token);
                await VerifyEndpointsAsync(proxy, backendB.Address, token);

                await management.StopAsync(token);
                Assert.Equal(0, await management.GetExitCodeAsync(token));
                await ExpectTrafficAsync(client, address, "B:/smoke", token);
                await proxy.StopAsync(token);
            }, token);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            using var diagnostics = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                var (stdout, stderr) = await management.GetLogsAsync(ct: diagnostics.Token);
                output.WriteLine($"Containerized control plane stdout:\n{stdout}\nContainerized control plane stderr:\n{stderr}");
            }
            catch (Exception logError) when (logError is not OutOfMemoryException)
            {
                output.WriteLine($"Control-plane logs unavailable: {logError.Message}");
            }
            throw;
        }
    }

    private IContainer CreateControlPlane(EnvoyImageLock images, INetwork network, string alias,
        DirectoryInfo settings, bool usePackages)
    {
        var builder = new ContainerBuilder(images.SdkIndex)
            .WithNetwork(network)
            .WithNetworkAliases(alias)
            .WithBindMount(settings.FullName, ConfigDirectory, AccessMode.ReadOnly)
            .WithWorkingDirectory(ConfigDirectory)
            .WithEnvironment("DOTNET_CLI_TELEMETRY_OPTOUT", "1")
            .WithEnvironment("DOTNET_USE_POLLING_FILE_WATCHER", "1")
            .WithEnvironment("ASPNETCORE_HTTP_PORTS", "")
            .WithEnvironment("Kestrel__Endpoints__Grpc__Url", "http://0.0.0.0:18000")
            .WithEnvironment("Kestrel__Endpoints__Grpc__Protocols", "Http2")
            .WithEnvironment("ListenerAddress", "0.0.0.0")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Now listening on: http://0.0.0.0:18000",
                wait => wait.WithTimeout(TimeSpan.FromMinutes(2))));

        if (usePackages)
        {
            // Restore solely from the copied, locked dependency archives and the freshly packed local packages.
            builder = builder.WithResourceMapping(DirectoryPath.Of(artifacts.ConsumerDirectory), DirectoryPath.Of(ConsumerSource))
                .WithResourceMapping(DirectoryPath.Of(artifacts.FeedDirectory), DirectoryPath.Of("/smoke/feed"))
                .WithEnvironment("NUGET_PACKAGES", ConsumerNuGet)
                .WithEntrypoint("/bin/sh", "-ec")
                .WithCommand("""
                    test ! -e /smoke/nuget
                    dotnet restore /smoke/consumer/Consumer.csproj --source /smoke/feed --no-http-cache
                    dotnet build /smoke/consumer/Consumer.csproj --no-restore --configuration Release --output /smoke/app
                    exec dotnet /smoke/app/Consumer.dll
                    """);
        }
        else
        {
            builder = builder.WithResourceMapping(DirectoryPath.Of(artifacts.ExampleDirectory), DirectoryPath.Of("/smoke/example"))
                .WithEntrypoint("dotnet")
                .WithCommand("/smoke/example/Envoy.ControlPlane.Example.dll");
        }
        return builder.Build();
    }

    private async Task VerifyFreshPackagesAsync(IContainer management, CancellationToken cancellationToken)
    {
        var assets = await management.ReadFileAsync(ConsumerSource + "/obj/project.assets.json", cancellationToken);
        using var document = JsonDocument.Parse(assets);
        var root = document.RootElement;
        Assert.Equal(ConsumerNuGet, Assert.Single(root.GetProperty("packageFolders").EnumerateObject()).Name.TrimEnd('/'));
        var framework = Assert.Single(root.GetProperty("project").GetProperty("frameworks").EnumerateObject()).Value;
        var direct = Assert.Single(framework.GetProperty("dependencies").EnumerateObject());
        Assert.Equal(SmokeBuildArtifacts.HostingPackage, direct.Name);
        var libraries = root.GetProperty("libraries");
        Assert.All(libraries.EnumerateObject(), library => Assert.Equal("package", library.Value.GetProperty("type").GetString()));
        foreach (var (id, archive) in artifacts.FreshPackages)
        {
            string version = artifacts.PackageVersion;
            Assert.Equal("package", libraries.GetProperty(id + "/" + version).GetProperty("type").GetString());
            string name = id.ToLowerInvariant();
            string path = $"{ConsumerNuGet}/{name}/{version}/{name}.{version}.nupkg.sha512";
            var restoredHash = await management.ReadFileAsync(path, cancellationToken);
            await using var input = File.OpenRead(archive);
            string expected = Convert.ToBase64String(await SHA512.HashDataAsync(input, cancellationToken));
            Assert.Equal(expected, Encoding.UTF8.GetString(restoredHash).Trim());
        }
    }

    private static async Task WriteSettingsAsync(DirectoryInfo settings, IPAddress backend, CancellationToken cancellationToken)
    {
        string target = Path.Combine(settings.FullName, "appsettings.json");
        string pending = target + ".next";
        await File.WriteAllTextAsync(pending, JsonSerializer.Serialize(new
        {
            NodeId = "node",
            BackendAddress = backend.ToString(),
            BackendPort = EnvoyBackendContainer.Port,
            ListenerPort,
        }), cancellationToken);
        File.Move(pending, target, overwrite: true);
    }

    private static Task ExpectTrafficAsync(HttpClient client, Uri address, string expected, CancellationToken cancellationToken) =>
        AsyncPoll.UntilAsync(async token =>
        {
            try
            {
                using var response = await client.GetAsync(address, token);
                return response.StatusCode == HttpStatusCode.OK && await response.Content.ReadAsStringAsync(token) == expected;
            }
            catch (HttpRequestException)
            {
                return false;
            }
        }, $"the accepted proxy route returning '{expected}'", PollTimeout, cancellationToken);

    private static async Task VerifyManagementAddressAsync(EnvoyProxyContainer proxy, string alias, CancellationToken cancellationToken)
    {
        var dump = await proxy.GetConfigDumpAsync(cancellationToken);
        var entry = Assert.Single(AdminEntries(dump, "ClustersConfigDump", "static_clusters"), item =>
            item.GetProperty("cluster").GetProperty("name").GetString() == "xds");
        var socket = entry.GetProperty("cluster").GetProperty("load_assignment").GetProperty("endpoints")[0]
            .GetProperty("lb_endpoints")[0].GetProperty("endpoint").GetProperty("address").GetProperty("socket_address");
        Assert.Equal(alias, socket.GetProperty("address").GetString());
        Assert.Equal(ManagementPort, socket.GetProperty("port_value").GetInt32());
    }

    private static async Task VerifyEndpointsAsync(EnvoyProxyContainer proxy, IPAddress expected, CancellationToken cancellationToken)
    {
        var dump = await proxy.GetConfigDumpAsync(cancellationToken, includeEndpoints: true);
        var entry = Assert.Single(AdminEntries(dump, "EndpointsConfigDump", "dynamic_endpoint_configs"), item =>
            item.GetProperty("endpoint_config").GetProperty("cluster_name").GetString() == "service");
        var socket = entry.GetProperty("endpoint_config").GetProperty("endpoints")[0].GetProperty("lb_endpoints")[0]
            .GetProperty("endpoint").GetProperty("address").GetProperty("socket_address");
        Assert.Equal(expected.ToString(), socket.GetProperty("address").GetString());
        Assert.Equal(EnvoyBackendContainer.Port, socket.GetProperty("port_value").GetInt32());
    }
}
