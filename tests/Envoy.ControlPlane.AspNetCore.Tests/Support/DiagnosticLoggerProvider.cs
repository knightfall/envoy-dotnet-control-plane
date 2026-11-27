using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Envoy.ControlPlane.AspNetCore.Tests.Support;

internal sealed class DiagnosticLoggerProvider(string category) : ILoggerProvider
{
    internal ConcurrentQueue<Diagnostic> Entries { get; } = new();

    public ILogger CreateLogger(string categoryName) =>
        categoryName == category ? new DiagnosticLogger(Entries) : NullLogger.Instance;

    public void Dispose()
    {
    }

    internal sealed record Diagnostic(LogLevel Level, string Message, Exception Error);

    private sealed class DiagnosticLogger(ConcurrentQueue<Diagnostic> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (exception is not null)
            {
                entries.Enqueue(new Diagnostic(logLevel, formatter(state, exception), exception));
            }
        }
    }
}
