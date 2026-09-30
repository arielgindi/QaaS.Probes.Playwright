namespace QaaS.Playwright.Engine;

/// <summary>
/// Writes a file atomically: the contents go to a sibling temp file which is then moved into place with a
/// same-volume rename, so a process reading the destination concurrently sees either the old file or the complete
/// new one — never a partially-written file. The parent directory is created if missing, and the temp file is
/// cleaned up if the write or move fails.
/// </summary>
internal static class AtomicFileWriter
{
    /// <summary>
    /// Atomically writes <paramref name="contents"/> to <paramref name="path"/> and returns the absolute path
    /// written. A relative <paramref name="path"/> resolves against the current directory.
    /// </summary>
    public static async Task<string> WriteAsync(string path, string contents, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(contents);

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        // The temp file shares the destination's directory, so the move is a same-volume rename (atomic) rather
        // than a copy. A GUID suffix keeps concurrent writers to the same target from colliding on the temp name.
        var tempPath = $"{fullPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(tempPath, contents, ct);
            File.Move(tempPath, fullPath, overwrite: true);
        }
        finally
        {
            // On success the move already consumed the temp file; delete it only if an error left it behind.
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); }
                catch { /* best-effort: never mask the real write/move failure */ }
            }
        }

        return fullPath;
    }
}
