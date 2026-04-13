using Microsoft.Extensions.Configuration;
using Microsoft.Playwright;
using QaaS.Framework.Configurations;
using QaaS.Framework.SDK.ContextObjects;

namespace QaaS.Probes.Playwright;

/// <summary>
/// Contract for a recorded browser flow that the probe discovers and runs.
/// </summary>
public interface IPlaywrightFlow
{
    Context Context { get; set; }
    void LoadAndValidateConfiguration(IConfiguration configuration);
    Task RunAsync(IPage page);
}

/// <summary>
/// Base class for Playwright flows with typed configuration — mirrors the QaaS pattern
/// used by BaseProbe&lt;T&gt;, BaseGenerator&lt;T&gt;, and BaseAssertion&lt;T&gt;.
///
/// YAML under <c>FlowConfiguration:</c> is automatically bound to <typeparamref name="TConfiguration"/>
/// using the same <c>BindToObject</c> mechanism as all QaaS hooks.
/// </summary>
public abstract class BasePlaywrightFlow<TConfiguration> : IPlaywrightFlow where TConfiguration : new()
{
    public Context Context { get; set; } = null!;

    /// <summary>
    /// Typed configuration bound from the <c>FlowConfiguration</c> YAML section.
    /// </summary>
    public TConfiguration Configuration { get; set; } = default!;

    public void LoadAndValidateConfiguration(IConfiguration configuration)
    {
        Configuration = configuration.BindToObject<TConfiguration>(new BinderOptions
        {
            ErrorOnUnknownConfiguration = true
        }, Context.Logger);
    }

    public abstract Task RunAsync(IPage page);
}
