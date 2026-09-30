namespace QaaS.Playwright.Browser;

/// <summary>Finds the Chrome binary to start: the configured one, or the first usual install location that exists.</summary>
internal static class ChromeExecutable
{
    private static readonly Environment.SpecialFolder[] WindowsInstallFolders =
    [
        Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86,
        Environment.SpecialFolder.LocalApplicationData,
    ];

    public static string? Find() => Candidates().FirstOrDefault(File.Exists);

    public static string Resolve(string? configuredPath)
    {
        if (configuredPath is not null)
            return File.Exists(configuredPath)
                ? configuredPath
                : throw new FileNotFoundException(
                    $"BrowserExecutablePath '{configuredPath}' does not exist. Fix it, or remove it to find Chrome " +
                    "automatically.", configuredPath);

        return Find() ?? throw new InvalidOperationException(
            $"Chrome was not found. Searched:\n  {string.Join("\n  ", Candidates())}\n" +
            "Install Chrome, or set ProbeConfiguration.BrowserExecutablePath.");
    }

    private static IEnumerable<string> Candidates()
    {
        if (OperatingSystem.IsWindows())
            return WindowsInstallFolders.Select(folder =>
                Path.Combine(Environment.GetFolderPath(folder), @"Google\Chrome\Application\chrome.exe"));

        if (OperatingSystem.IsMacOS())
            return
            [
                "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Applications/Google Chrome.app/Contents/MacOS/Google Chrome"),
                "/Applications/Chromium.app/Contents/MacOS/Chromium",
            ];

        return
        [
            "/usr/bin/google-chrome", "/usr/bin/google-chrome-stable", "/opt/google/chrome/chrome",
            "/snap/bin/chromium", "/usr/bin/chromium", "/usr/bin/chromium-browser",
        ];
    }
}
