using Microsoft.Extensions.Configuration;
using Microsoft.Playwright;
using QaaS.Framework.Configurations;
using QaaS.Framework.SDK.ContextObjects;

namespace QaaS.Probes.Playwright;

/// <summary>
/// Contract for a recorded browser flow.
/// The probe discovers implementations by class name (same pattern as QaaS's HookProvider)
/// and calls RunAsync with an active Playwright page.
///
/// Implementations should inherit from <see cref="BasePlaywrightFlow{TConfiguration}"/>
/// instead of implementing this interface directly.
/// </summary>
public interface IPlaywrightFlow
{
    /// <summary>QaaS execution context — provides the logger.</summary>
    Context Context { get; set; }

    /// <summary>
    /// The base URL from the probe's YAML configuration.
    /// Use this for navigation instead of hardcoding URLs, so the same flow
    /// works across environments (staging, production, etc).
    /// </summary>
    string BaseUrl { get; set; }

    /// <summary>
    /// Called by the probe before RunAsync. Binds the FlowConfiguration YAML section
    /// to the flow's typed configuration record.
    /// </summary>
    void LoadAndValidateConfiguration(IConfiguration configuration);

    /// <summary>
    /// Executes the browser flow. The page is already open and navigated to BaseUrl.
    /// Cookies and session state persist across flows in the same probe.
    /// </summary>
    Task RunAsync(IPage page);
}

/// <summary>
/// Base class for Playwright flows with typed configuration.
///
/// Follows the same pattern as QaaS's BaseProbe&lt;T&gt;, BaseGenerator&lt;T&gt;, BaseAssertion&lt;T&gt;:
/// - Define a config record with your properties
/// - YAML under FlowConfiguration: auto-binds to it via BindToObject
/// - Access values as Configuration.PropertyName in RunAsync
///
/// Example:
/// <code>
/// public class LoginFlow : BasePlaywrightFlow&lt;LoginFlowConfig&gt;
/// {
///     public override async Task RunAsync(IPage page)
///     {
///         await page.GetByLabel("Username").FillAsync(Configuration.Username);
///     }
/// }
/// public record LoginFlowConfig { public string Username { get; set; } }
/// </code>
/// </summary>
public abstract class BasePlaywrightFlow<TConfiguration> : IPlaywrightFlow where TConfiguration : new()
{
    public Context Context { get; set; } = null!;
    public string BaseUrl { get; set; } = null!;

    /// <summary>
    /// Typed configuration bound from YAML. Each flow gets its own section
    /// under FlowConfiguration:FlowClassName: in the probe's YAML config.
    /// </summary>
    public TConfiguration Configuration { get; set; } = default!;

    /// <summary>
    /// Binds the IConfiguration section to <typeparamref name="TConfiguration"/>
    /// using QaaS's BindToObject — same binding that all QaaS hooks use.
    /// ErrorOnUnknownConfiguration is true so typos in YAML are caught early.
    /// </summary>
    public void LoadAndValidateConfiguration(IConfiguration configuration)
    {
        Configuration = configuration.BindToObject<TConfiguration>(new BinderOptions
        {
            ErrorOnUnknownConfiguration = true
        }, Context.Logger);
    }

    public abstract Task RunAsync(IPage page);
}
