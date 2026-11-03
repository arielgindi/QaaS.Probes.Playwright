using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;

namespace QaaS.Playwright.Recorder.Login;

/// <summary>
/// Loads a test project's built assembly, and what it references from its own folder, while every assembly the recorder
/// has itself is the recorder's: QaaS.Playwright, Microsoft.Playwright, QaaS's SDK and .NET's own. A flow in it is
/// then a flow of the recorder's QaaS.Playwright, that runs on the recorder's pages, whatever versions the project
/// was built with. The flows of the folder's other assemblies load with it, as a shared package's do: the probe finds
/// a setup flow in any assembly of the test, not only in the project's own.
/// </summary>
internal sealed class FlowAssemblyContext : AssemblyLoadContext
{
    private const string FlowsAssembly = "QaaS.Playwright";

    // The recorder's own assemblies, and .NET's: what the host loads the recorder with.
    private static readonly Lazy<HashSet<string>> Shared = new(() =>
        [.. ((AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string) ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(Path.GetFileNameWithoutExtension).OfType<string>()]);

    // A context for each assembly loaded by its path, which resolves what it references from its folder.
    private static readonly ConcurrentDictionary<string, FlowAssemblyContext> Contexts = new(StringComparer.Ordinal);

    // Each file once, whichever context loads it first: loaded twice, its flows would be there twice, which no flow
    // name could tell apart.
    private static readonly ConcurrentDictionary<string, Lazy<Assembly>> Files = new(StringComparer.Ordinal);

    private readonly AssemblyDependencyResolver _resolver;
    private readonly string _folder;

    private FlowAssemblyContext(string path) : base($"Flows from {Path.GetFileName(path)}")
    {
        _resolver = new AssemblyDependencyResolver(path);
        _folder = Path.GetDirectoryName(path)!;
    }

    /// <summary>
    /// Loads the assembly at <paramref name="path"/>, and the flows of the assemblies beside it, so they can be found by
    /// name; once, so a login flow that runs again, as before each test of the flow, is the same flow.
    /// </summary>
    /// <exception cref="BadImageFormatException">It is not a .NET assembly.</exception>
    /// <exception cref="FileLoadException">It, or an assembly it needs, could not be loaded.</exception>
    public static Assembly Load(string path) => LoadWithFlows(path)[0];

    /// <summary>
    /// The assembly at <paramref name="path"/>, then those of its folder that can have flows (the ones that reference
    /// QaaS.Playwright), all loaded. One of them that cannot be loaded is left out.
    /// </summary>
    /// <exception cref="BadImageFormatException">The assembly at <paramref name="path"/> is not a .NET assembly.</exception>
    /// <exception cref="FileLoadException">It, or an assembly it needs, could not be loaded.</exception>
    public static IReadOnlyList<Assembly> LoadWithFlows(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var context = Contexts.GetOrAdd(fullPath, root => new FlowAssemblyContext(root));
        List<Assembly> loaded = [context.LoadFromAssemblyName(AssemblyName.GetAssemblyName(fullPath))];
        foreach (var beside in Directory.EnumerateFiles(context._folder, "*.dll"))
        {
            var name = Path.GetFileNameWithoutExtension(beside);
            if (string.Equals(beside, fullPath, StringComparison.Ordinal) || Shared.Value.Contains(name) || !HasFlows(beside))
                continue;
            try
            {
                loaded.Add(context.LoadFromAssemblyName(new AssemblyName(name)));
            }
            catch (Exception failure) when (failure is FileLoadException or FileNotFoundException or BadImageFormatException)
            {
                // Its flows cannot run either; the one asked for may be elsewhere.
            }
        }

        return loaded;
    }

    /// <summary>The flows of <paramref name="assemblies"/>, as the probe finds them by name.</summary>
    public static IEnumerable<Type> FlowsIn(IEnumerable<Assembly> assemblies) => assemblies
        .SelectMany(TypesOf)
        .Where(type => type is { IsAbstract: false, IsGenericTypeDefinition: false } && typeof(IPlaywrightFlow).IsAssignableFrom(type));

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name is not { } name) return null;
        // By name alone, so a project built with another version gets the recorder's.
        if (Shared.Value.Contains(name)) return Default.LoadFromAssemblyName(new AssemblyName(name));
        var path = _resolver.ResolveAssemblyToPath(assemblyName) ?? Path.Combine(_folder, $"{name}.dll");
        if (!File.Exists(path)) return null;
        var file = Files.GetOrAdd(Path.GetFullPath(path), full => new Lazy<Assembly>(() => LoadFromAssemblyPath(full)));
        try
        {
            return file.Value;
        }
        catch
        {
            // Not loaded: the person may build the project and try again.
            Files.TryRemove(KeyValuePair.Create(Path.GetFullPath(path), file));
            throw;
        }
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName) =>
        _resolver.ResolveUnmanagedDllToPath(unmanagedDllName) is { } path ? LoadUnmanagedDllFromPath(path) : IntPtr.Zero;

    // Read without loading it, so a folder of a few hundred assemblies is looked through in a moment.
    private static bool HasFlows(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var image = new PEReader(stream);
            if (!image.HasMetadata) return false;
            var metadata = image.GetMetadataReader();
            return metadata.IsAssembly && metadata.AssemblyReferences
                .Any(reference => metadata.StringComparer.Equals(metadata.GetAssemblyReference(reference).Name, FlowsAssembly));
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or BadImageFormatException
                                            or InvalidOperationException)
        {
            return false;
        }
    }

    // An assembly whose types only partly load still offers the ones that did.
    private static IEnumerable<Type> TypesOf(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException partlyLoaded)
        {
            return partlyLoaded.Types.OfType<Type>();
        }
        catch (Exception failure) when (failure is FileNotFoundException or FileLoadException or TypeLoadException)
        {
            return [];
        }
    }
}
