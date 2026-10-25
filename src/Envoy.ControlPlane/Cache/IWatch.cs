namespace Envoy.ControlPlane.Cache;

/// <summary>A one-shot response; disposal cancels pending delivery and releases its registration.</summary>
/// <typeparam name="T">The cache response type.</typeparam>
public interface IWatch<T> : IDisposable
{
    /// <summary>Gets the immediate, pending, faulted or canceled result.</summary>
    Task<T> Completion { get; }
}
