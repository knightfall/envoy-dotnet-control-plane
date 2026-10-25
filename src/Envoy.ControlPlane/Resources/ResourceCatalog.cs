using Envoy.Config.Endpoint.V3;
using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace Envoy.ControlPlane.Resources;

/// <summary>Maps upstream resource families and names without evaluating validation rules.</summary>
public static class ResourceCatalog
{
    /// <summary>Returns the known response family, or <see cref="ResponseType.Unknown"/>.</summary>
    /// <param name="typeUrl">The resource type URL.</param>
    public static ResponseType GetResponseType(string typeUrl) => typeUrl switch
    {
        ResourceTypeUrls.Cluster => ResponseType.Cluster,
        ResourceTypeUrls.Endpoint => ResponseType.Endpoint,
        ResourceTypeUrls.LbEndpoint => ResponseType.LbEndpoint,
        ResourceTypeUrls.Listener => ResponseType.Listener,
        ResourceTypeUrls.Route => ResponseType.Route,
        ResourceTypeUrls.ScopedRoute => ResponseType.ScopedRoute,
        ResourceTypeUrls.VirtualHost => ResponseType.VirtualHost,
        ResourceTypeUrls.Secret => ResponseType.Secret,
        ResourceTypeUrls.Runtime => ResponseType.Runtime,
        ResourceTypeUrls.ExtensionConfig => ResponseType.ExtensionConfig,
        ResourceTypeUrls.RateLimitConfig => ResponseType.RateLimitConfig,
        ResourceTypeUrls.FilterChain => ResponseType.FilterChain,
        _ => ResponseType.Unknown,
    };

    /// <summary>Returns the type URL for a known response family.</summary>
    /// <param name="responseType">A known response family.</param>
    /// <exception cref="ArgumentOutOfRangeException">The family is unknown.</exception>
    public static string GetTypeUrl(ResponseType responseType) => responseType switch
    {
        ResponseType.Cluster => ResourceTypeUrls.Cluster,
        ResponseType.Endpoint => ResourceTypeUrls.Endpoint,
        ResponseType.LbEndpoint => ResourceTypeUrls.LbEndpoint,
        ResponseType.Listener => ResourceTypeUrls.Listener,
        ResponseType.Route => ResourceTypeUrls.Route,
        ResponseType.ScopedRoute => ResourceTypeUrls.ScopedRoute,
        ResponseType.VirtualHost => ResourceTypeUrls.VirtualHost,
        ResponseType.Secret => ResourceTypeUrls.Secret,
        ResponseType.Runtime => ResourceTypeUrls.Runtime,
        ResponseType.ExtensionConfig => ResourceTypeUrls.ExtensionConfig,
        ResponseType.RateLimitConfig => ResourceTypeUrls.RateLimitConfig,
        ResponseType.FilterChain => ResourceTypeUrls.FilterChain,
        _ => throw new ArgumentOutOfRangeException(nameof(responseType), responseType, "Unknown response type"),
    };

    /// <summary>Returns the endpoint cluster name, generated string name, custom name, or an empty string.</summary>
    /// <param name="resource">The retained resource message.</param>
    public static string GetName(IMessage? resource)
    {
        if (resource is ClusterLoadAssignment endpoints)
        {
            return endpoints.ClusterName;
        }
        if (resource is INamedResource named)
        {
            return named.Name;
        }
        // Go recognizes generated GetName methods structurally. The protobuf
        // accessor provides the corresponding .NET field without CLR reflection.
        var name = resource?.Descriptor.FindFieldByName("name");
        return name is { FieldType: FieldType.String, IsRepeated: false }
            ? (string)name.Accessor.GetValue(resource) : "";
    }

    /// <summary>Returns resource names in input order, including empty and duplicate names.</summary>
    /// <param name="resources">The entries to inspect.</param>
    public static string[] GetNames(IEnumerable<ResourceEntry> resources) =>
        resources.Select(entry => GetName(entry.Resource)).ToArray();

    /// <summary>Indexes raw resources by name; the last duplicate wins.</summary>
    /// <param name="resources">The retained resource messages.</param>
    public static IReadOnlyDictionary<string, IMessage?> IndexResourcesByName(IEnumerable<IMessage?> resources)
    {
        var indexed = new Dictionary<string, IMessage?>(StringComparer.Ordinal);
        foreach (var resource in resources)
        {
            indexed[GetName(resource)] = resource;
        }
        return indexed;
    }

    /// <summary>Identifies families whose SotW responses must include all requested state.</summary>
    /// <param name="typeUrl">The response type URL.</param>
    public static bool RequiresFullStateInSotw(string typeUrl) =>
        typeUrl is ResourceTypeUrls.Cluster or ResourceTypeUrls.Listener;
}
