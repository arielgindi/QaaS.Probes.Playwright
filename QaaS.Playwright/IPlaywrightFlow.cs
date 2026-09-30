using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Playwright;
using QaaS.Framework.SDK.ContextObjects;

namespace QaaS.Playwright;

/// <summary>
/// A browser flow, found by class name and run by <see cref="PlaywrightFlowProbe"/>. Inherit
/// <see cref="BasePlaywrightFlow{TConfiguration}"/> rather than implementing this directly.
/// </summary>
public interface IPlaywrightFlow
{
    /// <summary>The QaaS context: the logger and the run's shared state.</summary>
    Context Context { get; set; }

    /// <summary>The probe's BaseUrl; build URLs from it so a flow works in every environment.</summary>
    string BaseUrl { get; set; }

    /// <summary>
    /// With ForEach, the DataSource item this run is for, as JSON; otherwise null. Implement it (BasePlaywrightFlow
    /// does) to receive the item; the default ignores it, so existing implementations still compile.
    /// </summary>
    JsonNode? Item { get => null; set { } }

    /// <summary>With ForEach, the item's position in the DataSource, from 0; otherwise null.</summary>
    int? ItemIndex { get => null; set { } }

    /// <summary>Binds the flow's <c>FlowConfiguration:&lt;FlowName&gt;</c> section; returns what is invalid in it.</summary>
    List<ValidationResult>? LoadAndValidateConfiguration(IConfiguration configuration);

    /// <summary>
    /// Runs the flow. A probe's flows run in order on one page, so each starts where the previous one left off and
    /// the first starts on BaseUrl.
    /// </summary>
    Task RunAsync(IPage page);
}
