using System.Threading.Channels;
using Envoy.ControlPlane.Cache;
using Envoy.ControlPlane.Tests.Support;
using Xunit;
using static Envoy.ControlPlane.Tests.Support.StreamFixtures;

namespace Envoy.ControlPlane.Tests;

public sealed class SotwStreamFixtureTests
{
    [Fact]
    public async Task ResponseCompletion_SurvivesImmediateReaderDisposal()
    {
        var responses = Channel.CreateUnbounded<ISotwResponse>(new UnboundedChannelOptions
        {
            AllowSynchronousContinuations = true,
        });
        var watch = new StreamWatch(responses.Writer);
        var response = Response(Request());
        Task reading = DisposeAfterReadingAsync(responses.Reader, watch, response);

        watch.Respond(response);
        await Wait(reading);

        Assert.Same(response, await Wait(watch.Completion));
        Assert.Equal(1, watch.Disposals);
    }

    private static async Task DisposeAfterReadingAsync(ChannelReader<ISotwResponse> responses, StreamWatch watch,
        ISotwResponse expected)
    {
        Assert.Same(expected, await responses.ReadAsync().ConfigureAwait(false));
        watch.Dispose();
    }
}
