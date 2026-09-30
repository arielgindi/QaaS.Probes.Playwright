using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Playwright;
using QaaS.Framework.Configurations;
using QaaS.Framework.SDK.ContextObjects;

namespace QaaS.Playwright;

/// <summary>
/// A flow with typed settings, like QaaS's <c>BaseProbe&lt;T&gt;</c>: its <c>FlowConfiguration:&lt;FlowName&gt;</c>
/// section binds to <typeparamref name="TConfiguration"/>, which <see cref="RunAsync"/> reads as
/// <see cref="Configuration"/>.
/// </summary>
public abstract class BasePlaywrightFlow<TConfiguration> : IPlaywrightFlow where TConfiguration : new()
{
    public Context Context { get; set; } = null!;

    public string BaseUrl { get; set; } = null!;

    public JsonNode? Item { get; set; }

    public int? ItemIndex { get; set; }

    /// <summary>The flow's settings; defaults until the probe binds them.</summary>
    public TConfiguration Configuration { get; set; } = new();

    public List<ValidationResult>? LoadAndValidateConfiguration(IConfiguration configuration)
    {
        // Strict, so the binder at least logs a warning for a misspelled setting.
        Configuration = configuration.BindToObject<TConfiguration>(
            new BinderOptions { ErrorOnUnknownConfiguration = true }, Context.Logger);

        var errors = new List<ValidationResult>();
        Validator.TryValidateObject(
            Configuration!, new ValidationContext(Configuration!), errors, validateAllProperties: true);
        return errors;
    }

    public abstract Task RunAsync(IPage page);
}
