namespace QaaS.Playwright.Browser;

/// <summary>
/// Writes a file so that no reader ever sees it half-written, since parallel sessions may load a storage state while
/// another saves it: the text goes to a temp file beside the target, which is then renamed over it. A saved login is as
/// good as a password, so on Linux and macOS only its owner may read the file; on Windows it inherits the folder's
/// permissions.
/// </summary>
internal static class AtomicFileWriter
{
    private const int MoveAttempts = 10;

    /// <summary>Writes <paramref name="contents"/> to <paramref name="path"/> and returns its absolute path.</summary>
    public static async Task<string> WriteAsync(string path, string contents)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        // Beside the target, so the move is a rename; uniquely named, so parallel writers do not collide; and short, as
        // the target's name may already be as long as a file name can be.
        var tempPath = Path.Combine(Path.GetDirectoryName(fullPath)!, $".qaas-{Guid.NewGuid():N}.tmp");
        try
        {
            // Owner-only from its creation; the rename gives the target these permissions, even one that had others.
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            await using (var file = new StreamWriter(tempPath, options)) await file.WriteAsync(contents);
            await MoveOverAsync(tempPath, fullPath);
        }
        finally
        {
            File.Delete(tempPath); // Left over only when writing or moving failed.
        }

        return fullPath;
    }

    // Windows refuses to replace a file another process is reading, e.g. a session loading the login: wait for it.
    private static async Task MoveOverAsync(string source, string target)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(source, target, overwrite: true);
                return;
            }
            catch (Exception failure)
                when (failure is IOException or UnauthorizedAccessException && attempt < MoveAttempts)
            {
                await Task.Delay(100);
            }
        }
    }
}
