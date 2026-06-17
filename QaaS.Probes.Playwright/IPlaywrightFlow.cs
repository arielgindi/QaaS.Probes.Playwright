using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Configuration;
using Microsoft.Playwright;
using QaaS.Framework.Configurations;
using QaaS.Framework.SDK.ContextObjects;

namespace QaaS.Probes.Playwright;

/// <summary>
/// Contract for a browser flow. The probe discovers implementations by class name (the same pattern as QaaS's
/// HookProvider) and calls <see cref="RunAsync"/> with an active Playwright page.
///
/// Implement flows by inheriting <see cref="BasePlaywrightFlow{TConfiguration}"/> rather than this interface.
/// </summary>
public interface IPlaywrightFlow
{
    /// <summary>QaaS execution context — provides the logger and run-scoped shared state.</summary>
    Context Context { get; set; }

    /// <summary>
    /// The base URL from the probe's configuration. The probe navigates the (shared) page here once, before the
    /// first flow; use it to build URLs so the same flow works across environments.
    /// </summary>
    string BaseUrl { get; set; }

    /// <summary>
    /// Called by the probe before <see cref="RunAsync"/>. Binds the flow's <c>FlowConfiguration</c> section to its
    /// typed configuration record and validates it.
    /// </summary>
    /// <returns>Validation failures, or an empty/null list when the configuration is valid.</returns>
    List<ValidationResult>? LoadAndValidateConfiguration(IConfiguration configuration);

    /// <summary>
    /// Runs the browser flow. All flows in a probe run share one page in order, so cookies and session state
    /// persist; the page is wherever the previous flow left it (the first flow starts on <see cref="BaseUrl"/>).
    /// </summary>
    Task RunAsync(IPage page);
}

/// <summary>
/// Base class for Playwright flows with typed configuration, following the same pattern as QaaS's
/// <c>BaseProbe&lt;T&gt;</c> / <c>BaseGenerator&lt;T&gt;</c> / <c>BaseAssertion&lt;T&gt;</c>: define a config record,
/// let YAML under <c>FlowConfiguration:&lt;FlowClassName&gt;</c> bind to it, and read <c>Configuration</c> in
/// <see cref="RunAsync"/>.
/// </summary>
/// <example>
/// <code>
/// public sealed class LoginFlow : BasePlaywrightFlow&lt;LoginFlowConfig&gt;
/// {
///     public override Task RunAsync(IPage page) =&gt;
///         page.GetByLabel("Username").FillAsync(Configuration.Username);
/// }
///
/// public sealed record LoginFlowConfig { public string Username { get; init; } = ""; }
/// </code>
/// </example>
public abstract class BasePlaywrightFlow<TConfiguration> : IPlaywrightFlow where TConfiguration : new()
{
    /// <inheritdoc />
    public Context Context { get; set; } = null!;

    /// <inheritdoc />
    public string BaseUrl { get; set; } = null!;

    /// <summary>
    /// Typed configuration bound from the flow's <c>FlowConfiguration:&lt;FlowClassName&gt;</c> section. Defaults to
    /// a fresh instance so reads before binding return defaults rather than throwing.
    /// </summary>
    public TConfiguration Configuration { get; set; } = new();

    /// <inheritdoc />
    public List<ValidationResult>? LoadAndValidateConfiguration(IConfiguration configuration)
    {
        // Strict binding so a typo in the flow's YAML is reported rather than silently ignored.
        Configuration = configuration.BindToObject<TConfiguration>(
            new BinderOptions { ErrorOnUnknownConfiguration = true }, Context.Logger);

        var validationResults = new List<ValidationResult>();
        Validator.TryValidateObject(
            Configuration!, new ValidationContext(Configuration!), validationResults, validateAllProperties: true);
        return validationResults;
    }

    /// <inheritdoc />
    public abstract Task RunAsync(IPage page);
}
