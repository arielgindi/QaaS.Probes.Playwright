using System.Collections.Concurrent;
using System.Reflection;

namespace QaaS.Playwright.Flows;

/// <summary>
/// Creates flows by class name, case-insensitively, from the <see cref="IPlaywrightFlow"/> types of every loaded
/// assembly. A name that matches two types is an error, so the result never depends on assembly load order.
/// </summary>
public static class FlowDiscovery
{
    private static readonly ConcurrentDictionary<string, Type> TypesByName = new(StringComparer.OrdinalIgnoreCase);

    /// <exception cref="InvalidOperationException">Not exactly one flow has that name, or it cannot be created.</exception>
    public static IPlaywrightFlow Resolve(string name)
    {
        var type = TypesByName.GetOrAdd(name, FindType);
        if (type.GetConstructor(Type.EmptyTypes) is null)
            throw new InvalidOperationException(
                $"Flow '{name}' ({type.FullName}) needs a public parameterless constructor.");

        try
        {
            return (IPlaywrightFlow)Activator.CreateInstance(type)!;
        }
        catch (TargetInvocationException failure)
        {
            throw new InvalidOperationException(
                $"Could not create flow '{name}' ({type.FullName}): {failure.InnerException?.Message}",
                failure.InnerException);
        }
    }

    private static Type FindType(string name)
    {
        var matches = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(LoadableTypes)
            .Where(type => type is { IsAbstract: false, IsGenericTypeDefinition: false }
                           && typeof(IPlaywrightFlow).IsAssignableFrom(type)
                           && type.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return matches switch
        {
            [var type] => type,
            [] => throw new InvalidOperationException(
                $"Flow '{name}' not found. Check that a class named '{name}' implements IPlaywrightFlow and that its " +
                "assembly is referenced."),
            _ => throw new InvalidOperationException(
                $"Flow name '{name}' is ambiguous: {string.Join(", ", matches.Select(type => type.FullName))}. Rename one."),
        };
    }

    // An assembly whose types only partly load still offers the ones that did.
    private static IEnumerable<Type> LoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException partlyLoaded)
        {
            return partlyLoaded.Types.OfType<Type>();
        }
        catch (Exception failure) when (failure is FileNotFoundException or TypeLoadException)
        {
            return [];
        }
    }
}
