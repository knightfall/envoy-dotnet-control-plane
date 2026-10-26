// Copyright 2020 Envoyproxy Authors. SPDX-License-Identifier: Apache-2.0
using Envoy.Service.Discovery.V3;
using Google.Protobuf.WellKnownTypes;

namespace Envoy.ControlPlane.Cache;

internal sealed class LinearSotwResponse : ISotwResponse
{
    private readonly Lazy<DiscoveryResponse> materialized;

    internal LinearSotwResponse(DiscoveryRequest request, string version, IReadOnlyList<LinearResource> resources,
        IReadOnlyDictionary<string, string> returned)
    {
        Request = request;
        ResponseVersion = version;
        ReturnedResources = returned;
        materialized = new Lazy<DiscoveryResponse>(() => Build(resources));
    }

    public DiscoveryRequest Request { get; }
    public string ResponseVersion { get; }
    public IReadOnlyDictionary<string, string> ReturnedResources { get; }
    public ResponseContext Context => default;
    public DiscoveryResponse GetDiscoveryResponse() => materialized.Value;

    private DiscoveryResponse Build(IReadOnlyList<LinearResource> resources)
    {
        var response = new DiscoveryResponse { TypeUrl = Request.TypeUrl, VersionInfo = ResponseVersion };
        foreach (var resource in resources)
        {
            response.Resources.Add(new Any { TypeUrl = Request.TypeUrl, Value = resource.Payload });
        }
        return response;
    }
}
