using System.Collections;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using QaaS.Framework.Configurations;

namespace QaaS.Playwright.Configuration;

/// <summary>A mistake in the settings at <see cref="Path"/>, e.g. <c>FlowConfiguration:LoginFlow:User</c>.</summary>
internal readonly record struct SettingProblem(string Path, string Text)
{
    public override string ToString() => Path.Length == 0 ? Text : $"{Path}: {Text}";
}

/// <summary>
/// Binds settings as QaaS does, and finds every mistake QaaS 4.8 only logs, passes through or crashes on: a key that is
/// no setting, a value that does not convert (QaaS binds the type's default instead), a <c>${...}</c> placeholder left
/// unresolved, a single value where a list or settings belong or the other way round, and every DataAnnotations error
/// of the bound settings, nested ones included.
/// </summary>
internal static class StrictBinder
{
    private enum Shape { Value, List, Dictionary, Settings, Anything }

    /// <param name="configuration">The settings to bind.</param>
    /// <param name="path">Where they are, for the problems' paths, e.g. <c>FlowConfiguration:LoginFlow</c>.</param>
    /// <param name="siblingKeys">Keys that are no setting but belong next to the settings; the caller checks them.</param>
    public static (T Settings, List<SettingProblem> Problems) Bind<T>(
        IConfiguration configuration, string path, params string[] siblingKeys) where T : new()
    {
        var problems = ShapeProblems(configuration, null, typeof(T), path, siblingKeys).ToList();
        T settings;
        try
        {
            // QaaS would only log what the problems already say.
            settings = configuration.BindToObject<T>(
                new BinderOptions { ErrorOnUnknownConfiguration = false }, NullLogger.Instance);
        }
        catch (Exception failure)
        {
            // QaaS throws on some shapes, e.g. a list where a single value belongs; a problem found above says which.
            if (problems.Count == 0) problems.Add(new SettingProblem(path, failure.Message));
            return (new T(), problems);
        }

        // One problem per path: a value that did not convert is not also reported as out of range.
        return (settings, [.. problems.Concat(ValidationProblems(settings, path)).DistinctBy(problem => problem.Path)]);
    }

    private static IEnumerable<SettingProblem> ShapeProblems(
        IConfiguration node, string? value, Type type, string path, string[] siblingKeys)
    {
        var shape = ShapeOf(type);
        var children = node.GetChildren().ToList();
        var hasValue = !string.IsNullOrEmpty(value);
        if (shape == Shape.Anything) yield break;

        if (shape == Shape.Value)
        {
            // An empty value counts too: QaaS binds a YAML null in a list of numbers by dropping it, shifting the rest.
            if (children.Count > 0) yield return new(path, "expected a single value, not a list or settings");
            else if (value?.Contains("${") == true) yield return new(path, $"'{value}' holds a placeholder QaaS did not resolve");
            else if (value is not null && !Converts(value, type)) yield return new(path, $"'{value}' is not {Describe(type)}");
            yield break;
        }

        if (hasValue)
        {
            yield return new(path, shape == Shape.List ? $"expected a list, e.g. [{value}]" : $"expected settings, not '{value}'");
            yield break;
        }

        if (shape == Shape.List && children.Any(child => !int.TryParse(child.Key, out _)))
        {
            yield return new(path, "expected a list, not settings");
            yield break;
        }

        foreach (var child in children.Where(child => !siblingKeys.Contains(child.Key, StringComparer.OrdinalIgnoreCase)))
        {
            var childPath = Join(path, child.Key);
            if (TypeOf(type, shape, child.Key) is not { } childType)
            {
                var known = SettingsOf(type).Select(property => property.Name).Concat(siblingKeys).ToList();
                yield return new(childPath, $"not a setting (known: {(known.Count > 0 ? string.Join(", ", known) : "none")})");
                continue;
            }

            foreach (var problem in ShapeProblems(child, child.Value, childType, childPath, [])) yield return problem;
        }
    }

    // Validator.TryValidateObject checks one object; this also checks its nested settings, list items and dictionary values.
    private static IEnumerable<SettingProblem> ValidationProblems(object? settings, string path) =>
        settings is null ? [] : ShapeOf(settings.GetType()) switch
        {
            Shape.List => ((IList)settings).Cast<object?>()
                .SelectMany((item, index) => ValidationProblems(item, Join(path, $"{index}"))),
            Shape.Dictionary => ((IDictionary)settings).Cast<DictionaryEntry>()
                .SelectMany(entry => ValidationProblems(entry.Value, Join(path, $"{entry.Key}"))),
            Shape.Settings => AnnotationProblems(settings, path).Concat(SettingsOf(settings.GetType())
                .SelectMany(property => ValidationProblems(property.GetValue(settings), Join(path, property.Name)))),
            _ => [],
        };

    private static IEnumerable<SettingProblem> AnnotationProblems(object settings, string path)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(settings, new ValidationContext(settings), results, validateAllProperties: true);
        return results.Select(result =>
            new SettingProblem(Join(path, result.MemberNames.FirstOrDefault() ?? ""), result.ErrorMessage ?? "is invalid"));
    }

    // Mirrors how QaaS binds a type: lists and dictionaries by their items, simple values by conversion, and other
    // types of the app by their settable properties.
    private static Shape ShapeOf(Type type)
    {
        if (type == typeof(object) || type.IsInterface || type.IsAbstract) return Shape.Anything;
        if (typeof(IDictionary).IsAssignableFrom(type)) return Shape.Dictionary;
        if (typeof(IList).IsAssignableFrom(type)) return Shape.List;
        var isSystemType = type.Namespace is "System" || type.Namespace?.StartsWith("System.") == true;
        return type.IsEnum || isSystemType ? Shape.Value : Shape.Settings;
    }

    // The type a key's value binds to: a list's item, a dictionary's value, or a setting; null when the key is no setting.
    private static Type? TypeOf(Type type, Shape shape, string key) => shape switch
    {
        Shape.List => type.IsArray ? type.GetElementType() : type.GetGenericArguments().FirstOrDefault() ?? typeof(object),
        Shape.Dictionary => type.GetGenericArguments().ElementAtOrDefault(1) ?? typeof(object),
        _ => SettingsOf(type).FirstOrDefault(property => property.Name.Equals(key, StringComparison.OrdinalIgnoreCase))
            ?.PropertyType,
    };

    private static IEnumerable<PropertyInfo> SettingsOf(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanWrite && property.GetIndexParameters().Length == 0);

    // As QaaS converts a value, failing on any exception: Enum.Parse for an enum, Convert.ChangeType otherwise.
    private static bool Converts(string value, Type type)
    {
        var target = Nullable.GetUnderlyingType(type) ?? type;
        try
        {
            _ = target.IsEnum ? Enum.Parse(target, value) : Convert.ChangeType(value, target);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string Describe(Type type)
    {
        var target = Nullable.GetUnderlyingType(type) ?? type;
        if (target.IsEnum) return $"one of {string.Join(", ", Enum.GetNames(target))}";
        return Type.GetTypeCode(target) switch
        {
            TypeCode.Boolean => "true or false",
            >= TypeCode.SByte and <= TypeCode.UInt64 => "a whole number",
            >= TypeCode.Single and <= TypeCode.Decimal => "a number",
            _ => $"a {target.Name}",
        };
    }

    private static string Join(string path, string key) =>
        key.Length == 0 ? path : path.Length == 0 ? key : $"{path}:{key}";
}
