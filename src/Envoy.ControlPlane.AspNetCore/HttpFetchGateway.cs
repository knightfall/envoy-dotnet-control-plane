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
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Envoy.ControlPlane.AspNetCore;

internal sealed class HttpFetchGateway(TypeRegistry types, int maxRequestBodyBytes)
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
        var stopping = context.RequestServices.GetService<IHostApplicationLifetime>()?.ApplicationStopping ?? default;
        using var linked = stopping.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, stopping)
            : null;
        var cancellationToken = linked?.Token ?? context.RequestAborted;
        try
        {
            await FetchAsync(context, typeUrl, cancellationToken);
        }
        catch (Exception error) when (error is not OutOfMemoryException && cancellationToken.IsCancellationRequested)
        {
            if (error is not OperationCanceledException)
            {
                context.RequestServices.GetRequiredService<ILogger<HttpFetchGateway>>()
                    .LogDebug(error, "Discovery Fetch failed during cancellation");
            }
            if (!context.RequestAborted.IsCancellationRequested)
            {
                if (context.Response.HasStarted)
                {
                    context.Abort();
                }
                else
                {
                    context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                }
            }
        }
    }

    private async Task FetchAsync(HttpContext context, string typeUrl, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DiscoveryRequest request;
        try
        {
            request = await ReadRequestAsync(context.Request, cancellationToken);
        }
        catch (BadHttpRequestException error) when (!cancellationToken.IsCancellationRequested)
        {
            context.Response.StatusCode = error.StatusCode;
            return;
        }
        catch (Exception error) when (error is not OutOfMemoryException && !cancellationToken.IsCancellationRequested)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }
        request.TypeUrl = typeUrl;
        string json;
        try
        {
            var server = context.RequestServices.GetRequiredService<FetchServer>();
            var response = await server.FetchAsync(request, new ResponseContext(cancellationToken, context));
            json = formatter.Format(response);
        }
        catch (Exception error) when (error is not OutOfMemoryException && !cancellationToken.IsCancellationRequested)
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
        cancellationToken.ThrowIfCancellationRequested();
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(json, cancellationToken);
    }

    private async Task<DiscoveryRequest> ReadRequestAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (request.ContentLength > maxRequestBodyBytes)
        {
            throw BodyTooLarge();
        }
        await using var body = new MemoryStream();
        var buffer = new byte[Math.Min(maxRequestBodyBytes, 8192)];
        while (true)
        {
            // At the limit, read only the byte needed to distinguish EOF from excess data.
            int allowed = (int)Math.Min(buffer.Length, (long)maxRequestBodyBytes - body.Length + 1);
            int count = await request.Body.ReadAsync(buffer.AsMemory(0, allowed), cancellationToken);
            if (count == 0)
            {
                return parser.Parse<DiscoveryRequest>(Utf8.GetString(body.GetBuffer(), 0, (int)body.Length));
            }
            if (body.Length + count > maxRequestBodyBytes)
            {
                throw BodyTooLarge();
            }
            body.Write(buffer, 0, count);
        }
    }

    private static BadHttpRequestException BodyTooLarge() =>
        new("Discovery request body exceeds the gateway limit.", StatusCodes.Status413PayloadTooLarge);

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
        ResourceFetchPaths.Endpoints => ResourceTypeUrls.Endpoint,
        ResourceFetchPaths.Clusters => ResourceTypeUrls.Cluster,
        ResourceFetchPaths.Listeners => ResourceTypeUrls.Listener,
        ResourceFetchPaths.Routes => ResourceTypeUrls.Route,
        ResourceFetchPaths.ScopedRoutes => ResourceTypeUrls.ScopedRoute,
        ResourceFetchPaths.Secrets => ResourceTypeUrls.Secret,
        ResourceFetchPaths.Runtime => ResourceTypeUrls.Runtime,
        ResourceFetchPaths.ExtensionConfigs => ResourceTypeUrls.ExtensionConfig,
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
