using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Configuration;
using Microsoft.Playwright;
using QaaS.Framework.Configurations;
using QaaS.Framework.SDK.ContextObjects;

namespace QaaS.Playwright;

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
