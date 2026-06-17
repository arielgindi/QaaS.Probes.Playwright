using System.Collections.Concurrent;
using System.Reflection;

namespace QaaS.Probes.Playwright.Engine;

/// <summary>
/// Finds and instantiates <see cref="IPlaywrightFlow"/> implementations by class name across the loaded
/// assemblies, caching each resolved type. A name that matches more than one type is rejected as ambiguous, so
/// resolution is deterministic regardless of assembly load order.
/// </summary>
public static class FlowDiscovery
{
    private static readonly ConcurrentDictionary<string, Type> TypeCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Resolves and constructs the flow with the given class name.</summary>
    /// <exception cref="InvalidOperationException">No flow, an ambiguous flow, or a non-constructible flow matched.</exception>
    public static IPlaywrightFlow Resolve(string name)
    {
        var flowType = TypeCache.GetOrAdd(name, FindUniqueFlowType);
        return Instantiate(name, flowType);
    }

    private static Type FindUniqueFlowType(string name)
    {
        var matches = FindMatchingFlowTypes(name).ToList();
        return matches.Count switch
        {
            0 => throw new InvalidOperationException(
                $"Flow '{name}' not found. Ensure a class named '{name}' implements IPlaywrightFlow and its " +
                "assembly is referenced by this project."),
            1 => matches[0],
            _ => throw new InvalidOperationException(
                $"Flow name '{name}' is ambiguous; it matches multiple types: " +
                $"{string.Join(", ", matches.Select(type => type.FullName))}. Rename one of them so the name is unique."),
        };
    }

    private static IEnumerable<Type> FindMatchingFlowTypes(string name)
    {
        var flowInterface = typeof(IPlaywrightFlow);
        return AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(SafeGetTypes)
            .Where(type => type is { IsAbstract: false, IsInterface: false }
                           && flowInterface.IsAssignableFrom(type)
                           && type.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    private static IPlaywrightFlow Instantiate(string name, Type flowType)
    {
        if (flowType.GetConstructor(Type.EmptyTypes) is null)
            throw new InvalidOperationException(
                $"Flow '{name}' ({flowType.FullName}) needs a public parameterless constructor.");

        try
        {
            return (IPlaywrightFlow)Activator.CreateInstance(flowType)!;
        }
        catch (Exception constructionFailure)
        {
            throw new InvalidOperationException(
                $"Could not construct flow '{name}' ({flowType.FullName}): {constructionFailure.Message}",
                constructionFailure);
        }
    }

    private static IEnumerable<Type> SafeGetTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException loadFailure)
        {
            // Some types in the assembly failed to load; fall back to the ones that did.
            return loadFailure.Types.OfType<Type>();
        }
        catch (Exception failure) when (failure is FileNotFoundException or TypeLoadException)
        {
            return [];
        }
    }
}
