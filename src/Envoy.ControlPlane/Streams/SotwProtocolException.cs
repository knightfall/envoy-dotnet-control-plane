namespace Envoy.ControlPlane.Streams;

/// <summary>Transport-independent protocol failures translated by discovery service adapters.</summary>
public enum SotwProtocolError
{
    /// <summary>An ADS request omitted its required type URL.</summary>
    InvalidArgument = 0,

    /// <summary>The request or current response watch ended without a usable value.</summary>
    Unavailable = 1,
}

/// <summary>A SotW protocol failure; application and producer exceptions retain their original types.</summary>
public sealed class SotwProtocolException : Exception
{
    /// <summary>Creates an unavailable-stream failure.</summary>
    public SotwProtocolException() : this(SotwProtocolError.Unavailable, "Resource stream unavailable") { }

    /// <summary>Creates an unavailable-stream failure with a message.</summary>
    /// <param name="message">The failure description.</param>
    public SotwProtocolException(string? message) : this(SotwProtocolError.Unavailable, message) { }

    /// <summary>Creates an unavailable-stream failure retaining its cause.</summary>
    /// <param name="message">The failure description.</param>
    /// <param name="innerException">The original failure.</param>
    public SotwProtocolException(string? message, Exception? innerException)
        : this(SotwProtocolError.Unavailable, message, innerException) { }

    /// <summary>Creates a failure with the classification expected by transport adapters.</summary>
    /// <param name="error">The protocol classification.</param>
    /// <param name="message">The failure description.</param>
    /// <param name="innerException">An optional original failure.</param>
    public SotwProtocolException(SotwProtocolError error, string? message, Exception? innerException = null) : base(message, innerException)
    {
        Error = error;
    }

    /// <summary>Gets the protocol classification.</summary>
    public SotwProtocolError Error { get; }
}
