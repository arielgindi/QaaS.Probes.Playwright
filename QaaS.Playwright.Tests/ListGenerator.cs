using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Configuration;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.DataSourceObjects;
using QaaS.Framework.SDK.Hooks.Generator;
using QaaS.Framework.SDK.Session.DataObjects;
using QaaS.Framework.SDK.Session.SessionDataObjects;

namespace QaaS.Playwright.Tests;

/// <summary>A generator that yields the given bodies, as a YAML list turned into one item each would.</summary>
public sealed class ListGenerator(params object?[] bodies) : IGenerator
{
    public Context Context { get; set; } = null!;

    public static DataSource DataSource(string name, params object?[] bodies) =>
        new() { Name = name, Generator = new ListGenerator(bodies) };

    public List<ValidationResult>? LoadAndValidateConfiguration(IConfiguration configuration) => [];

    public IEnumerable<Data<object>> Generate(
        IImmutableList<SessionData> sessionDataList, IImmutableList<DataSource> dataSourceList) =>
        bodies.Select(body => new Data<object> { Body = body });
}
