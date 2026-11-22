using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Envoy.ControlPlane.Integration.Tests.Harness;

namespace Envoy.ControlPlane.Integration.Tests.Security;

internal sealed class TestCertificateAuthority : IDisposable
{
    private const string ServerAuthentication = "1.3.6.1.5.5.7.3.1";
    private const string ClientAuthentication = "1.3.6.1.5.5.7.3.2";
    internal X509Certificate2 Certificate { get; } = CreateRoot();
    private static DateTimeOffset NotBefore => DateTimeOffset.UtcNow.AddMinutes(-5);

    internal X509Certificate2 IssueServer(string dnsName, bool addLoopback = false) =>
        Issue(dnsName, ServerAuthentication, dnsName, addLoopback);

    internal X509Certificate2 IssueClient(string commonName) =>
        Issue(commonName, ClientAuthentication, dnsName: null, addLoopback: false);

    internal bool TrustsClient(X509Certificate2 certificate)
    {
        using var chain = new X509Chain();
        chain.ChainPolicy = TrustPolicy(ClientAuthentication);
        return chain.Build(certificate);
    }

    internal X509ChainPolicy ServerTrustPolicy() => TrustPolicy(ServerAuthentication);

    internal IReadOnlyDictionary<string, byte[]> ManagementFiles(X509Certificate2? client = null)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            [EnvoyProxyContainer.ManagementCaFile] = Encoding.UTF8.GetBytes(Certificate.ExportCertificatePem()),
        };
        if (client is not null)
        {
            files.Add(EnvoyProxyContainer.ManagementClientCertificateFile, Encoding.UTF8.GetBytes(client.ExportCertificatePem()));
            files.Add(EnvoyProxyContainer.ManagementClientKeyFile, Encoding.UTF8.GetBytes(PrivateKeyPem(client)));
        }
        return files;
    }

    internal static string PrivateKeyPem(X509Certificate2 certificate)
    {
        using var key = certificate.GetRSAPrivateKey()
            ?? throw new InvalidOperationException("The test certificate lacks its generated RSA key.");
        return key.ExportPkcs8PrivateKeyPem();
    }

    public void Dispose() => Certificate.Dispose();

    private static X509Certificate2 CreateRoot()
    {
        using var key = RSA.Create(2048);
        var request = CreateRequest("Envoy integration CA " + Guid.NewGuid().ToString("N"), key);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        return request.CreateSelfSigned(NotBefore, DateTimeOffset.UtcNow.AddHours(2));
    }

    private X509Certificate2 Issue(string commonName, string application, string? dnsName, bool addLoopback)
    {
        using var key = RSA.Create(2048);
        var request = CreateRequest(commonName, key);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new Oid(application) }, true));
        if (dnsName is not null)
        {
            var names = new SubjectAlternativeNameBuilder();
            names.AddDnsName(dnsName);
            if (addLoopback)
            {
                names.AddIpAddress(IPAddress.Loopback);
                names.AddIpAddress(IPAddress.IPv6Loopback);
            }
            request.CertificateExtensions.Add(names.Build());
        }

        using var leaf = request.Create(Certificate, NotBefore, DateTimeOffset.UtcNow.AddHours(1), RandomNumberGenerator.GetBytes(16));
        return leaf.CopyWithPrivateKey(key);
    }

    private static CertificateRequest CreateRequest(string commonName, RSA key)
    {
        var request = new CertificateRequest("CN=" + commonName, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        return request;
    }

    private X509ChainPolicy TrustPolicy(string application) => new()
    {
        TrustMode = X509ChainTrustMode.CustomRootTrust,
        RevocationMode = X509RevocationMode.NoCheck,
        CustomTrustStore = { Certificate },
        ApplicationPolicy = { new Oid(application) },
    };
}
