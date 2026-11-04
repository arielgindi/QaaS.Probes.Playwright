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
/// <param name="Project">The test project it was found in, its .csproj; null when it was given.</param>
/// <param name="BaseUrlFrom">
/// The test YAML whose probe runs it as a setup flow and gives its <c>BaseUrl</c>; null when none does, or it was given.
/// </param>
internal sealed record LoginFlow(string Flow, string Assembly, string? Settings, bool Found = false, string? Project = null,
    string? BaseUrlFrom = null)
{
    /// <summary>
    /// The BaseUrl the flow runs with, as the tests give it: the <c>BaseUrl</c> of the probe that runs it in
    /// <see cref="BaseUrlFrom"/> or in <see cref="Settings"/>, placeholders resolved; else the address of
    /// <paramref name="startUrl"/>, with no path, as a login flow builds its own (<c>$"{BaseUrl}/login"</c>). With the file
    /// it came from, or null.
    /// </summary>
    public (string Url, string? From) BaseUrlFor(string startUrl)
    {
        foreach (var file in new[] { BaseUrlFrom, Settings }.OfType<string>().Distinct())
            if (LoginSettings.ProbeIn(file, Flow) is { } probe)
                return (probe.BaseUrl, file);
        return (Uri.TryCreate(startUrl, UriKind.Absolute, out var start) ? start.GetLeftPart(UriPartial.Authority) : startUrl, null);
    }

    /// <summary>
    /// Whether the probe that runs it, found where its BaseUrl is, runs its setup flows on a blank page before it opens
    /// BaseUrl (<c>SetupFlowsBeforeBaseUrl</c>), as a login that plants a token first needs; else, as the probe does by
    /// default, BaseUrl opens first. The recording, a test and a replay run it in the same order.
    /// </summary>
    public bool SetupFlowsBeforeBaseUrl() =>
        new[] { BaseUrlFrom, Settings }.OfType<string>().Distinct().Select(file => LoginSettings.ProbeIn(file, Flow))
            .FirstOrDefault(probe => probe is not null)?.SetupFirst ?? false;
}

/// <summary>
/// Runs a login flow on the recording's page as the probe runs a setup flow: found by its class name, given BaseUrl and
/// a QaaS context, its settings bound and checked, then run on the page the recording opened on BaseUrl.
/// </summary>
internal static partial class LoginRunner
{
    /// <summary>
    /// Returns null when the flow passed, else why it failed, as the probe reports it, its secrets hidden, with the
    /// BaseUrl it was given and the page it was on; a flow built against another QaaS.Playwright says so. Once
    /// <paramref name="stopped"/>, what it still logs is not shown: the person logs in by hand.
    /// </summary>
    public static async Task<string?> RunAsync(LoginFlow login, IPage page, string baseUrl, CancellationToken stopped = default,
        string? baseUrlFrom = null)
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
            if (FlowCheck.Explain(failure, login.Flow) is { } plainly) return secrets.Scrub(plainly);
            var on = page.IsClosed ? null : page.Url;
            return secrets.Scrub($"{FlowRunner.Describe(failure)}\n{WhereItWas(login.Flow, failure, on, baseUrl, baseUrlFrom)}");
        }
    }

    /// <summary>
    /// "LoginFlow waited 15 s on https://app/x/login: was BaseUrl right? It was given https://app (from Tests/Smoke.qaas.yaml)."
    /// </summary>
    internal static string WhereItWas(string flow, Exception failure, string? on, string baseUrl, string? baseUrlFrom)
    {
        var given = baseUrlFrom is null
            ? $"It was given BaseUrl {baseUrl}, the address you record on, as no test YAML that runs {flow} gives one"
            : $"It was given BaseUrl {baseUrl}, from {ConsoleUi.Relative(baseUrlFrom)}";
        var waited = Timeout().Match(failure.Message) is { Success: true } timeout
            ? $"{flow} waited {int.Parse(timeout.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) / 1000} s"
            : $"{flow} stopped";
        return $"{waited} on {(on is null or "" ? "a page that closed" : on)}: was BaseUrl right? {given}. If it is not, set " +
               $"BaseUrl in the test YAML that runs {flow} (ProbeConfiguration: BaseUrl:), or log in by hand.";
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"Timeout (\d+)ms")]
    private static partial System.Text.RegularExpressions.Regex Timeout();

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
