// Copyright 2018 Envoyproxy Authors. SPDX-License-Identifier: Apache-2.0
namespace Envoy.ControlPlane.Resources;

/// <summary>The eight standard v3 HTTP discovery paths.</summary>
public static class ResourceFetchPaths
{
    /// <summary>Endpoint assignment polling.</summary>
    public const string Endpoints = "/v3/discovery:endpoints";
    /// <summary>Cluster polling.</summary>
    public const string Clusters = "/v3/discovery:clusters";
    /// <summary>Listener polling.</summary>
    public const string Listeners = "/v3/discovery:listeners";
    /// <summary>Route polling.</summary>
    public const string Routes = "/v3/discovery:routes";
    /// <summary>Scoped-route polling.</summary>
    public const string ScopedRoutes = "/v3/discovery:scoped-routes";
    /// <summary>Secret polling.</summary>
    public const string Secrets = "/v3/discovery:secrets";
    /// <summary>Runtime polling.</summary>
    public const string Runtime = "/v3/discovery:runtime";
    /// <summary>Extension configuration polling.</summary>
    public const string ExtensionConfigs = "/v3/discovery:extension_configs";
}
