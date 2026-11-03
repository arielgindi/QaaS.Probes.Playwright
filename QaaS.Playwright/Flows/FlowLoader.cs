using Microsoft.Extensions.Configuration;
using QaaS.Framework.SDK.ContextObjects;

namespace QaaS.Playwright.Flows;

/// <summary>
/// Creates a flow and binds its settings, the same way for the probe's check before the run and for each run: a flow's
/// loader may build URLs from BaseUrl, and a flow may hold a resource until it is disposed.
/// </summary>
internal static class FlowLoader
{
    /// <summary>Returns the flow and its settings' problems; dispose it with <see cref="DisposeAsync"/>.</summary>
    /// <exception cref="InvalidOperationException">
    /// The flow cannot be found or created, or its LoadAndValidateConfiguration threw; the message names the flow.
    /// </exception>
    public static (IPlaywrightFlow Flow, List<string> Problems) Load(string flowName, Context context, string baseUrl,
        IConfiguration flowConfiguration, FlowItem? item = null) =>
        Load(FlowDiscovery.Resolve(flowName), flowName, context, baseUrl, flowConfiguration, item);

    /// <summary>
    /// Sets up a flow its caller created, as <see cref="Load(string, Context, string, IConfiguration, FlowItem?)"/> sets
    /// up one it finds by name, e.g. the one the recorder has just built to test.
    /// </summary>
    /// <exception cref="InvalidOperationException">Its LoadAndValidateConfiguration threw; the message names the flow.</exception>
    public static (IPlaywrightFlow Flow, List<string> Problems) Load(IPlaywrightFlow flow, string flowName, Context context,
        string baseUrl, IConfiguration flowConfiguration, FlowItem? item = null)
    {
        flow.Context = context;
        flow.BaseUrl = baseUrl;
        flow.Item = item?.Data;
        flow.ItemIndex = item?.Index;
        try
        {
            var errors = flow.LoadAndValidateConfiguration(flowConfiguration.GetSection(flowName)) ?? [];
            return (flow, [.. errors.Select(error => error.ErrorMessage ?? $"{flowName}: invalid settings")]);
        }
        catch (Exception failure)
        {
            // Loading is synchronous, as QaaS calls it.
            DisposeAsync(flow).GetAwaiter().GetResult();
            throw new InvalidOperationException(
                $"{flowName}: loading its settings threw {failure.GetType().Name}: {failure.Message}", failure);
        }
    }

    public static async Task DisposeAsync(IPlaywrightFlow flow)
    {
        if (flow is IAsyncDisposable asyncDisposable) await asyncDisposable.DisposeAsync();
        else (flow as IDisposable)?.Dispose();
    }
}
