namespace QaaS.Playwright;

/// <summary>How one flow ended, recorded by the probe and reported by the assertion.</summary>
/// <param name="FlowName">The flow's class name, as listed in the probe's settings.</param>
/// <param name="Passed">Whether the flow finished without throwing.</param>
/// <param name="FailureMessage">Why it failed; <see langword="null"/> when it passed.</param>
/// <param name="FailureScreenshot">A PNG of the flow's page when it failed, if one could be taken.</param>
/// <param name="FailureUrl">The flow's page's URL when it failed.</param>
/// <param name="ProbeName">The probe that ran the flow, when the runner names it.</param>
/// <param name="FailureDetail">
/// The whole failure, for the report's trace: the exception with its causes and stack, and why no screenshot was taken
/// when none was.
/// </param>
/// <param name="FailurePopups">
/// The pop-ups the flow left open when it failed, in the order they opened. Nothing tells which page a failed call was
/// on, so the failure may be about one of them rather than the flow's page.
/// </param>
public sealed record PlaywrightFlowOutcome(
    string FlowName,
    bool Passed,
    string? FailureMessage = null,
    byte[]? FailureScreenshot = null,
    string? FailureUrl = null,
    string? ProbeName = null,
    string? FailureDetail = null,
    IReadOnlyList<PlaywrightFlowPopup>? FailurePopups = null)
{
    /// <summary>An outcome with only the flow's name, whether it passed, why not, and its screenshot.</summary>
    public PlaywrightFlowOutcome(string flowName, bool passed, string? failureMessage, byte[]? failureScreenshot)
        : this(flowName, passed, failureMessage, failureScreenshot, FailureUrl: null)
    {
    }

    /// <summary>The flow's name, whether it passed, why not, and its screenshot.</summary>
    public void Deconstruct(
        out string flowName, out bool passed, out string? failureMessage, out byte[]? failureScreenshot) =>
        (flowName, passed, failureMessage, failureScreenshot) = (FlowName, Passed, FailureMessage, FailureScreenshot);
}

/// <summary>A pop-up a failed flow left open.</summary>
/// <param name="Url">Its URL when the flow failed.</param>
/// <param name="Screenshot">A PNG of it, if one could be taken.</param>
public sealed record PlaywrightFlowPopup(string Url, byte[]? Screenshot);
