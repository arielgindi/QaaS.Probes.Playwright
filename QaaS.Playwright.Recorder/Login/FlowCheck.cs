using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;
using QaaS.Playwright.Flows;

namespace QaaS.Playwright.Recorder.Login;

/// <summary>
/// Whether a login flow can run in the recorder, which gives it its own QaaS.Playwright: a flow built against another
/// version may use what that version has and the recorder's has not, e.g. <c>BrowserDefaults.RemoteUrl</c>. Each method
/// of the flow, of its nested types (its async state machines, its lambdas) and of its base types in its DLL is bound
/// as the runtime binds it when it runs, so what is gone is found before the browser opens, and said plainly.
/// </summary>
internal static partial class FlowCheck
{
    private const string FlowsAssembly = "QaaS.Playwright";

    // Each IL opcode by its value, to step over their operands.
    private static readonly Lazy<Dictionary<short, OpCode>> OpCodesByValue = new(() => typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(field => (OpCode)field.GetValue(null)!)
        .ToDictionary(code => code.Value));

    /// <summary>The recorder's own QaaS.Playwright version, e.g. 2.0.3.</summary>
    public static string RecorderVersion =>
        typeof(IPlaywrightFlow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            .Split('+')[0] ?? typeof(IPlaywrightFlow).Assembly.GetName().Version?.ToString(3) ?? "unknown";

    /// <summary>Why <paramref name="flow"/> cannot run in the recorder, said plainly; null when it can.</summary>
    public static string? CannotRun(Type flow)
    {
        string? gone;
        try
        {
            gone = MissingIn(flow).FirstOrDefault();
        }
        catch (Exception failure) when (IsGone(failure))
        {
            gone = Gone(failure);
        }

        return gone is null ? null : Plainly(flow.Name, flow.Assembly, gone);
    }

    /// <summary>
    /// Why the flow named <paramref name="name"/> in <paramref name="assembly"/> did not load at all, said plainly; null
    /// when it did, or the assembly has no type of that name.
    /// </summary>
    public static string? CannotLoad(string name, Assembly assembly)
    {
        try
        {
            assembly.GetTypes();
            return null;
        }
        catch (ReflectionTypeLoadException partlyLoaded)
        {
            var loaded = partlyLoaded.Types.OfType<Type>().Select(type => type.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (loaded.Contains(name) || !TypeNamesIn(assembly.Location).Contains(name)) return null;
            var why = partlyLoaded.LoaderExceptions.OfType<Exception>().Select(Gone).FirstOrDefault() ?? "its types did not load";
            return Plainly(name, assembly, why);
        }
        catch (Exception failure) when (IsGone(failure) || failure is FileNotFoundException or FileLoadException)
        {
            return TypeNamesIn(assembly.Location).Contains(name) ? Plainly(name, assembly, Gone(failure)) : null;
        }
    }

    /// <summary>
    /// <paramref name="failure"/> of a flow that ran, said plainly when it is one of a flow built against another
    /// QaaS.Playwright, or of what its DLL needs and the recorder has not; null for any other failure.
    /// </summary>
    public static string? Explain(Exception failure, string flow)
    {
        var cause = failure.GetBaseException();
        if (!IsGone(cause) && !IsGone(failure)) return null;
        var type = FlowDiscovery.Named(flow, AppDomain.CurrentDomain.GetAssemblies()) is [var one] ? one : null;
        return type is null ? null : Plainly(type.Name, type.Assembly, Gone(IsGone(cause) ? cause : failure));
    }

    /// <summary>The QaaS.Playwright version <paramref name="assembly"/> was built against; null when it references none.</summary>
    public static Version? BuiltAgainst(Assembly assembly) =>
        assembly.GetReferencedAssemblies().FirstOrDefault(reference => reference.Name == FlowsAssembly)?.Version;

    /// <summary>
    /// "LoginFlow (in Acme.Shared.dll) was built against QaaS.Playwright 1.15.1, and the recorder runs 2.0.3: it can't
    /// load it (BrowserDefaults.RemoteUrl is gone). ...", with what to do.
    /// </summary>
    private static string Plainly(string flow, Assembly assembly, string gone)
    {
        var file = string.IsNullOrEmpty(assembly.Location) ? $"{assembly.GetName().Name}.dll" : Path.GetFileName(assembly.Location);
        var runs = typeof(IPlaywrightFlow).Assembly.GetName().Version;
        var built = BuiltAgainst(assembly);
        var series = runs is null ? RecorderVersion : $"{runs.Major}.{runs.Minor}.x";
        return built is not null && runs is not null && (built.Major, built.Minor, built.Build) != (runs.Major, runs.Minor, runs.Build)
            ? $"{flow} (in {file}) was built against QaaS.Playwright {built.ToString(3)}, and the recorder runs {RecorderVersion}: " +
              $"it can't load it ({gone}). Update that package to QaaS.Playwright {series}, then build your tests again. " +
              "Until then, record with --no-login and log in by hand."
            : $"{flow} (in {file}) can't run in the recorder ({gone}): it needs something the recorder's packages do not " +
              $"have. Build it again against QaaS.Playwright {RecorderVersion}, or record with --no-login and log in by hand.";
    }

    private static bool IsGone(Exception failure) => failure is MissingMemberException or TypeLoadException or BadImageFormatException;

    // "BrowserDefaults.RemoteUrl is gone", from the runtime's "Field not found: 'QaaS.Playwright.Browser.BrowserDefaults.RemoteUrl'."
    // or "Method not found: 'System.String QaaS.Playwright.Browser.BrowserDefaults.get_RemoteUrl()'."
    private static string Gone(Exception failure)
    {
        var quoted = Quoted().Match(failure.Message) is { Success: true } match ? match.Groups[1].Value : null;
        switch (failure)
        {
            case MissingMemberException when quoted is not null:
                var member = quoted.Split('(')[0];
                member = member[(member.LastIndexOf(' ') + 1)..];
                return $"{Short(Accessor().Replace(member, "."), member: true)} is gone";
            case TypeLoadException { Message: var message } when message.Contains("does not have an implementation", StringComparison.Ordinal):
                return Terminal.Clean(message.TrimEnd('.'));
            case TypeLoadException loading when (quoted ?? loading.TypeName) is { Length: > 0 } type:
                return $"{Short(type, member: false)} is gone";
            default:
                return Terminal.Clean(failure.Message.TrimEnd('.'));
        }
    }

    // "QaaS.Playwright.Browser.BrowserDefaults.RemoteUrl" is "BrowserDefaults.RemoteUrl"; a type, its name alone.
    private static string Short(string name, bool member)
    {
        var parts = Arity().Replace(name, "").Split('.', '+');
        return string.Join('.', member && parts.Length >= 2 ? parts[^2..] : parts[^1..]);
    }

    // Every member the flow's methods use that does not bind: the flow, its nested types, and its base types in its DLL.
    private static IEnumerable<string> MissingIn(Type flow)
    {
        var types = new List<Type>();
        for (var type = flow; type is not null && type.Assembly == flow.Assembly; type = type.BaseType) AddWithNested(type, types);
        foreach (var type in types)
        foreach (var method in type.GetMethods(Declared).Cast<MethodBase>().Concat(type.GetConstructors(Declared)))
        {
            byte[]? il;
            try
            {
                il = method.GetMethodBody()?.GetILAsByteArray();
            }
            catch (Exception failure) when (IsGone(failure))
            {
                il = null;
            }

            if (il is null) continue;
            var typeArguments = type.IsGenericType ? type.GetGenericArguments() : null;
            var methodArguments = method.IsGenericMethod ? method.GetGenericArguments() : null;
            foreach (var token in TokensIn(il))
            {
                string? gone = null;
                try
                {
                    type.Module.ResolveMember(token, typeArguments, methodArguments);
                }
                catch (Exception failure) when (IsGone(failure) || failure is FileNotFoundException or FileLoadException)
                {
                    gone = Gone(failure);
                }
                catch (ArgumentException)
                {
                    // Not a member the method uses, e.g. a token of a signature.
                }

                if (gone is not null) yield return gone;
            }
        }
    }

    private const BindingFlags Declared = BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static
                                          | BindingFlags.Public | BindingFlags.NonPublic;

    private static void AddWithNested(Type type, List<Type> types)
    {
        types.Add(type);
        foreach (var nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)) AddWithNested(nested, types);
    }

    // The tokens of the fields, methods and types an IL body uses, stepping over every other operand.
    private static IEnumerable<int> TokensIn(byte[] il)
    {
        for (var at = 0; at < il.Length;)
        {
            short value = il[at++];
            if (value == 0xFE && at < il.Length) value = unchecked((short)(0xFE00 | il[at++]));
            if (!OpCodesByValue.Value.TryGetValue(value, out var code)) yield break;
            switch (code.OperandType)
            {
                case OperandType.InlineField or OperandType.InlineMethod or OperandType.InlineType or OperandType.InlineTok:
                    if (at + 4 <= il.Length) yield return BitConverter.ToInt32(il, at);
                    at += 4;
                    break;
                case OperandType.InlineNone:
                    break;
                case OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar:
                    at += 1;
                    break;
                case OperandType.InlineVar:
                    at += 2;
                    break;
                case OperandType.InlineSwitch:
                    at += 4 + 4 * (at + 4 <= il.Length ? BitConverter.ToInt32(il, at) : 0);
                    break;
                case OperandType.InlineI8 or OperandType.InlineR:
                    at += 8;
                    break;
                default:
                    at += 4;
                    break;
            }
        }
    }

    // The names of the types the DLL defines, read without loading it.
    private static HashSet<string> TypeNamesIn(string path)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(path)) return names;
        try
        {
            using var stream = File.OpenRead(path);
            using var image = new PEReader(stream);
            var metadata = image.GetMetadataReader();
            foreach (var handle in metadata.TypeDefinitions)
                names.Add(metadata.GetString(metadata.GetTypeDefinition(handle).Name));
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or BadImageFormatException
                                            or InvalidOperationException)
        {
            // None to tell.
        }

        return names;
    }

    [GeneratedRegex("'([^']+)'")]
    private static partial Regex Quoted();

    [GeneratedRegex(@"\.(get|set|add|remove)_")]
    private static partial Regex Accessor();

    [GeneratedRegex(@"`\d+")]
    private static partial Regex Arity();
}
