using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace QaaS.Probes.Playwright.Engine;

/// <summary>
/// Single source of truth for browser-related defaults shared by the probe and
/// the recorder. Values are loaded once from the embedded <c>browser-defaults.yaml</c>
/// shipped with this library — edit that file (in the source tree) when forking,
/// rebuild, and every consuming repo inherits the new values. Per-test YAML can
/// still override any URL via ProbeConfiguration.
/// </summary>
public static class BrowserDefaults
{
    private static readonly Settings _s = Load();

    // ── URLs ─────────────────────────────────────────────────────────────────
    public static string RemoteUrl => _s.RemoteBrowserUrl;
    public static string LocalUrl => _s.LocalBrowserUrl;

    // ── Recorder ────────────────────────────────────────────────────────────
    public static string ChromeChannel => _s.ChromeChannel;
    public static string RecorderViewport => _s.RecorderViewport;

    // ── Timing ──────────────────────────────────────────────────────────────
    public static TimeSpan LocalStartupTimeout => TimeSpan.FromSeconds(_s.LocalStartupTimeoutSeconds);

    // ── Paths under ~/.qaas (filesystem layout, not user-configurable) ──────
    public static string QaasDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".qaas");
    public static string ChromeProfileDir => Path.Combine(QaasDir, "chrome-profile");
    public static string AuthStatePath => Path.Combine(QaasDir, "auth.json");

    private static Settings Load()
    {
        var asm = typeof(BrowserDefaults).Assembly;
        var resourceName = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("browser-defaults.yaml", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                "Embedded resource 'browser-defaults.yaml' not found. Check the .csproj's " +
                "<EmbeddedResource> entry exists and the file is present in the project.");

        using var stream = asm.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(PascalCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        return deserializer.Deserialize<Settings>(reader)
            ?? throw new InvalidOperationException("browser-defaults.yaml parsed as null.");
    }

    private sealed class Settings
    {
        public string RemoteBrowserUrl { get; set; } = null!;
        public string LocalBrowserUrl { get; set; } = null!;
        public string ChromeChannel { get; set; } = null!;
        public string RecorderViewport { get; set; } = null!;
        public int LocalStartupTimeoutSeconds { get; set; }
    }
}
