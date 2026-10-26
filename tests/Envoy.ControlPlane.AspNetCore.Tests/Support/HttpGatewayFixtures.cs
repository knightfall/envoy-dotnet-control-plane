using System.Net;
using System.Text;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Envoy.ControlPlane.AspNetCore.Tests.Support;

internal static class HttpGatewayFixtures
{
    internal static HttpClient Client(GrpcHost host) => new()
    {
        BaseAddress = new Uri(host.Application.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()),
        DefaultRequestVersion = HttpVersion.Version20,
        DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact,
        Timeout = GrpcHost.Timeout,
    };

    internal static async Task<DefaultHttpContext> InvokeAsync(GrpcHost host, string path, string body, bool readFailure = false)
    {
        var endpoint = ((IEndpointRouteBuilder)host.Application).DataSources.SelectMany(source => source.Endpoints)
            .Single(item => item.DisplayName == "Envoy xDS HTTP gateway");
        using Stream input = readFailure ? new UnreadableBody() : new MemoryStream(Encoding.UTF8.GetBytes(body));
        var context = new DefaultHttpContext { RequestServices = host.Application.Services, RequestAborted = Xunit.TestContext.Current.CancellationToken };
        context.Request.Path = path;
        context.Request.Method = "POST";
        context.Request.Body = input;
        context.Response.Body = new MemoryStream();
        await GrpcHost.Wait(endpoint.RequestDelegate!(context));
        return context;
    }
}

internal sealed class UnreadableBody : Stream
{
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) => throw new IOException("Fixture read failure");
    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
