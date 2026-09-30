using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace QaaS.Playwright.Browser;

/// <summary>
/// The browser defaults the probe and the recorder share, read once from the embedded browser-defaults.yaml. Edit
/// that file when forking the library, and every consumer inherits the new values.
/// </summary>
public static class BrowserDefaults
{
    // Lazy, so a broken file reports its own error rather than a TypeInitializationException.
    private static readonly Lazy<Settings> Current = new(Load);

    /// <summary>The CDP endpoint used when ProbeConfiguration sets no BrowserUrl.</summary>
    public static string BrowserUrl => Current.Value.BrowserUrl;

    /// <summary>The Chrome channel the recorder opens, e.g. <c>chrome</c>.</summary>
    public static string ChromeChannel => Current.Value.ChromeChannel;

    /// <summary>The viewport the recorder opens Chrome with, e.g. <c>1920,1080</c>.</summary>
    public static string RecorderViewport => Current.Value.RecorderViewport;

    /// <summary>The attribute GetByTestId() resolves against, in the recorder and the probe alike.</summary>
    public static string TestIdAttribute => Current.Value.TestIdAttribute;

    /// <summary>How long the probe waits for a Chrome it started to answer.</summary>
    public static TimeSpan ChromeStartupTimeout => TimeSpan.FromSeconds(Current.Value.ChromeStartupTimeoutSeconds);

    /// <summary>The profile of a Chrome the probe starts: <c>~/.qaas/chrome-profile</c>.</summary>
    public static string ChromeProfileDir => Path.Combine(QaasDir, "chrome-profile");

    /// <summary>Where the recorder keeps its login between recordings: <c>~/.qaas/auth.json</c>.</summary>
    public static string AuthStatePath => Path.Combine(QaasDir, "auth.json");

    private static string QaasDir
    {
        get
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!Path.IsPathRooted(home))
                throw new InvalidOperationException(
                    $"Could not find the home directory (got '{home}'). Set HOME, or USERPROFILE on Windows.");
            return Path.Combine(home, ".qaas");
        }
    }

    private static Settings Load()
    {
        const string resource = "QaaS.Playwright.browser-defaults.yaml";
        using var stream = typeof(BrowserDefaults).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"The embedded {resource} is missing from the build.");
        using var reader = new StreamReader(stream);
        var settings = new DeserializerBuilder()
            .WithNamingConvention(PascalCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build()
            .Deserialize<Settings?>(reader) ?? new Settings();

        // A missing or renamed key would silently become empty, so every value is checked here.
        string[] values = [settings.BrowserUrl, settings.ChromeChannel, settings.RecorderViewport, settings.TestIdAttribute];
        if (values.Any(string.IsNullOrWhiteSpace) || settings.ChromeStartupTimeoutSeconds <= 0)
            throw new InvalidOperationException(
                "browser-defaults.yaml must set BrowserUrl, ChromeChannel, RecorderViewport, TestIdAttribute and a " +
                "positive ChromeStartupTimeoutSeconds.");
        return settings;
    }

    private sealed class Settings
    {
        public string BrowserUrl { get; set; } = "";
        public string ChromeChannel { get; set; } = "";
        public string RecorderViewport { get; set; } = "";
        public string TestIdAttribute { get; set; } = "";
        public int ChromeStartupTimeoutSeconds { get; set; }
    }
}
