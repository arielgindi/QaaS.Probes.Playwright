namespace QaaS.Playwright;

/// <summary>
/// The result of running one Playwright flow, recorded by the probe and reported by the assertion.
/// </summary>
/// <param name="FlowName">The flow's class name, as listed in the probe configuration.</param>
/// <param name="Passed"><see langword="true"/> when the flow completed without throwing.</param>
/// <param name="FailureMessage">
/// The failure reason when <paramref name="Passed"/> is <see langword="false"/>; otherwise <see langword="null"/>.
/// </param>
/// <param name="FailureScreenshot">
/// A PNG of the page captured at the moment of failure, when one could be taken; otherwise <see langword="null"/>.
/// Always <see langword="null"/> for a passing flow.
/// </param>
public sealed record PlaywrightFlowOutcome(
    string FlowName,
    bool Passed,
    string? FailureMessage = null,
    byte[]? FailureScreenshot = null);
