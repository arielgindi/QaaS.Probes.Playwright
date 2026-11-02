using System.Collections.Immutable;
using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.DataSourceObjects;
using QaaS.Framework.SDK.Session.SessionDataObjects;

namespace QaaS.Playwright.Tests.EndToEnd;

/// <summary>
/// Drives the real probe and assertion the way the QaaS runner does: every session and assertion shares one Context,
/// a session publishes its name through Activity baggage, and a probe that throws becomes a session failure.
/// </summary>
public sealed class QaasRun
{
    private const string SessionNameBaggageKey = "qaas.probe.session-name";
    private const string ProbeNameBaggageKey = "qaas.probe.probe-name";

    private readonly Context _context;

    public QaasRun() => _context = new Context { Logger = Log };

    public ListLogger Log { get; } = new();

    /// <summary>Runs a session with one probe, given the data sources listed in its DataSourceNames.</summary>
    public SessionData RunSession(
        string name, Dictionary<string, string?> probeConfiguration, params DataSource[] dataSources) =>
        SessionOf(name, [RunProbe(name, "Browser", probeConfiguration, dataSources)]);

    /// <summary>Runs one session's probes at once, as the runner runs the probes of one action stage.</summary>
    public async Task<SessionData> RunSessionAsync(
        string name, params (string Probe, Dictionary<string, string?> Configuration)[] probes)
    {
        var failures = await Task.WhenAll(
            probes.Select(probe => Task.Run(() => RunProbe(name, probe.Probe, probe.Configuration, []))));
        return SessionOf(name, failures);
    }

    /// <summary>Starts every session at once, like sessions that share a stage.</summary>
    public Task<SessionData[]> RunSessionsInParallelAsync(
        IEnumerable<(string Name, Dictionary<string, string?> Configuration)> sessions) =>
        Task.WhenAll(sessions.Select(session => Task.Run(() => RunSession(session.Name, session.Configuration))));

    public PlaywrightFlowAssertion RunAssertion(params SessionData[] sessions)
    {
        var assertion = new PlaywrightFlowAssertion { Context = _context };
        assertion.Assert(sessions.ToImmutableList(), ImmutableList<DataSource>.Empty);
        return assertion;
    }

    private ActionFailure? RunProbe(
        string sessionName, string probeName, Dictionary<string, string?> configuration, DataSource[] dataSources)
    {
        using var scope = new Activity("probe")
            .AddBaggage(SessionNameBaggageKey, sessionName)
            .AddBaggage(ProbeNameBaggageKey, probeName)
            .Start();
        // Like QaaS 4.8, which ignores the errors this returns: the probe throws them when it runs.
        var probe = new PlaywrightFlowProbe { Context = _context };
        probe.LoadAndValidateConfiguration(new ConfigurationBuilder().AddInMemoryCollection(configuration).Build());

        try
        {
            probe.Run(ImmutableList<SessionData>.Empty, [.. dataSources]);
            return null;
        }
        catch (Exception failure)
        {
            return new ActionFailure { Name = probeName, Reason = new Reason { Message = failure.Message } };
        }
    }

    private static SessionData SessionOf(string name, IEnumerable<ActionFailure?> failures) =>
        new() { Name = name, SessionFailures = [.. failures.OfType<ActionFailure>()] };
}
