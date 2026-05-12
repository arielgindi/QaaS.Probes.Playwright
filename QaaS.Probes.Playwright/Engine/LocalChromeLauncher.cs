using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace QaaS.Probes.Playwright.Engine;

/// <summary>
/// Ensures a local Chrome with --remote-debugging-port is running, launching it
/// detached if not. Subsequent runs reuse the same Chrome — fingerprint/permission
/// prompts are handled once per Chrome lifetime instead of once per test run.
/// </summary>
public static class LocalChromeLauncher
{
    // Static reuse — the poll loop shouldn't allocate a socket every 300ms.
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(2) };

    public static async Task EnsureRunningAsync(
        string cdpUrl,
        string? executablePathOverride,
        TimeSpan startupTimeout,
        ILogger logger,
        CancellationToken ct = default)
    {
        if (await IsReachableAsync(cdpUrl, ct)) return;

        var uri = ParseOrThrow(cdpUrl);
        var profileDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".qaas", "chrome-profile");
        Directory.CreateDirectory(profileDir);

        // Cross-process lock so parallel runs don't both spawn against the same profile.
        await using var _ = await AcquireLockAsync(Path.Combine(profileDir, ".launch.lock"), ct);
        if (await IsReachableAsync(cdpUrl, ct)) return;

        var exe = executablePathOverride ?? FindChrome()
            ?? throw new InvalidOperationException(NotFoundMessage());

        logger.LogInformation("Local Chrome not running at {Url} — starting {Exe}", cdpUrl, exe);
        LaunchDetached(exe, uri.Port, profileDir);
        await WaitForReachableAsync(cdpUrl, startupTimeout, ct);
    }

    public static async Task<bool> IsReachableAsync(string cdpUrl, CancellationToken ct = default)
    {
        var url = new UriBuilder(cdpUrl) { Path = "/json/version", Query = "" }.Uri.ToString();
        try { return (await HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct)).IsSuccessStatusCode; }
        catch (HttpRequestException) { return false; }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { return false; }
    }

    public static string? FindChrome() => Candidates().FirstOrDefault(File.Exists);

    private static async Task WaitForReachableAsync(string cdpUrl, TimeSpan timeout, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            if (await IsReachableAsync(cdpUrl, ct)) return;
            await Task.Delay(300, ct);
        }
        throw new TimeoutException(
            $"Chrome did not become reachable at {cdpUrl} within {timeout.TotalSeconds:0}s.\n" +
            "Likely causes:\n" +
            "  • Chrome is already running with your default profile — close it and re-run\n" +
            "    (Chrome only allows one instance per profile; second launch is a no-op)\n" +
            "  • A permission/fingerprint dialog is showing — approve it and re-run");
    }

    private static Uri ParseOrThrow(string cdpUrl)
    {
        if (!Uri.TryCreate(cdpUrl, UriKind.Absolute, out var uri) || uri.Port <= 0)
            throw new ArgumentException(
                $"Browser URL must be an absolute http URI with a port: '{cdpUrl}'. Example: http://localhost:9222");
        return uri;
    }

    private static async Task<IAsyncDisposable> AcquireLockAsync(string lockPath, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                return new LockHandle(new FileStream(
                    lockPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None));
            }
            catch (IOException)
            {
                if (sw.Elapsed > TimeSpan.FromSeconds(60))
                    throw new TimeoutException($"Could not acquire launch lock at {lockPath} within 60s.");
                await Task.Delay(200, ct);
            }
        }
    }

    private sealed class LockHandle(FileStream fs) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() { fs.Dispose(); return ValueTask.CompletedTask; }
    }

    // Chrome 136+ refuses --remote-debugging-port on the DEFAULT user-data-dir for
    // security. We must use a dedicated profile dir (~/.qaas/chrome-profile).
    // It's persistent — log in once, sessions/cookies/bookmarks stay between runs.
    // POSIX: detach via `nohup ... &` so Chrome survives SIGHUP.
    // Windows: Process.Start already outlives the parent.
    private static void LaunchDetached(string exe, int port, string profileDir)
    {
        var psi = new ProcessStartInfo
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };

        if (OperatingSystem.IsWindows())
        {
            psi.FileName = exe;
            psi.ArgumentList.Add($"--remote-debugging-port={port}");
            psi.ArgumentList.Add("--remote-allow-origins=*");
            psi.ArgumentList.Add($"--user-data-dir={profileDir}");
            psi.ArgumentList.Add("--no-first-run");
            psi.ArgumentList.Add("--no-default-browser-check");
        }
        else
        {
            var args = $"--remote-debugging-port={port} --remote-allow-origins=* " +
                       $"--user-data-dir={Quote(profileDir)} --no-first-run --no-default-browser-check";
            psi.FileName = "/bin/sh";
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add($"nohup {Quote(exe)} {args} </dev/null >/dev/null 2>&1 &");
        }

        using var p = Process.Start(psi)
            ?? throw new InvalidOperationException("Process.Start returned null when launching Chrome.");
    }

    private static string Quote(string s) => "'" + s.Replace("'", "'\\''") + "'";

    private static IEnumerable<string> Candidates()
    {
        if (OperatingSystem.IsWindows())
            return new[] { "ProgramFiles", "ProgramFilesX86", "LocalApplicationData" }
                .Select(f => Path.Combine(
                    Environment.GetFolderPath(Enum.Parse<Environment.SpecialFolder>(f)),
                    @"Google\Chrome\Application\chrome.exe"));

        if (OperatingSystem.IsMacOS())
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return [
                "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",
                Path.Combine(home, "Applications/Google Chrome.app/Contents/MacOS/Google Chrome"),
                "/Applications/Chromium.app/Contents/MacOS/Chromium",
            ];
        }

        return [
            "/usr/bin/google-chrome", "/usr/bin/google-chrome-stable", "/opt/google/chrome/chrome",
            "/snap/bin/chromium",     "/usr/bin/chromium",            "/usr/bin/chromium-browser",
        ];
    }

    private static string NotFoundMessage() =>
        "Chrome was not found. Searched:\n  " +
        string.Join("\n  ", Candidates().Distinct()) +
        "\nInstall Chrome, or set ProbeConfiguration.BrowserExecutablePath in YAML.";
}
