using System.Collections.Concurrent;
using System.Reflection;

namespace QaaS.Probes.Playwright.Engine;

/// <summary>
/// Finds and instantiates IPlaywrightFlow implementations by class name across all
/// loaded assemblies. Results are cached for subsequent lookups.
/// </summary>
public static class FlowDiscovery
{
    private static readonly ConcurrentDictionary<string, Type> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static IPlaywrightFlow Resolve(string name)
    {
        if (!Cache.TryGetValue(name, out var type))
        {
            type = FindType(name)
                ?? throw new InvalidOperationException(
                    $"Flow '{name}' not found. Make sure a class named '{name}' " +
                    "implements IPlaywrightFlow and is referenced by this project.");
            Cache[name] = type;
        }

        return (IPlaywrightFlow)(Activator.CreateInstance(type)
            ?? throw new InvalidOperationException(
                $"Could not instantiate '{name}'. It needs a public parameterless constructor."));
    }

    private static Type? FindType(string name)
    {
        var flowInterface = typeof(IPlaywrightFlow);
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            foreach (var type in SafeGetTypes(asm))
            {
                if (type is null || type.IsAbstract || type.IsInterface) continue;
                if (!flowInterface.IsAssignableFrom(type)) continue;
                if (type.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return type;
            }
        }
        return null;
    }

    private static IEnumerable<Type?> SafeGetTypes(Assembly asm)
    {
        try { return asm.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types; }
        catch (FileNotFoundException)          { return []; }
        catch (TypeLoadException)              { return []; }
    }
}
