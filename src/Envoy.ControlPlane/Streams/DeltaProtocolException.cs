namespace Envoy.ControlPlane.Streams;

/// <summary>Transport-independent Delta protocol failures.</summary>
public enum DeltaProtocolError
{
    /// <summary>An ADS request omitted its required type URL.</summary>
    InvalidArgument = 0,

    /// <summary>The request or response channel ended without a usable value.</summary>
    Unavailable = 1,
}

/// <summary>A Delta protocol failure; application and producer exceptions retain their original types.</summary>
public sealed class DeltaProtocolException : Exception
{
    /// <summary>Creates an unavailable-stream failure.</summary>
    public DeltaProtocolException() : this(DeltaProtocolError.Unavailable, "Resource stream unavailable") { }

    /// <summary>Creates an unavailable-stream failure with a message.</summary>
    /// <param name="message">The failure description.</param>
    public DeltaProtocolException(string? message) : this(DeltaProtocolError.Unavailable, message) { }

    /// <summary>Creates an unavailable-stream failure retaining its cause.</summary>
    /// <param name="message">The failure description.</param>
    /// <param name="innerException">The original failure.</param>
    public DeltaProtocolException(string? message, Exception? innerException)
        : this(DeltaProtocolError.Unavailable, message, innerException) { }

    /// <summary>Creates a failure with the classification expected by transport adapters.</summary>
    /// <param name="error">The protocol classification.</param>
    /// <param name="message">The failure description.</param>
    /// <param name="innerException">An optional original failure.</param>
    public DeltaProtocolException(DeltaProtocolError error, string? message, Exception? innerException = null)
        : base(message, innerException) => Error = error;

    /// <summary>Gets the protocol classification.</summary>
    public DeltaProtocolError Error { get; }
}
