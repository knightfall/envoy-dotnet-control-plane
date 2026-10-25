using Envoy.ControlPlane.TransportProbe;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using System.Diagnostics;
using System.Net.Security;
using System.Runtime.InteropServices;
using System.Runtime;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography;

var builder = WebApplication.CreateSlimBuilder();
builder.Logging.ClearProviders();
string certificateDirectory = args.Length > 2 ? args[2] : "";
builder.WebHost.ConfigureKestrel(options =>
{
    options.Listen(System.Net.IPAddress.Loopback, int.Parse(args[0]), endpoint =>
    {
        endpoint.Protocols = HttpProtocols.Http2;
        if (certificateDirectory.Length > 0)
        {
            using var pem = X509Certificate2.CreateFromPemFile(Path.Combine(certificateDirectory, "server.pem"), Path.Combine(certificateDirectory, "server-key.pem"));
            var certificate = X509CertificateLoader.LoadPkcs12(pem.Export(X509ContentType.Pkcs12), null);
            var authority = X509CertificateLoader.LoadCertificateFromFile(Path.Combine(certificateDirectory, "ca.pem"));
            endpoint.UseHttps(new HttpsConnectionAdapterOptions
            {
                ServerCertificate = certificate,
                SslProtocols = Environment.GetEnvironmentVariable("ENVOY_PROBE_TLS_PROTOCOL") == "tls13" ? SslProtocols.Tls13 : SslProtocols.Tls12,
                OnAuthenticate = (_, authentication) =>
                {
                    if (OperatingSystem.IsLinux() && authentication.EnabledSslProtocols == SslProtocols.Tls13)
                    {
                        authentication.CipherSuitesPolicy = new CipherSuitesPolicy([TlsCipherSuite.TLS_AES_128_GCM_SHA256]);
                    }
                },
                ClientCertificateMode = ClientCertificateMode.RequireCertificate,
                ClientCertificateValidation = (client, _, _) =>
                {
                    using var chain = new X509Chain();
                    chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                    chain.ChainPolicy.CustomTrustStore.Add(authority);
                    chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                    chain.ChainPolicy.ApplicationPolicy.Add(new Oid("1.3.6.1.5.5.7.3.2"));
                    return chain.Build(client);
                },
            });
        }
    });
    options.Listen(System.Net.IPAddress.Loopback, int.Parse(args[1]), endpoint => endpoint.Protocols = HttpProtocols.Http1);
});
builder.Services.AddGrpc();
builder.Services.AddSingleton<ProbeState>();
var app = builder.Build();
app.MapGrpcService<AdsProbe>();
app.MapGrpcService<EndpointProbe>();
app.MapGet("/stats", (ProbeState state) =>
{
    using var process = Process.GetCurrentProcess();
    return new
    {
        allocatedBytes = GC.GetTotalAllocatedBytes(precise: true),
        cpuSeconds = process.TotalProcessorTime.TotalSeconds,
        gcCollections = GC.CollectionCount(0) + GC.CollectionCount(1) + GC.CollectionCount(2),
        active = Volatile.Read(ref state.Active),
        burstStarted = Volatile.Read(ref state.BurstStarted),
        attemptedWrites = Volatile.Read(ref state.AttemptedWrites),
        completedWrites = Volatile.Read(ref state.CompletedWrites),
        runtime = RuntimeInformation.FrameworkDescription,
        serverGc = GCSettings.IsServerGC,
        processors = Environment.ProcessorCount,
        pid = Environment.ProcessId,
    };
});
await app.RunAsync();
