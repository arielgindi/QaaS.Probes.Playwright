using System.Reflection;

namespace QaaS.Probes.Playwright.Engine;

/// <summary>
/// Finds IPlaywrightFlow implementations by class name across all loaded assemblies.
///
/// This mirrors QaaS's own HookProvider pattern: scan assemblies for types implementing
/// the target interface, match by class name (case-insensitive), instantiate via Activator.
///
/// Results are cached after first scan so repeated lookups are fast.
/// Flow classes can live in any referenced assembly — the probe project, a shared NuGet
/// package, or the test project itself.
/// </summary>
public static class FlowDiscovery
{
    private static readonly Dictionary<string, Type> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Finds and instantiates a flow by class name.
    /// Throws InvalidOperationException with a helpful message if not found.
    /// </summary>
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

        // Scan all loaded assemblies for IPlaywrightFlow implementations.
        // This is the same approach QaaS uses in HookProvider to find probes/generators/assertions.
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
                // Some assemblies (e.g. native interop) can't enumerate types — skip them
            }
        }

        return null;
    }
}
