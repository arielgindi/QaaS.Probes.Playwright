using System.Reflection;
using Microsoft.Extensions.Configuration;

namespace QaaS.Playwright.Recorder.Login;

/// <summary>
/// What a login flow needs to run, said for a person: whether it has values at all, the ones its YAML gives, the ones
/// it needs that nothing gives (a required one, or text that is empty unless given, as a user name is), and what is
/// wrong with the others, as the probe checks them.
/// </summary>
/// <param name="Has">Its settings have any value.</param>
/// <param name="Given">The values its YAML gives.</param>
/// <param name="Missing">The values it needs that nothing gives.</param>
/// <param name="Problems">What is wrong with the values given, as the probe would say it.</param>
internal sealed record LoginValues(bool Has, IReadOnlyList<string> Given, IReadOnlyList<string> Missing, IReadOnlyList<string> Problems)
{
    /// <summary>The values of <paramref name="flow"/>, with its YAML at <paramref name="settings"/>, or none.</summary>
    public static LoginValues Of(Type flow, string? settings)
    {
        var type = SettingsTypeOf(flow);
        var names = type?.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanWrite && property.GetIndexParameters().Length == 0)
            .ToList() ?? [];
        if (names.Count == 0) return new LoginValues(false, [], [], []);

        IConfigurationSection section;
        try
        {
            section = LoginSettings.Read(settings, flow.Name).GetSection(flow.Name);
        }
        catch (InvalidOperationException unreadable)
        {
            return new LoginValues(true, [], [], [unreadable.Message]);
        }

        var given = names.Where(property => section.GetSection(property.Name).Exists()).Select(property => property.Name).ToList();
        var defaults = Create(type!);
        var problems = ProblemsOf(flow, section);
        var missing = names.Where(property => !given.Contains(property.Name)
                                              && (Empty(property, defaults) || problems.Any(problem => About(problem, section, property.Name))))
            .Select(property => property.Name).ToList();
        return new LoginValues(true, given,
            missing, [.. problems.Where(problem => !missing.Any(name => About(problem, section, name)))]);
    }

    // BasePlaywrightFlow<TSettings>'s TSettings.
    private static Type? SettingsTypeOf(Type flow)
    {
        for (var type = flow; type is not null; type = type.BaseType)
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(BasePlaywrightFlow<>))
                return type.GetGenericArguments()[0];
        return null;
    }

    private static object? Create(Type type)
    {
        try
        {
            return Activator.CreateInstance(type);
        }
        catch (Exception failure) when (failure is TargetInvocationException or MissingMethodException or MemberAccessException)
        {
            return null;
        }
    }

    // Text with no value unless given, as a user name or an address is.
    private static bool Empty(PropertyInfo property, object? defaults) =>
        defaults is not null && (property.PropertyType == typeof(string) || property.PropertyType == typeof(Uri))
                             && property.GetValue(defaults) is null or "";

    // The flow's own check of its settings, as the probe runs it before the browser opens.
    private static List<string> ProblemsOf(Type flow, IConfigurationSection section)
    {
        try
        {
            return Activator.CreateInstance(flow) is IPlaywrightFlow created
                ? [.. (created.LoadAndValidateConfiguration(section) ?? []).Select(problem => problem.ErrorMessage ?? "")
                    .Where(problem => problem.Length > 0)]
                : [];
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            return [];
        }
    }

    private static bool About(string problem, IConfigurationSection section, string name) =>
        problem.StartsWith($"{section.Path}:{name}:", StringComparison.OrdinalIgnoreCase)
        || problem.Contains($" {name} field ", StringComparison.Ordinal);
}
