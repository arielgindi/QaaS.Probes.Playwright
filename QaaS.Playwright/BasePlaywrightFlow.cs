using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Playwright;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Playwright.Configuration;

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

    /// <summary>
    /// Binds the flow's settings and returns every mistake in them with its path, e.g.
    /// <c>FlowConfiguration:LoginFlow:Usernmae: not a setting (known: Username)</c>: keys that are no setting, values
    /// that do not convert, and DataAnnotations errors, those of nested settings and list items included.
    /// </summary>
    public List<ValidationResult>? LoadAndValidateConfiguration(IConfiguration configuration)
    {
        (Configuration, var problems) =
            StrictBinder.Bind<TConfiguration>(configuration, (configuration as IConfigurationSection)?.Path ?? "");
        return [.. problems.Select(problem => new ValidationResult(problem.ToString()))];
    }

    public abstract Task RunAsync(IPage page);
}
