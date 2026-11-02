using System.Collections;
using System.Globalization;
using Microsoft.Extensions.Logging;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Playwright.Configuration;

namespace QaaS.Playwright.Reporting;

/// <summary>
/// The run's logger, which also records each warning for the session's <see cref="PlaywrightFlowAssertion"/> to show in
/// the report: a warning only on the console is one nobody reads. It hides the run's secrets in both: in the message,
/// in each of its properties, e.g. {Flow}, which it passes on for structured sinks, and in the exception.
/// </summary>
internal sealed class WarningRecorder(Context context, string? sessionName, Secrets secrets) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => context.Logger.BeginScope(state);

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning || context.Logger.IsEnabled(logLevel);

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var message = secrets.Scrub(formatter(state, exception));
        if (logLevel >= LogLevel.Warning) PlaywrightFlowResults.RecordWarning(context, sessionName, message);
        var properties = (state as IEnumerable<KeyValuePair<string, object?>> ?? [])
            .Select(property => KeyValuePair.Create(property.Key, Scrub(property.Value)));
        context.Logger.Log(logLevel, eventId, new ScrubbedState(message, properties),
            exception is null ? null : secrets.Scrub(exception), static (scrubbed, _) => scrubbed.ToString());
    }

    // A plain value, such as a string or a number, stays as it is unless its text holds a secret. Any other, which a
    // structured sink may take apart, becomes its text, a list's items joined as in the message.
    private object? Scrub(object? value)
    {
        if (value is null) return null;
        var text = value is IEnumerable items and not string
            ? string.Join(", ", items.Cast<object?>())
            : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        var scrubbed = secrets.Scrub(text);
        return value is IConvertible && scrubbed == text ? value : scrubbed;
    }

    // The call's properties as structured sinks read them, a list of names and values; its text is the message.
    private sealed class ScrubbedState(string message, IEnumerable<KeyValuePair<string, object?>> properties)
        : List<KeyValuePair<string, object?>>(properties)
    {
        public override string ToString() => message;
    }
}
