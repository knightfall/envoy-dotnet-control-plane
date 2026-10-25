namespace Envoy.ControlPlane.Cache;

/// <summary>A stable client subscription view retained for one watch lifetime.</summary>
public interface ISubscription
{
    /// <summary>Gets resources known by the client and their last returned versions.</summary>
    IReadOnlyDictionary<string, string> ReturnedResources { get; }

    /// <summary>Gets exact subscribed names.</summary>
    IReadOnlySet<string> SubscribedResources { get; }

    /// <summary>Gets subscribed prefixes without the trailing glob, retaining the separator.</summary>
    IReadOnlySet<string> SubscribedPrefixes { get; }

    /// <summary>Gets whether this subscription uses wildcard selection.</summary>
    bool IsWildcard { get; }
}
