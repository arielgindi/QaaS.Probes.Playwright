using System.Collections;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Runtime.Loader;

namespace QaaS.Playwright.Recorder.TestRun;

/// <summary>
/// A test run's build of the flow, loaded in a context of its own that unloads after the run, so each run starts afresh
/// and leaves nothing behind. Every assembly it uses is the recorder's own: QaaS.Playwright, Playwright, QaaS's SDK and
/// .NET's, so its flow is a flow of the recorder's QaaS.Playwright, and runs on the recorder's Chrome.
/// </summary>
internal sealed class TestFlowContext : AssemblyLoadContext
{
    private Assembly? _assembly;

    private TestFlowContext() : base("QaaS recorder test run", isCollectible: true)
    {
    }

    /// <summary>Loads the build from memory, so its files stay free, with its symbols, which give a failure its line.</summary>
    public static TestFlowContext Load(BuiltFlow built)
    {
        var context = new TestFlowContext();
        try
        {
            using var assembly = new MemoryStream(File.ReadAllBytes(built.Assembly));
            using var symbols = new MemoryStream(File.ReadAllBytes(built.Symbols));
            context._assembly = context.LoadFromStream(assembly, symbols);
            return context;
        }
        catch
        {
            context.Unload();
            throw;
        }
    }

    /// <summary>A new flow of the class named <paramref name="className"/>.</summary>
    /// <exception cref="InvalidOperationException">The build has no such flow.</exception>
    public IPlaywrightFlow Create(string className)
    {
        var type = Assembly.GetTypes().FirstOrDefault(type => type.Name == className && typeof(IPlaywrightFlow).IsAssignableFrom(type))
                   ?? throw new InvalidOperationException($"The test build has no flow named {className}.");
        return (IPlaywrightFlow)Activator.CreateInstance(type)!;
    }

    /// <summary>
    /// Calls <paramref name="reached"/> with each action's number as it begins, and the action waits for what it returns;
    /// null no longer calls anything.
    /// </summary>
    public void OnAction(Func<int, Task>? reached) =>
        Assembly.GetType(FlowBuild.ProbeType)?.GetField("Reached", BindingFlags.Public | BindingFlags.Static)?.SetValue(null, reached);

    /// <summary>Unloads the build, once nothing of it is used any more.</summary>
    public void Release()
    {
        if (_assembly is not { } assembly) return;
        OnAction(null);
        ForgetCachedTypes(assembly);
        _assembly = null;
        Unload();
    }

    // Everything but the build itself comes from the recorder.
    protected override Assembly? Load(AssemblyName assemblyName) => null;

    private Assembly Assembly => _assembly ?? throw new InvalidOperationException("The test build was released.");

    // Validating the flow's settings has TypeDescriptor and DataAnnotations keep what they learn of its types for as long
    // as the process runs, which would keep the build loaded. Their tables are private, so each is found by what it is, a
    // table keyed by types or members, and the build's are taken out of it. Best effort: a .NET that keeps them otherwise
    // keeps the build loaded until the recorder ends.
    private static void ForgetCachedTypes(Assembly assembly)
    {
        TypeDescriptor.Refresh(assembly);
        const BindingFlags shared = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        var tables = new List<object?>();
        foreach (var cache in (Type?[])[typeof(TypeDescriptor), typeof(TypeDescriptor).Assembly.GetType("System.ComponentModel.ReflectTypeDescriptionProvider")])
            tables.AddRange(cache?.GetFields(shared).Select(field => field.GetValue(null)) ?? []);
        // The providers that describe types, as TypeDescriptor's own tables hold them, keep tables of their own.
        foreach (var table in tables.OfType<IDictionary>().ToList())
            tables.AddRange(Values(table).OfType<TypeDescriptionProvider>().SelectMany(provider => ProviderTables(provider, depth: 0)));
        tables.AddRange(ProviderTables(TypeDescriptor.GetProvider(typeof(object)), depth: 0));
        var store = typeof(Validator).Assembly.GetType("System.ComponentModel.DataAnnotations.ValidationAttributeStore");
        var instance = store?.GetProperty("Instance", shared)?.GetValue(null);
        if (instance is not null)
            tables.AddRange(store!.GetFields(BindingFlags.Instance | BindingFlags.NonPublic).Select(field => field.GetValue(instance)));

        foreach (var table in tables.OfType<IDictionary>().Distinct(ReferenceEqualityComparer.Instance).Cast<IDictionary>())
        {
            try
            {
                // A Hashtable is written to under its lock; a concurrent dictionary needs none.
                if (table is Hashtable hashtable)
                    lock (hashtable.SyncRoot) Forget(hashtable);
                else
                    Forget(table);
            }
            catch (Exception failure) when (failure is InvalidOperationException or NotSupportedException)
            {
                // Read-only, or changed meanwhile: it keeps what it has.
            }
        }
    }

    private static List<object?> Values(IDictionary table)
    {
        try
        {
            if (table is not Hashtable hashtable) return [.. table.Values.Cast<object?>()];
            lock (hashtable.SyncRoot) return [.. hashtable.Values.Cast<object?>()];
        }
        catch (InvalidOperationException)
        {
            return [];
        }
    }

    // The tables of a provider of type descriptions, and of the providers it chains to.
    private static IEnumerable<object?> ProviderTables(TypeDescriptionProvider? provider, int depth)
    {
        if (provider is null || depth > 3) yield break;
        foreach (var field in provider.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            var value = field.GetValue(provider);
            if (value is IDictionary) yield return value;
            else if (value is TypeDescriptionProvider inner)
                foreach (var table in ProviderTables(inner, depth + 1)) yield return table;
        }
    }

    // Every key that is a type or a member of a collectible assembly, as only test builds are: a type of the build, one
    // of its members, e.g. a setting, or a type made of one, e.g. IEquatable of the settings record. A cache learns
    // again what it forgot.
    private static void Forget(IDictionary table)
    {
        foreach (var member in table.Keys.OfType<MemberInfo>().Where(member => member.IsCollectible).ToList()) table.Remove(member);
    }
}
