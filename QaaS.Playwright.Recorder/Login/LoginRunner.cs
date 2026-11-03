using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Playwright.Configuration;
using QaaS.Playwright.Flows;

namespace QaaS.Playwright.Recorder.Login;

/// <summary>A flow of the person's tests to run before recording, e.g. their login, and where it is.</summary>
/// <param name="Flow">The flow's class name, as SetupFlows names it.</param>
/// <param name="Assembly">The DLL in the test project's bin folder that has the flow, or the project's own.</param>
/// <param name="Settings">A YAML file with the flow's settings, or null for their defaults.</param>
/// <param name="Found">
/// The recorder found it in the tests, rather than the person giving it: it is found again each time, in the newest
/// build.
/// </param>
internal sealed record LoginFlow(string Flow, string Assembly, string? Settings, bool Found = false);

/// <summary>
/// Runs a login flow on the recording's page as the probe runs a setup flow: found by its class name, given BaseUrl and
/// a QaaS context, its settings bound and checked, then run on the page the recording opened on BaseUrl.
/// </summary>
internal static class LoginRunner
{
    /// <summary>
    /// Returns null when the flow passed, else why it failed, as the probe reports it, its secrets hidden. Once
    /// <paramref name="stopped"/>, what it still logs is not shown: the person logs in by hand.
    /// </summary>
    public static async Task<string?> RunAsync(LoginFlow login, IPage page, string baseUrl, CancellationToken stopped = default)
    {
        var secrets = Secrets.None;
        try
        {
            var flowConfiguration = LoginSettings.Read(login.Settings, login.Flow);
            secrets = Secrets.Of(flowConfiguration, "");
            FlowAssemblyContext.Load(login.Assembly);
            var context = new Context { Logger = new ConsoleLogger(secrets, stopped) };
            var (flow, problems) = FlowLoader.Load(login.Flow, context, baseUrl, flowConfiguration);
            try
            {
                if (problems.Count > 0)
                    throw new InvalidOperationException($"{login.Flow} cannot run with its values: {string.Join("; ", problems)}");
                await flow.RunAsync(page);
            }
            finally
            {
                await FlowLoader.DisposeAsync(flow);
            }

            return null;
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            return secrets.Scrub(FlowRunner.Describe(failure));
        }
    }

    /// <summary>Hides the login's secrets, e.g. its password, in what is shown of it while it runs.</summary>
    public static Func<string, string> Scrubber(LoginFlow login)
    {
        try
        {
            return Secrets.Of(LoginSettings.Read(login.Settings, login.Flow), "").Scrub;
        }
        catch (InvalidOperationException)
        {
            return text => text; // The flow fails on its settings before it shows anything.
        }
    }

    // What the flow logs, in the console, its secrets hidden; nothing once it was stopped.
    private sealed class ConsoleLogger(Secrets secrets, CancellationToken stopped) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information && !stopped.IsCancellationRequested;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel)) ConsoleUi.FlowLog(secrets.Scrub(formatter(state, exception)));
        }
    }
}
