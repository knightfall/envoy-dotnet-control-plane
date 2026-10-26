// Copyright 2020 Envoyproxy Authors. SPDX-License-Identifier: Apache-2.0
using Envoy.Service.Discovery.V3;
using Google.Protobuf.WellKnownTypes;
using DiscoveryResource = Envoy.Service.Discovery.V3.Resource;

namespace Envoy.ControlPlane.Cache;

internal sealed class LinearDeltaResponse : IDeltaResponse
{
    private readonly Lazy<DeltaDiscoveryResponse> materialized;

    internal LinearDeltaResponse(DeltaDiscoveryRequest request, string version, IReadOnlyList<LinearResource> resources,
        IReadOnlyList<string> removed, IReadOnlyDictionary<string, string> returned)
    {
        Request = request;
        ResponseVersion = version;
        ReturnedResources = returned;
        materialized = new Lazy<DeltaDiscoveryResponse>(() => Build(resources, removed));
    }

    public DeltaDiscoveryRequest Request { get; }
    public string ResponseVersion { get; }
    public IReadOnlyDictionary<string, string> ReturnedResources { get; }
    public ResponseContext Context => default;
    public DeltaDiscoveryResponse GetDeltaDiscoveryResponse() => materialized.Value;

    private DeltaDiscoveryResponse Build(IReadOnlyList<LinearResource> resources, IReadOnlyList<string> removed)
    {
        var response = new DeltaDiscoveryResponse { TypeUrl = Request.TypeUrl, SystemVersionInfo = ResponseVersion };
        foreach (var resource in resources)
        {
            response.Resources.Add(new DiscoveryResource
            {
                Name = resource.Name,
                Version = resource.GetVersion(delta: true),
                Resource_ = new Any { TypeUrl = Request.TypeUrl, Value = resource.Payload },
            });
        }
        response.RemovedResources.Add(removed);
        return response;
    }
}
