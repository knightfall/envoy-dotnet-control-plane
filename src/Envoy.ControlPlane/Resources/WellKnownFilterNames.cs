// Copyright 2018 Envoyproxy Authors. Licensed under Apache-2.0.
namespace Envoy.ControlPlane.Resources;

/// <summary>Canonical labels used by the preview HTTP graph; typed-config URLs select the extensions.</summary>
public static class WellKnownFilterNames
{
    /// <summary>The HTTP connection manager network-filter label.</summary>
    public const string HttpConnectionManager = "envoy.filters.network.http_connection_manager";

    /// <summary>The router HTTP-filter label.</summary>
    public const string Router = "envoy.filters.http.router";
}
