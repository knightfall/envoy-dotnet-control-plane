using Envoy.ControlPlane.Resources;
using Envoy.Service.Discovery.V3;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using DiscoveryResource = Envoy.Service.Discovery.V3.Resource;

namespace Envoy.ControlPlane.Cache;

internal sealed class RawSotwResponse : ISotwResponse
{
    private readonly Lazy<DiscoveryResponse> materialized;

    internal RawSotwResponse(DiscoveryRequest request, string version, IReadOnlyList<ResourceEntry> resources,
        IReadOnlyDictionary<string, string> returned, ResponseContext context)
    {
        Request = request;
        ResponseVersion = version;
        ReturnedResources = returned;
        Context = context;
        materialized = new Lazy<DiscoveryResponse>(() => Build(resources));
    }

    public DiscoveryRequest Request { get; }
    public string ResponseVersion { get; }
    public IReadOnlyDictionary<string, string> ReturnedResources { get; }
    public ResponseContext Context { get; }
    public DiscoveryResponse GetDiscoveryResponse() => materialized.Value;

    private DiscoveryResponse Build(IReadOnlyList<ResourceEntry> resources)
    {
        var response = new DiscoveryResponse { TypeUrl = Request.TypeUrl, VersionInfo = ResponseVersion };
        foreach (var entry in resources)
        {
            var payload = new Any { TypeUrl = Request.TypeUrl, Value = ByteString.CopyFrom(ResourceSerialization.Serialize(entry.Resource)) };
            if (entry.Ttl is { } ttl)
            {
                var wrapper = new DiscoveryResource
                {
                    Name = ResourceCatalog.GetName(entry.Resource),
                    Ttl = Duration.FromTimeSpan(ttl),
                    Resource_ = payload,
                };
                payload = new Any
                {
                    TypeUrl = ResourceTypeUrls.Prefix + DiscoveryResource.Descriptor.FullName,
                    Value = ByteString.CopyFrom(ResourceSerialization.Serialize(wrapper)),
                };
            }
            response.Resources.Add(payload);
        }
        return response;
    }
}
