using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace QaaS.Playwright.Browser;

/// <summary>
/// Starts Chrome for a BrowserUrl on this machine when nothing answers there. Chrome runs detached, with the profile
/// <c>~/.qaas/chrome-profile</c>, and keeps running, so later runs reuse it and its logins. (Chrome 136+ refuses remote
/// debugging on the default profile.)
/// </summary>
internal static class LocalChromeLauncher
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(2) };
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(60);

    public static async Task EnsureRunningAsync(string cdpUrl, string? executablePath, ILogger logger)
    {
        if (await IsReachableAsync(cdpUrl)) return;

        var port = PortOf(cdpUrl);
        var profileDir = BrowserDefaults.ChromeProfileDir;
        Directory.CreateDirectory(profileDir);

        // Parallel runs must not each start a Chrome: the first starts it, the others find it running.
        await using var launchLock = await LockAsync(Path.Combine(profileDir, ".launch.lock"));
        if (await IsReachableAsync(cdpUrl)) return;

        var chrome = ChromeExecutable.Resolve(executablePath);
        logger.LogInformation("Nothing answers at {Url}; starting {Chrome}", cdpUrl, chrome);
        StartDetached(chrome,
        [
            $"--remote-debugging-port={port}", "--remote-allow-origins=*", $"--user-data-dir={profileDir}",
            "--no-first-run", "--no-default-browser-check",
        ]);
        await WaitUntilReachableAsync(cdpUrl, BrowserDefaults.ChromeStartupTimeout);
    }

    public static async Task<bool> IsReachableAsync(string cdpUrl)
    {
        var versionUrl = new UriBuilder(cdpUrl) { Path = "/json/version", Query = "" }.Uri;
        try
        {
            using var response = await Http.GetAsync(versionUrl, HttpCompletionOption.ResponseHeadersRead);
            return response.IsSuccessStatusCode;
        }
        catch (Exception failure) when (failure is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }

    private static int PortOf(string cdpUrl) =>
        BrowserUrl.IsOnThisMachine(cdpUrl) && new Uri(cdpUrl) is { IsDefaultPort: false } uri
            ? uri.Port
            : throw new ArgumentException(
                "To start Chrome, the browser URL must be an absolute http(s) URL on this machine with a port, " +
                $"e.g. http://localhost:9222 (got '{cdpUrl}').");

    private static async Task<FileStream> LockAsync(string path)
    {
        var waited = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
            }
            catch (IOException) when (waited.Elapsed < LockTimeout)
            {
                await Task.Delay(200);
            }
        }
    }

    // On Linux and macOS, through nohup with output to /dev/null, so Chrome outlives this process and never blocks on
    // a full pipe. On Windows a started process outlives its parent anyway.
    private static void StartDetached(string chrome, string[] arguments)
    {
        var commandLine = string.Join(' ', arguments.Prepend(chrome).Select(ShellQuote));
        var start = OperatingSystem.IsWindows()
            ? new ProcessStartInfo(chrome, arguments)
            : new ProcessStartInfo("/bin/sh", ["-c", $"nohup {commandLine} </dev/null >/dev/null 2>&1 &"]);
        start.UseShellExecute = false;
        start.CreateNoWindow = true;
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {chrome}.");
    }

    private static string ShellQuote(string value) => $"'{value.Replace("'", @"'\''")}'";

    private static async Task WaitUntilReachableAsync(string cdpUrl, TimeSpan timeout)
    {
        var waited = Stopwatch.StartNew();
        while (waited.Elapsed < timeout)
        {
            if (await IsReachableAsync(cdpUrl)) return;
            await Task.Delay(300);
        }

        throw new TimeoutException(
            $"Chrome did not answer at {cdpUrl} within {timeout.TotalSeconds:0}s. A Chrome already open on " +
            $"{BrowserDefaults.ChromeProfileDir} without remote debugging takes over the launch: close it and run again.");
    }
}
