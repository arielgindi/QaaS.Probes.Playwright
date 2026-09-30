using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace QaaS.Playwright.Browser;

/// <summary>
/// Single source of truth for browser defaults shared by the probe and the recorder. Values are loaded once
/// (lazily, on first use) from the embedded <c>browser-defaults.yaml</c> shipped with this library — edit that
/// file when forking, rebuild, and every consuming repo inherits the new values. Per-test YAML can still
/// override any URL via <c>ProbeConfiguration</c>. Loading is lazy (not a static field initializer) so a
/// malformed file surfaces its real error message instead of a wrapping <c>TypeInitializationException</c>.
/// </summary>
public static class BrowserDefaults
{
    private static readonly Lazy<Settings> LazySettings = new(Load);

    private static Settings Current => LazySettings.Value;

    /// <summary>CDP endpoint of the Chrome the probe runs in when the yaml sets no BrowserUrl.</summary>
    public static string BrowserUrl => Current.BrowserUrl;

    /// <summary>Chrome channel the recorder launches (for example <c>chrome</c>).</summary>
    public static string ChromeChannel => Current.ChromeChannel;

    /// <summary>Viewport the recorder opens Chrome with (for example <c>1920,1080</c>).</summary>
    public static string RecorderViewport => Current.RecorderViewport;

    /// <summary>
    /// The attribute the recorder records as a test id and the probe resolves <c>GetByTestId</c> against — read by
    /// both sides so they always agree. Defaults to <c>data-testid</c>, Playwright's own default.
    /// </summary>
    public static string TestIdAttribute => Current.TestIdAttribute;

    /// <summary>How long the probe waits for a Chrome it started to become reachable.</summary>
    public static TimeSpan ChromeStartupTimeout => TimeSpan.FromSeconds(Current.ChromeStartupTimeoutSeconds);

    /// <summary>The <c>~/.qaas</c> working directory (filesystem layout, not user-configurable).</summary>
    public static string QaasDir => Path.Combine(ResolveHomeDirectory(), ".qaas");

    /// <summary>The persistent profile of a Chrome the probe starts.</summary>
    public static string ChromeProfileDir => Path.Combine(QaasDir, "chrome-profile");

    /// <summary>Where the recorder persists logged-in storage state.</summary>
    public static string AuthStatePath => Path.Combine(QaasDir, "auth.json");

    private static string ResolveHomeDirectory()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home) || !Path.IsPathRooted(home))
            throw new InvalidOperationException(
                "Could not resolve an absolute user-profile directory for the ~/.qaas working directory " +
                $"(got '{home}'). Set HOME (Linux/macOS) or USERPROFILE (Windows) to an absolute path.");
        return home;
    }

    private static Settings Load()
    {
        var assembly = typeof(BrowserDefaults).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith("browser-defaults.yaml", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                "Embedded resource 'browser-defaults.yaml' not found. Check the .csproj's <EmbeddedResource> " +
                "entry exists and the file is present in the project.");

        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(PascalCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        var settings = deserializer.Deserialize<Settings>(reader)
            ?? throw new InvalidOperationException("browser-defaults.yaml parsed as null.");

        Validate(settings);
        return settings;
    }

    // IgnoreUnmatchedProperties() means a renamed/missing key deserializes to its default, so validate here to
    // fail at load with a clear message instead of far downstream (e.g. a 0-second startup timeout).
    private static void Validate(Settings settings)
    {
        RequireNonEmpty(settings.BrowserUrl, nameof(Settings.BrowserUrl));
        RequireNonEmpty(settings.ChromeChannel, nameof(Settings.ChromeChannel));
        RequireNonEmpty(settings.RecorderViewport, nameof(Settings.RecorderViewport));
        RequireNonEmpty(settings.TestIdAttribute, nameof(Settings.TestIdAttribute));
        if (settings.ChromeStartupTimeoutSeconds <= 0)
            throw new InvalidOperationException(
                $"browser-defaults.yaml: '{nameof(Settings.ChromeStartupTimeoutSeconds)}' must be greater than 0 " +
                $"(was {settings.ChromeStartupTimeoutSeconds}).");
    }

    private static void RequireNonEmpty(string? value, string key)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"browser-defaults.yaml is missing the required value '{key}'.");
    }

    private sealed class Settings
    {
        public string BrowserUrl { get; set; } = null!;
        public string ChromeChannel { get; set; } = null!;
        public string RecorderViewport { get; set; } = null!;
        public string TestIdAttribute { get; set; } = null!;
        public int ChromeStartupTimeoutSeconds { get; set; }
    }
}
