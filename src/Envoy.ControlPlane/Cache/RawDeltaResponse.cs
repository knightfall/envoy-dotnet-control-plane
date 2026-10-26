// Copyright 2018 Envoyproxy Authors. SPDX-License-Identifier: Apache-2.0
// Adapted for lazy .NET serialization from the pinned RawDeltaResponse behavior.
using Envoy.ControlPlane.Resources;
using Envoy.Service.Discovery.V3;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using DiscoveryResource = Envoy.Service.Discovery.V3.Resource;

namespace Envoy.ControlPlane.Cache;

internal sealed class RawDeltaResponse : IDeltaResponse
{
    private readonly Lazy<DeltaDiscoveryResponse> materialized;

    internal RawDeltaResponse(DeltaDiscoveryRequest request, string version,
        IReadOnlyDictionary<string, ResourceEntry> resources, IReadOnlyList<string> removed,
        IReadOnlyDictionary<string, string> returned, ResponseContext context)
    {
        Request = request;
        ResponseVersion = version;
        ReturnedResources = returned;
        Context = context;
        materialized = new Lazy<DeltaDiscoveryResponse>(() => Build(resources, removed));
    }

    public DeltaDiscoveryRequest Request { get; }
    public string ResponseVersion { get; }
    public IReadOnlyDictionary<string, string> ReturnedResources { get; }
    public ResponseContext Context { get; }
    public DeltaDiscoveryResponse GetDeltaDiscoveryResponse() => materialized.Value;

    private DeltaDiscoveryResponse Build(IReadOnlyDictionary<string, ResourceEntry> resources, IReadOnlyList<string> removed)
    {
        var response = new DeltaDiscoveryResponse { TypeUrl = Request.TypeUrl, SystemVersionInfo = ResponseVersion };
        foreach (var (name, entry) in resources)
        {
            byte[] payload = ResourceSerialization.Serialize(entry.Resource);
            response.Resources.Add(new DiscoveryResource
            {
                Name = name,
                Resource_ = new Any { TypeUrl = Request.TypeUrl, Value = ByteString.CopyFrom(payload) },
                Version = ResourceSerialization.ComputeHash(payload),
            });
        }
        response.RemovedResources.Add(removed);
        return response;
    }
}
