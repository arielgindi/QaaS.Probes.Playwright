using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using QaaS.Playwright.Configuration;

namespace QaaS.Playwright.Browser;

/// <summary>A login one session saves to a file and later sessions start from.</summary>
internal static class StorageState
{
    private static readonly DateTime ProcessStart = Process.GetCurrentProcess().StartTime.ToUniversalTime();

    /// <summary>The saved login to start the run from, or null when LoadStorageStatePath is not set.</summary>
    public static string? FileToLoad(PlaywrightFlowConfig config, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(config.LoadStorageStatePath)) return null;

        var path = Path.GetFullPath(config.LoadStorageStatePath);
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"LoadStorageStatePath '{path}' does not exist. The session that saves it (SaveStorageStatePath) " +
                "must finish first, or remove LoadStorageStatePath.", path);

        // A filter such as -a can skip the session that saves it, and leave this run an old login.
        var savedAt = File.GetLastWriteTimeUtc(path);
        if (savedAt < ProcessStart)
            logger.LogWarning("LoadStorageStatePath '{Path}' was saved {Hours:0.#} h ago, before this run: did the " +
                              "session that saves it run?", path, (DateTime.UtcNow - savedAt).TotalHours);

        logger.LogInformation("Loading storage state from {Path}", path);
        return path;
    }

    /// <summary>Writes the context's cookies and localStorage to <paramref name="path"/>.</summary>
    public static async Task SaveAsync(IBrowserContext context, string path, ILogger logger)
    {
        var savedPath = await AtomicFileWriter.WriteAsync(path, await context.StorageStateAsync());
        logger.LogInformation("Saved storage state to {Path}", savedPath);
    }
}
