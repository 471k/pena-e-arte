using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Pena_e_Arte.IntegrationTests.Infrastructure;

/// <summary>A logger that keeps what was logged, for asserting a job's one-line-per-run outcome.</summary>
public sealed class ListLogger<T> : ILogger<T>
{
    public sealed record Entry(LogLevel Level, string Message);

    private readonly ConcurrentQueue<Entry> _entries = new();

    public IReadOnlyList<Entry> Entries => _entries.ToArray();

    public IEnumerable<string> Messages(LogLevel level) =>
        _entries.Where(e => e.Level == level).Select(e => e.Message);

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter) =>
        _entries.Enqueue(new Entry(logLevel, formatter(state, exception)));
}
