namespace QaaS.Playwright.Flows;

/// <summary>
/// Ends a flow before its end on purpose, e.g. after the step the recorder's "Run to here" runs to. It is no failure:
/// <see cref="FlowRunner"/> disposes the flow and lets it through, without recording an outcome or taking a screenshot.
/// </summary>
internal sealed class FlowStoppedException() : Exception("The flow was stopped on purpose.");
