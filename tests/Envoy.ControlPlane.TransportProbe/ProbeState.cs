using Envoy.Service.Discovery.V3;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace Envoy.ControlPlane.TransportProbe;

sealed class ProbeState
{
    private readonly Dictionary<int, DiscoveryResponse> responses = new[] { 1024, 65536, 1048576, 5 * 1048576 }
        .ToDictionary(size => size, size => CreateResponse(size));
    public int Active;
    public int BurstStarted;
    public int AttemptedWrites;
    public int CompletedWrites;

    public DiscoveryResponse Response(ServerCallContext context) => responses[int.Parse(context.RequestHeaders.GetValue("size") ?? "1024")];

    private static DiscoveryResponse CreateResponse(int size)
    {
        var response = new DiscoveryResponse { VersionInfo = "transport-probe", Nonce = "1", TypeUrl = "type.googleapis.com/probe.Payload" };
        response.Resources.Add(new Any { TypeUrl = response.TypeUrl, Value = ByteString.CopyFrom(new byte[size]) });
        return response;
    }
}
