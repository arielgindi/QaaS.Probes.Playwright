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

    private readonly Context _context;

    public QaasRun() => _context = new Context { Logger = Log };

    public ListLogger Log { get; } = new();

    public SessionData RunSession(string name, Dictionary<string, string?> probeConfiguration)
    {
        using var session = new Activity("session").AddBaggage(SessionNameBaggageKey, name).Start();
        var probe = new PlaywrightFlowProbe { Context = _context };
        var errors = probe.LoadAndValidateConfiguration(
            new ConfigurationBuilder().AddInMemoryCollection(probeConfiguration).Build());
        Assert.That(errors, Is.Empty, "probe configuration");

        try
        {
            probe.Run(ImmutableList<SessionData>.Empty, ImmutableList<DataSource>.Empty);
            return new SessionData { Name = name };
        }
        catch (Exception failure)
        {
            var reason = new Reason { Message = failure.Message };
            return new SessionData { Name = name, SessionFailures = [new ActionFailure { Name = "Probe", Reason = reason }] };
        }
    }

    /// <summary>Starts every session at once, like sessions that share a stage.</summary>
    public Task<SessionData[]> RunSessionsInParallelAsync(IEnumerable<(string Name, Dictionary<string, string?> Configuration)> sessions) =>
        Task.WhenAll(sessions.Select(session => Task.Run(() => RunSession(session.Name, session.Configuration))));

    public PlaywrightFlowAssertion RunAssertion(params SessionData[] sessions)
    {
        var assertion = new PlaywrightFlowAssertion { Context = _context };
        assertion.Assert(sessions.ToImmutableList(), ImmutableList<DataSource>.Empty);
        return assertion;
    }
}
