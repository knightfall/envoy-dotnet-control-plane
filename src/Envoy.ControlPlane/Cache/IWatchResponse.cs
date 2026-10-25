namespace Envoy.ControlPlane.Cache;

/// <summary>Metadata shared by SotW and Delta cache responses.</summary>
public interface IWatchResponse
{
    /// <summary>Gets the response version; Delta uses this for system-version information.</summary>
    string ResponseVersion { get; }

    /// <summary>Gets the client's resulting resource versions, including retained previous resources.</summary>
    IReadOnlyDictionary<string, string> ReturnedResources { get; }

    /// <summary>Gets the response-creation context forwarded to callbacks.</summary>
    ResponseContext Context { get; }
}
