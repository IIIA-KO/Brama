using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Brama.Tests.Hosting;

/// <summary>
/// Captures what Brama logged, so tests can assert on the decision Brama makes (what and at which
/// level) without asserting on any particular sink's formatting.
/// </summary>
public sealed class RecordingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<(LogLevel Level, string Message)> _entries = new();

    public IReadOnlyList<string> Entries => [.. _entries.Select(entry => entry.Message)];

    public IReadOnlyList<LogLevel> Levels => [.. _entries.Select(entry => entry.Level)];

    public ILogger CreateLogger(string categoryName) =>
        categoryName?.StartsWith("Brama", StringComparison.Ordinal) == true
            ? new RecordingLogger(_entries)
            : Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

    public void Dispose()
    {
    }

    private sealed class RecordingLogger : ILogger
    {
        private readonly ConcurrentQueue<(LogLevel Level, string Message)> _entries;

        public RecordingLogger(ConcurrentQueue<(LogLevel Level, string Message)> entries) => _entries = entries;

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            _entries.Enqueue((logLevel, formatter(state, exception)));
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}
