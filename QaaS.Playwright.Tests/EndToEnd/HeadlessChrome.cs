using System.Diagnostics;
using QaaS.Playwright.Browser;

namespace QaaS.Playwright.Tests.EndToEnd;

/// <summary>A headless Chrome started by hand, as teams run one, with a throwaway profile. Disposing stops it.</summary>
public sealed class HeadlessChrome : IDisposable
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(20);

    private readonly Process _process;
    private readonly string _profileDir;
    private bool _disposed;

    private HeadlessChrome(Process process, string profileDir)
    {
        _process = process;
        _profileDir = profileDir;
    }

    /// <summary>The CDP endpoint, e.g. <c>http://127.0.0.1:41234</c>.</summary>
    public string Url { get; private set; } = "";

    /// <summary>Starts Chrome, or ignores the calling test when Chrome is not installed.</summary>
    public static async Task<HeadlessChrome> StartAsync(params string[] extraArguments)
    {
        var executable = ChromeExecutable.Find();
        if (executable is null) Assert.Ignore("Google Chrome is not installed.");

        // Port 0 lets Chrome pick a free port and write it to DevToolsActivePort, so no other process can take it first.
        var profileDir = Directory.CreateTempSubdirectory("qaas-e2e-chrome-").FullName;
        var start = new ProcessStartInfo(executable!,
        [
            "--headless=new", "--remote-debugging-port=0", $"--user-data-dir={profileDir}",
            "--no-first-run", "--no-default-browser-check", .. extraArguments, "about:blank",
        ]) { RedirectStandardOutput = true, RedirectStandardError = true };
        var process = Process.Start(start)!;
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var chrome = new HeadlessChrome(process, profileDir);
        try
        {
            chrome.Url = await WaitForEndpointAsync(process, profileDir);
            return chrome;
        }
        catch
        {
            chrome.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _process.Kill(entireProcessTree: true);
        _process.WaitForExit(5_000);
        _process.Dispose();
        try { Directory.Delete(_profileDir, recursive: true); }
        catch (IOException) { /* Chrome may still be releasing a file; the temp folder is cleaned up eventually. */ }
    }

    private static async Task<string> WaitForEndpointAsync(Process process, string profileDir)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < StartupTimeout && !process.HasExited)
        {
            if (PortOf(profileDir) is { } port && await LocalChromeLauncher.IsReachableAsync($"http://127.0.0.1:{port}"))
                return $"http://127.0.0.1:{port}";
            await Task.Delay(50);
        }

        throw new TimeoutException($"Chrome did not expose CDP within {StartupTimeout.TotalSeconds}s.");
    }

    // The port Chrome picked. On Windows the file is locked while Chrome writes it: the caller asks again.
    private static string? PortOf(string profileDir)
    {
        var portFile = Path.Combine(profileDir, "DevToolsActivePort");
        try
        {
            return File.Exists(portFile) ? File.ReadLines(portFile).FirstOrDefault() : null;
        }
        catch (IOException)
        {
            return null;
        }
    }
}
