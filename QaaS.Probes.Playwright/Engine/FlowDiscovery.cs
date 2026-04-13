using System.Reflection;

namespace QaaS.Probes.Playwright.Engine;

/// <summary>
/// Finds IPlaywrightFlow implementations by class name across all loaded assemblies.
/// Same discovery pattern as QaaS's HookProvider — scan assemblies, match by name.
/// Results are cached after first scan.
/// </summary>
public static class FlowDiscovery
{
    private static readonly Dictionary<string, Type> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static IPlaywrightFlow Resolve(string name)
    {
        var type = FindType(name)
                   ?? throw new InvalidOperationException(
                       $"Flow '{name}' not found. Make sure a class named '{name}' " +
                       "implements IPlaywrightFlow and is referenced by this project.");

        return (IPlaywrightFlow)(Activator.CreateInstance(type)
                                 ?? throw new InvalidOperationException($"Could not instantiate '{name}'."));
    }

    private static Type? FindType(string name)
    {
        if (Cache.TryGetValue(name, out var cached))
            return cached;

        var flowInterface = typeof(IPlaywrightFlow);
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                foreach (var type in asm.GetTypes())
                {
                    if (!flowInterface.IsAssignableFrom(type) || type.IsAbstract || type.IsInterface)
                        continue;

                    Cache[type.Name] = type;

                    if (type.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                        return type;
                }
            }
            catch (ReflectionTypeLoadException)
            {
                // Some assemblies can't be fully loaded — skip
            }
        }

        return null;
    }
}
