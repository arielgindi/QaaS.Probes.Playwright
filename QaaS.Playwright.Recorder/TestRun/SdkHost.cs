using QaaS.Playwright.Files;

namespace QaaS.Playwright.Recorder.TestRun;

/// <summary>
/// The <c>dotnet</c> that builds: the first of the hosts a person may have that has an SDK of the recorder's .NET. The
/// recorder runs on a runtime, often one installed for every user, while the SDK may be installed apart, for one user,
/// and found on PATH: the runtime's own host then has no SDK, and is passed over. Every SDK command (the compiler,
/// MSBuild's questions, a build) uses the host chosen, and a build without one says each host it looked at.
/// </summary>
internal static class SdkHost
{
    /// <summary>The host, or null; and each host looked at, with what it lacked.</summary>
    public static (string? Host, IReadOnlyList<string> Checked) Find(IEnumerable<string> candidates, int major, IFileSystem files)
    {
        var checkedHosts = new List<string>();
        foreach (var host in candidates.Distinct(files.IgnoresCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal))
        {
            if (!files.Exists(host)) continue;
            var sdks = Path.Combine(Path.GetDirectoryName(host)!, "sdk");
            if (files.Folders(sdks, $"{major}.*").Any()) return (host, checkedHosts);
            checkedHosts.Add($"{host}: no .NET {major} SDK in {sdks}");
        }

        return (null, checkedHosts);
    }

    /// <summary>
    /// Where a host may be, the likeliest first: beside the runtime the recorder runs on, then <c>DOTNET_ROOT</c>, each
    /// folder of PATH, the per-user install, and the machine's.
    /// </summary>
    public static IEnumerable<string> Candidates(string runtimeDirectory, Func<string, string?> environment, bool windows)
    {
        var name = windows ? "dotnet.exe" : "dotnet";
        yield return Path.Combine(Path.GetFullPath(Path.Combine(runtimeDirectory, "..", "..", "..")), name);
        if (environment("DOTNET_ROOT") is { Length: > 0 } root) yield return Path.Combine(root, name);
        foreach (var folder in (environment("PATH") ?? "").Split(windows ? ';' : ':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            yield return Path.Combine(folder.Trim('"'), name);
        if (environment(windows ? "USERPROFILE" : "HOME") is { Length: > 0 } home) yield return Path.Combine(home, ".dotnet", name);
        if (windows && environment("ProgramFiles") is { Length: > 0 } programs) yield return Path.Combine(programs, "dotnet", name);
    }
}
