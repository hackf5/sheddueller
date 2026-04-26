namespace Sheddueller.Tests.Logging;

using System.Collections.Concurrent;

using Microsoft.Extensions.Logging;

internal sealed class TestLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<TestLogEntry> _entries = new();

    public IReadOnlyList<TestLogEntry> Entries
      => [.. this._entries];

    public ILogger CreateLogger(string categoryName)
      => new TestLogger(categoryName, this._entries);

    public void Dispose()
    {
    }

    public TestLogEntry SingleByEventId(int eventId)
      => this.Entries.Single(entry => entry.EventId.Id == eventId);

    public bool HasEventId(int eventId)
      => this.Entries.Any(entry => entry.EventId.Id == eventId);

    private sealed class TestLogger(
        string categoryName,
        ConcurrentQueue<TestLogEntry> entries) : ILogger
    {
        public IDisposable BeginScope<TState>(TState state)
          where TState : notnull
          => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel)
          => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            var properties = ReadProperties(state);
            properties.TryGetValue("{OriginalFormat}", out var messageTemplate);

            entries.Enqueue(new TestLogEntry(
              categoryName,
              logLevel,
              eventId,
              messageTemplate as string,
              formatter(state, exception),
              exception,
              properties));
        }

        private static Dictionary<string, object?> ReadProperties<TState>(TState state)
        {
            if (state is not IEnumerable<KeyValuePair<string, object?>> pairs)
            {
                return new Dictionary<string, object?>(StringComparer.Ordinal);
            }

            var properties = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var pair in pairs)
            {
                properties[pair.Key] = pair.Value;
            }

            return properties;
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}

internal sealed record TestLogEntry(
    string CategoryName,
    LogLevel Level,
    EventId EventId,
    string? MessageTemplate,
    string RenderedMessage,
    Exception? Exception,
    IReadOnlyDictionary<string, object?> Properties);
