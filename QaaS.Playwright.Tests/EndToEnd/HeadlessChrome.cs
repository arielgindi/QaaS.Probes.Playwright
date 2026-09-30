using System.Diagnostics;
using QaaS.Playwright.Browser;

namespace QaaS.Playwright.Tests.EndToEnd;

/// <summary>A headless Chrome started by hand, as teams run one, with a throwaway profile. Disposing stops it.</summary>
public sealed class HeadlessChrome : IDisposable
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(20);

    private readonly Process _process;
    private readonly string _profileDir;

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

        var profileDir = Directory.CreateTempSubdirectory("qaas-e2e-chrome-").FullName;
        // Port 0 lets Chrome pick a free port and write it to DevToolsActivePort, so no other process can take it first.
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
        _process.Kill(entireProcessTree: true);
        _process.WaitForExit(5_000);
        _process.Dispose();
        try { Directory.Delete(_profileDir, recursive: true); }
        catch (IOException) { /* Chrome may still be releasing a file; the temp folder is cleaned up eventually. */ }
    }

    private static async Task<string> WaitForEndpointAsync(Process process, string profileDir)
    {
        var portFile = Path.Combine(profileDir, "DevToolsActivePort");
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < StartupTimeout && !process.HasExited)
        {
            if (File.Exists(portFile) && File.ReadLines(portFile).FirstOrDefault() is { Length: > 0 } port)
            {
                var url = $"http://127.0.0.1:{port}";
                if (await LocalChromeLauncher.IsReachableAsync(url)) return url;
            }
            await Task.Delay(50);
        }

        throw new TimeoutException($"Chrome did not expose CDP within {StartupTimeout.TotalSeconds}s.");
    }
}
