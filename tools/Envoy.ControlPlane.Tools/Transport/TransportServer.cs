using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json.Nodes;

namespace Envoy.ControlPlane.Tools;

internal sealed class TransportServer : IAsyncDisposable
{
    private readonly Process process;
    private readonly Task<string> stdout;
    private readonly Task<string> stderr;
    private readonly string logPath;
    private readonly HttpClient client = new() { Timeout = TimeSpan.FromSeconds(2) };

    private TransportServer(Process process, string logPath, string rpcAddress, string adminAddress)
    {
        this.process = process;
        this.logPath = logPath;
        RpcAddress = rpcAddress;
        AdminAddress = adminAddress;
        stdout = process.StandardOutput.ReadToEndAsync();
        stderr = process.StandardError.ReadToEndAsync();
    }

    internal string RpcAddress { get; }
    internal string AdminAddress { get; }

    internal static async Task<TransportServer> StartAsync(string executable, string[] leadingArguments,
        string certificates, string logPath, IReadOnlyDictionary<string, string> environment)
    {
        int rpcPort = ReservePort();
        int adminPort = ReservePort();
        var start = new ProcessStartInfo(executable) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in leadingArguments.Concat([rpcPort.ToString(), adminPort.ToString(), certificates]))
        {
            start.ArgumentList.Add(argument);
        }
        foreach (var entry in environment)
        {
            start.Environment[entry.Key] = entry.Value;
        }
        var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start transport probe");
        var server = new TransportServer(process, logPath, "127.0.0.1:" + rpcPort, "http://127.0.0.1:" + adminPort);
        try
        {
            await server.WaitForReadyAsync();
            return server;
        }
        catch
        {
            await server.DisposeAsync();
            throw;
        }
    }

    internal async Task<JsonNode> ReadStatsAsync()
    {
        return await client.GetFromJsonAsync<JsonNode>(AdminAddress + "/stats") ?? throw new InvalidOperationException("Missing transport statistics");
    }

    private async Task WaitForReadyAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (true)
        {
            if (process.HasExited)
            {
                throw new InvalidOperationException("Transport probe exited before readiness: " + process.ExitCode);
            }
            try
            {
                _ = await ReadStatsAsync();
                return;
            }
            catch (Exception error) when (error is HttpRequestException or TaskCanceledException)
            {
                await Task.Delay(50, deadline.Token);
            }
        }
    }

    private static int ReservePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    public async ValueTask DisposeAsync()
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
        }
        await process.WaitForExitAsync();
        await File.WriteAllTextAsync(logPath, await stdout + await stderr);
        process.Dispose();
        client.Dispose();
    }
}
