using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Loader;

namespace QaaS.Playwright.Recorder.Login;

/// <summary>
/// Loads a test project's built assembly, and what it references from its own folder, while every assembly the recorder
/// has itself is the recorder's: QaaS.Playwright, Microsoft.Playwright, QaaS's SDK and .NET's own. A flow in it is
/// then a flow of the recorder's QaaS.Playwright, that runs on the recorder's pages, whatever versions the project
/// was built with.
/// </summary>
internal sealed class FlowAssemblyContext : AssemblyLoadContext
{
    // The recorder's own assemblies, and .NET's: what the host loads the recorder with.
    private static readonly Lazy<HashSet<string>> Shared = new(() =>
        [.. ((AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string) ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(Path.GetFileNameWithoutExtension).OfType<string>()]);

    // Each assembly once, by its path: loaded again, its flows would be there twice, which no flow name could tell apart.
    private static readonly ConcurrentDictionary<string, Lazy<Assembly>> Loaded = new(StringComparer.Ordinal);

    private readonly AssemblyDependencyResolver _resolver;
    private readonly string _folder;

    private FlowAssemblyContext(string path) : base($"Flows from {Path.GetFileName(path)}")
    {
        _resolver = new AssemblyDependencyResolver(path);
        _folder = Path.GetDirectoryName(path)!;
    }

    /// <summary>
    /// Loads the assembly at <paramref name="path"/>, so its flows can be found by name; once, so a login flow that runs
    /// again, as before each test of the flow, is the same flow.
    /// </summary>
    /// <exception cref="BadImageFormatException">It is not a .NET assembly.</exception>
    /// <exception cref="FileLoadException">It, or an assembly it needs, could not be loaded.</exception>
    public static Assembly Load(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var loaded = Loaded.GetOrAdd(fullPath, _ => new Lazy<Assembly>(() =>
            new FlowAssemblyContext(fullPath).LoadFromAssemblyName(AssemblyName.GetAssemblyName(fullPath))));
        try
        {
            return loaded.Value;
        }
        catch
        {
            // Not loaded: the person may build the project and try again.
            Loaded.TryRemove(KeyValuePair.Create(fullPath, loaded));
            throw;
        }
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name is not { } name) return null;
        // By name alone, so a project built with another version gets the recorder's.
        if (Shared.Value.Contains(name)) return Default.LoadFromAssemblyName(new AssemblyName(name));
        var path = _resolver.ResolveAssemblyToPath(assemblyName) ?? Path.Combine(_folder, $"{name}.dll");
        return File.Exists(path) ? LoadFromAssemblyPath(path) : null;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName) =>
        _resolver.ResolveUnmanagedDllToPath(unmanagedDllName) is { } path ? LoadUnmanagedDllFromPath(path) : IntPtr.Zero;
}
