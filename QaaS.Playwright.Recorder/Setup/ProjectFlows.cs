namespace QaaS.Playwright.Recorder.Setup;

/// <summary>The flows of a project's output folder, for the start screen's Continue tab and the name's check.</summary>
internal static class ProjectFlows
{
    /// <summary>
    /// Each flow's class file in <paramref name="outputFolder"/>, the newest first, with its hash and the recording kept
    /// to go on from: the recorder's own copy, or the raw recording kept beside it. None when the folder is not there.
    /// </summary>
    public static IReadOnlyList<ProjectFlow> Of(string outputFolder)
    {
        try
        {
            if (!Directory.Exists(outputFolder)) return [];
            return [.. new DirectoryInfo(outputFolder).GetFiles("*.cs")
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Select(file =>
                {
                    var flow = Path.GetFileNameWithoutExtension(file.Name);
                    return new ProjectFlow(flow, file.FullName, Autosave.Hash(File.ReadAllText(file.FullName)), Autosave.Find(outputFolder, flow));
                })];
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
