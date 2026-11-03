using System.ComponentModel.DataAnnotations;
using System.Globalization;
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
    /// <inheritdoc />
    public Context Context { get; set; } = null!;

    /// <inheritdoc />
    public string BaseUrl { get; set; } = null!;

    /// <inheritdoc />
    public JsonNode? Item { get; set; }

    /// <inheritdoc />
    public int? ItemIndex { get; set; }

    /// <summary>The flow's settings; defaults until the probe binds them.</summary>
    public TConfiguration Configuration { get; set; } = new();

    private string? _runStamp;

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

    /// <inheritdoc />
    public abstract Task RunAsync(IPage page);

    /// <summary>
    /// <paramref name="value"/> with this run's stamp appended, e.g. <c>Printer offline 1103-142501-k7q</c>: for a value
    /// that must not repeat from run to run, such as the name of a record the flow creates. The stamp is the time the
    /// run first asked for it, and three random characters for runs that start at once; it stays the same for the whole
    /// run, so a later step finds what an earlier one typed.
    /// </summary>
    protected string Unique(string value) => $"{value} {_runStamp ??= NewStamp()}";

    private static string NewStamp()
    {
        const string characters = "0123456789abcdefghijklmnopqrstuvwxyz";
        var random = string.Concat(Enumerable.Range(0, 3).Select(_ => characters[Random.Shared.Next(characters.Length)]));
        return $"{DateTime.Now.ToString("MMdd-HHmmss", CultureInfo.InvariantCulture)}-{random}";
    }
}
