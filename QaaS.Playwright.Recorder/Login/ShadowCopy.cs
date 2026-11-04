using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using QaaS.Playwright.Browser;
using QaaS.Playwright.Files;

namespace QaaS.Playwright.Recorder.Login;

/// <summary>
/// A build of the person's tests copied apart, which its flows are loaded from: Windows locks a loaded DLL, so loaded
/// in place, the person could not build their tests again while recording. One copy per build, told by each file's
/// path, size and time, so a build that changed is copied anew and an unchanged one is not; a copy keeps
/// <see cref="System.Reflection.Assembly.Location"/> meaningful for a flow that reads a file beside its DLL. Only what
/// loading needs is copied: the assemblies and their symbols, the dependencies and runtime files, satellite resources,
/// and this platform's native libraries, never the browser's driver.
/// </summary>
internal static class ShadowCopy
{
    private const int Kept = 10;

    public static string Root => Path.Combine(Path.GetDirectoryName(BrowserDefaults.AuthStatePath)!, "shadow");

    /// <summary>Where the copy of <paramref name="assembly"/> is: made now, unless this build was copied already.</summary>
    public static string Of(string assembly, FileStore store, string? root = null)
    {
        root ??= Root;
        var folder = Path.GetDirectoryName(Path.GetFullPath(assembly))!;
        var names = Needed(folder).ToList();
        var copy = Path.Combine(root, $"{Path.GetFileNameWithoutExtension(assembly)}-{Key(folder, names)}");
        if (!Directory.Exists(copy))
        {
            store.CopyFolder(folder, names, copy);
            Prune(root, store);
        }

        return Path.Combine(copy, Path.GetFileName(assembly));
    }

    private static IEnumerable<string> Needed(string folder)
    {
        string[] platforms = [RuntimeInformation.RuntimeIdentifier, RuntimeInformation.RuntimeIdentifier.Split('-')[0],
            OperatingSystem.IsWindows() ? "win" : "unix"];
        foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
        {
            var name = Path.GetRelativePath(folder, file);
            var parts = name.Split(Path.DirectorySeparatorChar);
            var kept = parts switch
            {
                [var single] => single.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || single.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase)
                                || single.EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase)
                                || single.EndsWith(".runtimeconfig.json", StringComparison.OrdinalIgnoreCase),
                ["runtimes", var platform, ..] => platforms.Contains(platform, StringComparer.OrdinalIgnoreCase),
                [var culture, var resources] => culture.Length is 2 or 5 && resources.EndsWith(".resources.dll", StringComparison.OrdinalIgnoreCase),
                _ => false,
            };
            if (kept) yield return name;
        }
    }

    private static string Key(string folder, IEnumerable<string> names)
    {
        var text = new StringBuilder(folder);
        foreach (var name in names.Order(StringComparer.Ordinal))
        {
            var file = new FileInfo(Path.Combine(folder, name));
            text.Append('\n').Append(name).Append(' ').Append(file.Length).Append(' ').Append(file.LastWriteTimeUtc.Ticks);
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())))[..16];
    }

    // The newest copies stay; an older one goes once nothing loads it (Windows refuses while a recorder does).
    private static void Prune(string root, FileStore store)
    {
        foreach (var old in new DirectoryInfo(root).GetDirectories().Where(folder => !folder.Name.EndsWith(".tmp", StringComparison.Ordinal))
                     .OrderByDescending(folder => folder.LastWriteTimeUtc).Skip(Kept))
        {
            try
            {
                store.Files.DeleteFolder(old.FullName);
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                // In use: the next recording prunes it.
            }
        }
    }
}
