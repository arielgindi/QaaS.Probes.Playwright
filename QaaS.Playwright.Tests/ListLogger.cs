using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace QaaS.Playwright.Tests;

/// <summary>Keeps every log line, so a test can check what the probe warned about.</summary>
public sealed class ListLogger : ILogger
{
    private readonly ConcurrentQueue<(LogLevel Level, string Message)> _entries = new();

    public IEnumerable<string> Messages => _entries.Select(entry => entry.Message);

    public IEnumerable<string> Warnings =>
        _entries.Where(entry => entry.Level == LogLevel.Warning).Select(entry => entry.Message);

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter) => _entries.Enqueue((logLevel, formatter(state, exception)));
}
