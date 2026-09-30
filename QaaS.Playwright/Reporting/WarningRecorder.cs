using Microsoft.Extensions.Logging;
using QaaS.Framework.SDK.ContextObjects;

namespace QaaS.Playwright.Reporting;

/// <summary>
/// The run's logger, which also records each warning for the session's <see cref="PlaywrightFlowAssertion"/> to show in
/// the report: a warning only on the console is one nobody reads.
/// </summary>
internal sealed class WarningRecorder(Context context, string? sessionName) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => context.Logger.BeginScope(state);

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning || context.Logger.IsEnabled(logLevel);

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (logLevel >= LogLevel.Warning)
            PlaywrightFlowResults.RecordWarning(context, sessionName, formatter(state, exception));
        context.Logger.Log(logLevel, eventId, state, exception, formatter);
    }
}
