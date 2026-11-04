using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;
using System.Text.Json;
using QaaS.Playwright.Files;
using QaaS.Playwright.Flows;

namespace QaaS.Playwright.Recorder.Login;

/// <summary>A DLL looked through for flows: where it is, why it was looked at, and what it gave.</summary>
/// <param name="Path">The file.</param>
/// <param name="Why">"the build", "beside it", or "a dependency of it" (from its .deps.json).</param>
/// <param name="BuiltAgainst">The QaaS.Playwright it was built against; null when it references none, so has no flows.</param>
/// <param name="NotLoaded">Why it could not be loaded; null when it was, or was not loaded, having no flows.</param>
internal sealed record ScannedDll(string Path, string Why, Version? BuiltAgainst, string? NotLoaded);

/// <summary>The assemblies loaded from a build, the build's first, and every DLL looked through.</summary>
internal sealed record FlowAssemblies(IReadOnlyList<Assembly> Loaded, IReadOnlyList<ScannedDll> Scanned);

/// <summary>
/// Loads a test project's built assembly with what it uses, as the test run has it: its dependency closure, from its
/// <c>.deps.json</c> (through <see cref="AssemblyDependencyResolver"/>, the output folder and the NuGet package folders, as
/// a library's packages are not copied beside it), and the other DLLs of its folder. Every assembly the recorder has
/// itself is the recorder's: QaaS.Playwright, Microsoft.Playwright, QaaS's SDK and .NET's own. A flow in it is then a
/// flow of the recorder's QaaS.Playwright, that runs on the recorder's pages, whatever versions the project was built
/// with. The flows of all of them load with it, as a shared package's do: the probe finds a setup flow in any assembly
/// of the test, not only in the project's own.
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

    // The package folders restores wrote to, besides NuGet's own: those of the test projects the recorder looked at.
    private static readonly ConcurrentDictionary<string, bool> MorePackageFolders = new(StringComparer.Ordinal);

    private readonly AssemblyDependencyResolver _resolver;
    private readonly string _folder;
    private readonly Dictionary<string, string> _closure;

    private FlowAssemblyContext(string path) : base($"Flows from {Path.GetFileName(path)}")
    {
        _resolver = new AssemblyDependencyResolver(path);
        _folder = Path.GetDirectoryName(path)!;
        _closure = ClosureOf(path);
    }

    /// <summary>The package folders of a project's restore, from its <c>project.assets.json</c>, to find its packages in.</summary>
    public static void AddPackageFoldersOf(string assetsFile)
    {
        try
        {
            using var assets = JsonDocument.Parse(File.ReadAllText(assetsFile));
            if (assets.RootElement.TryGetProperty("packageFolders", out var folders) && folders.ValueKind == JsonValueKind.Object)
                foreach (var folder in folders.EnumerateObject())
                    MorePackageFolders.TryAdd(folder.Name, true);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or JsonException)
        {
            // NuGet's own folder is looked in still.
        }
    }

    /// <summary>
    /// Loads the assembly at <paramref name="path"/>, and the flows of the assemblies it uses, so they can be found by
    /// name; once, so a login flow that runs again, as before each test of the flow, is the same flow.
    /// </summary>
    /// <exception cref="BadImageFormatException">It is not a .NET assembly.</exception>
    /// <exception cref="FileLoadException">It, or an assembly it needs, could not be loaded.</exception>
    public static Assembly Load(string path) => LoadWithFlows(path).Loaded[0];

    /// <summary>
    /// The assembly at <paramref name="path"/>, then those of its dependency closure and of its folder that can have flows
    /// (the ones that reference QaaS.Playwright), all loaded, and every DLL looked through. One of them that cannot be
    /// loaded is left out, and why is said.
    /// </summary>
    /// <exception cref="BadImageFormatException">The assembly at <paramref name="path"/> is not a .NET assembly.</exception>
    /// <exception cref="FileLoadException">It, or an assembly it needs, could not be loaded.</exception>
    public static FlowAssemblies LoadWithFlows(string path)
    {
        // From a copy of the build, so Windows locks no file of it, and the person can build it again while recording;
        // what was looked through is said where it is in the build.
        var original = Path.GetFullPath(path);
        var fullPath = ShadowOf(original);
        string Shown(string file) => file.StartsWith(Path.GetDirectoryName(fullPath)!, StringComparison.Ordinal)
            ? Path.Combine(Path.GetDirectoryName(original)!, Path.GetRelativePath(Path.GetDirectoryName(fullPath)!, file))
            : file;
        var context = Contexts.GetOrAdd(fullPath, root => new FlowAssemblyContext(root));
        var root = context.LoadFromAssemblyName(AssemblyName.GetAssemblyName(fullPath));
        List<Assembly> loaded = [root];
        List<ScannedDll> scanned = [new(original, "the build", BuiltAgainst(fullPath), null)];
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Path.GetFileNameWithoutExtension(fullPath) };
        var candidates = context._closure.Select(entry => (Name: entry.Key, Path: entry.Value, Why: "a dependency of it"))
            .Concat(Directory.EnumerateFiles(context._folder, "*.dll")
                .Select(file => (Name: Path.GetFileNameWithoutExtension(file), Path: file, Why: "beside it")));
        foreach (var (name, file, why) in candidates)
        {
            if (Shared.Value.Contains(name) || !seen.Add(name)) continue;
            var builtAgainst = BuiltAgainst(file);
            if (builtAgainst is null)
            {
                scanned.Add(new ScannedDll(Shown(file), why, null, null));
                continue;
            }

            try
            {
                loaded.Add(context.LoadFromAssemblyName(new AssemblyName(name)));
                scanned.Add(new ScannedDll(Shown(file), why, builtAgainst, null));
            }
            catch (Exception failure) when (failure is FileLoadException or FileNotFoundException or BadImageFormatException)
            {
                // Its flows cannot run either; the one asked for may be elsewhere.
                scanned.Add(new ScannedDll(Shown(file), why, builtAgainst, failure.Message));
            }
        }

        return new FlowAssemblies(loaded, scanned);
    }

    // Each copy's folder, and the build's it was copied from.
    private static readonly ConcurrentDictionary<string, string> Originals = new(StringComparer.Ordinal);

    /// <summary>Where <paramref name="location"/>, an assembly loaded from a build's copy, is in the build itself.</summary>
    public static string Original(string location)
    {
        var folder = Path.GetDirectoryName(location) ?? "";
        return Originals.TryGetValue(folder, out var original) ? Path.Combine(original, Path.GetFileName(location)) : location;
    }

    // The build's copy; the build itself when it cannot be copied (no room), as before copies.
    private static string ShadowOf(string path)
    {
        try
        {
            if (!File.Exists(path)) return path;
            var copy = ShadowCopy.Of(path, FileStore.Machine);
            Originals[Path.GetDirectoryName(copy)!] = Path.GetDirectoryName(path)!;
            return copy;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return path;
        }
    }

    /// <summary>The flows of <paramref name="assemblies"/>, as the probe finds them by name.</summary>
    public static IEnumerable<Type> FlowsIn(IEnumerable<Assembly> assemblies) => FlowDiscovery.FlowsIn(assemblies);

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name is not { } name) return null;
        // By name alone, so a project built with another version gets the recorder's.
        if (Shared.Value.Contains(name)) return Default.LoadFromAssemblyName(new AssemblyName(name));
        var path = _resolver.ResolveAssemblyToPath(assemblyName)
                   ?? _closure.GetValueOrDefault(name)
                   ?? Path.Combine(_folder, $"{name}.dll");
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

    // The runtime assemblies the .deps.json files of the build's folder list, its own first, each by its name, where it
    // is: in the folder, else in a NuGet package folder, as a library's packages stay there.
    private static Dictionary<string, string> ClosureOf(string path)
    {
        var closure = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var folder = Path.GetDirectoryName(path)!;
        var own = Path.ChangeExtension(path, ".deps.json");
        IEnumerable<string> depsFiles;
        try
        {
            depsFiles = [.. (File.Exists(own) ? [own] : Array.Empty<string>())
                .Concat(Directory.EnumerateFiles(folder, "*.deps.json").Where(file => file != own))];
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return closure;
        }

        var packageFolders = PackageFolders();
        foreach (var deps in depsFiles)
        {
            try
            {
                using var json = JsonDocument.Parse(File.ReadAllText(deps));
                var root = json.RootElement;
                if (!root.TryGetProperty("targets", out var targets) || !root.TryGetProperty("libraries", out var libraries)) continue;
                var target = root.TryGetProperty("runtimeTarget", out var runtimeTarget) && runtimeTarget.TryGetProperty("name", out var name)
                    ? name.GetString()
                    : null;
                if (target is null || !targets.TryGetProperty(target, out var assets)) continue;
                foreach (var library in assets.EnumerateObject())
                {
                    if (!library.Value.TryGetProperty("runtime", out var runtime)) continue;
                    var package = libraries.TryGetProperty(library.Name, out var about)
                                  && about.TryGetProperty("type", out var type) && type.GetString() == "package"
                                  && about.TryGetProperty("path", out var packagePath)
                        ? packagePath.GetString()
                        : null;
                    foreach (var asset in runtime.EnumerateObject())
                    {
                        if (!asset.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) continue;
                        var assembly = Path.GetFileNameWithoutExtension(asset.Name);
                        if (closure.ContainsKey(assembly)) continue;
                        var local = Path.Combine(folder, Path.GetFileName(asset.Name));
                        var found = File.Exists(local) ? local
                            : package is null ? null
                            : packageFolders.Select(packages => Path.Combine(packages, package, asset.Name.Replace('/', Path.DirectorySeparatorChar)))
                                .FirstOrDefault(File.Exists);
                        if (found is not null) closure[assembly] = found;
                    }
                }
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or JsonException
                                                or InvalidOperationException)
            {
                // A .deps.json that cannot be read gives nothing: the folder's DLLs are looked through still.
            }
        }

        return closure;
    }

    // NuGet's own package folder, as NUGET_PACKAGES or the default sets it, and those of the projects looked at.
    private static List<string> PackageFolders()
    {
        var folders = new List<string>(MorePackageFolders.Keys);
        if (Environment.GetEnvironmentVariable("NUGET_PACKAGES") is { Length: > 0 } set) folders.Add(set);
        folders.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages"));
        return [.. folders.Distinct(StringComparer.Ordinal).Where(Directory.Exists)];
    }

    // The QaaS.Playwright version it references, read without loading it, so a folder of a few hundred assemblies is
    // looked through in a moment; null when it references none, or is not a .NET assembly.
    private static Version? BuiltAgainst(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var image = new PEReader(stream);
            if (!image.HasMetadata) return null;
            var metadata = image.GetMetadataReader();
            if (!metadata.IsAssembly) return null;
            foreach (var handle in metadata.AssemblyReferences)
            {
                var reference = metadata.GetAssemblyReference(handle);
                if (metadata.StringComparer.Equals(reference.Name, FlowsAssembly)) return reference.Version;
            }

            return null;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or BadImageFormatException
                                            or InvalidOperationException)
        {
            return null;
        }
    }
}
