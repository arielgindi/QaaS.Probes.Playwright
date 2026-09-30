namespace QaaS.Playwright;

/// <summary>How one flow ended, recorded by the probe and reported by the assertion.</summary>
/// <param name="FlowName">The flow's class name, as listed in the probe's settings.</param>
/// <param name="Passed">Whether the flow finished without throwing.</param>
/// <param name="FailureMessage">Why it failed; <see langword="null"/> when it passed.</param>
/// <param name="FailureScreenshot">A PNG of the page when it failed, if one could be taken.</param>
/// <param name="FailureUrl">The page's URL when it failed.</param>
public sealed record PlaywrightFlowOutcome(
    string FlowName,
    bool Passed,
    string? FailureMessage = null,
    byte[]? FailureScreenshot = null,
    string? FailureUrl = null);
