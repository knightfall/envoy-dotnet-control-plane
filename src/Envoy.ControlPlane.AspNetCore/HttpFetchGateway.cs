// Copyright 2018 Envoyproxy Authors. SPDX-License-Identifier: Apache-2.0
using System.Text;
using Envoy.ControlPlane.Cache;
using Envoy.ControlPlane.Fetch;
using Envoy.ControlPlane.Resources;
using Envoy.Service.Discovery.V3;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Envoy.ControlPlane.AspNetCore;

internal sealed class HttpFetchGateway(TypeRegistry types)
{
    internal static readonly TypeRegistry DefaultTypes = TypeRegistry.FromMessages(
        Envoy.Config.Endpoint.V3.ClusterLoadAssignment.Descriptor, Envoy.Config.Cluster.V3.Cluster.Descriptor,
        Envoy.Config.Listener.V3.Listener.Descriptor, Envoy.Config.Route.V3.RouteConfiguration.Descriptor,
        Envoy.Config.Route.V3.ScopedRouteConfiguration.Descriptor, Envoy.Extensions.TransportSockets.Tls.V3.Secret.Descriptor,
        Envoy.Service.Runtime.V3.Runtime.Descriptor, Envoy.Config.Core.V3.TypedExtensionConfig.Descriptor, Resource.Descriptor);

    private static readonly UTF8Encoding Utf8 = new(false, true);
    private readonly JsonParser parser = new(JsonParser.Settings.Default.WithTypeRegistry(types));
    private readonly JsonFormatter formatter = new(JsonFormatter.Settings.Default.WithTypeRegistry(types).WithPreserveProtoFieldNames(true));

    internal async Task HandleAsync(HttpContext context)
    {
        string typeUrl = GetTypeUrl(CleanPath(context.Request.Path.Value ?? ""));
        if (typeUrl.Length == 0)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        DiscoveryRequest request;
        try
        {
            await using var body = new MemoryStream();
            await context.Request.Body.CopyToAsync(body, context.RequestAborted);
            request = parser.Parse<DiscoveryRequest>(Utf8.GetString(body.GetBuffer(), 0, checked((int)body.Length)));
        }
        catch (Exception error) when (error is not OutOfMemoryException && !context.RequestAborted.IsCancellationRequested)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }
        request.TypeUrl = typeUrl;
        string json;
        try
        {
            var server = context.RequestServices.GetRequiredService<FetchServer>();
            var response = await server.FetchAsync(request, new ResponseContext(context.RequestAborted, context));
            json = formatter.Format(response);
        }
        catch (Exception error) when (error is not OutOfMemoryException && !context.RequestAborted.IsCancellationRequested)
        {
            if (IsSkipped(error))
            {
                context.Response.StatusCode = StatusCodes.Status304NotModified;
                return;
            }
            context.RequestServices.GetRequiredService<ILogger<HttpFetchGateway>>().LogDebug(error, "Discovery Fetch failed");
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            return;
        }
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(json, context.RequestAborted);
    }

    private static bool IsSkipped(Exception error)
    {
        for (Exception? current = error; current is not null; current = current.InnerException)
        {
            if (current is SkipFetchException)
            {
                return true;
            }
        }
        return false;
    }

    private static string GetTypeUrl(string path) => path switch
    {
        "/v3/discovery:endpoints" => ResourceTypeUrls.Endpoint,
        "/v3/discovery:clusters" => ResourceTypeUrls.Cluster,
        "/v3/discovery:listeners" => ResourceTypeUrls.Listener,
        "/v3/discovery:routes" => ResourceTypeUrls.Route,
        "/v3/discovery:scoped-routes" => ResourceTypeUrls.ScopedRoute,
        "/v3/discovery:secrets" => ResourceTypeUrls.Secret,
        "/v3/discovery:runtime" => ResourceTypeUrls.Runtime,
        "/v3/discovery:extension_configs" => ResourceTypeUrls.ExtensionConfig,
        _ => "",
    };

    private static string CleanPath(string path)
    {
        var segments = new List<string>();
        foreach (string segment in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == "..")
            {
                if (segments.Count > 0)
                {
                    segments.RemoveAt(segments.Count - 1);
                }
            }
            else if (segment != ".")
            {
                segments.Add(segment);
            }
        }
        return "/" + string.Join('/', segments);
    }
}
