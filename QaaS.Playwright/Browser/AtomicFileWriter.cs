namespace QaaS.Playwright.Browser;

/// <summary>
/// Writes a file so that no reader ever sees it half-written, since parallel sessions may load a storage state while
/// another saves it: the text goes to a temp file beside the target, which is then renamed over it.
/// </summary>
internal static class AtomicFileWriter
{
    /// <summary>Writes <paramref name="contents"/> to <paramref name="path"/> and returns its absolute path.</summary>
    public static async Task<string> WriteAsync(string path, string contents)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        // Beside the target, so the move is a rename; uniquely named, so parallel writers do not collide.
        var tempPath = $"{fullPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(tempPath, contents);
            File.Move(tempPath, fullPath, overwrite: true);
        }
        finally
        {
            File.Delete(tempPath); // Left over only when writing or moving failed.
        }

        return fullPath;
    }
}
