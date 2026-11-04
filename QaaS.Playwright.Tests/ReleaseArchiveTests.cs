using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace QaaS.Playwright.Tests;

/// <summary>
/// The release's "Source code" zip, as GitHub makes it from the repository with its export-ignore rules, has to stay
/// small enough for a size-limited transfer into a closed network, and still build.
/// </summary>
[TestFixture]
public class ReleaseArchiveTests
{
    private const long Limit = 650 * 1024;

    [Test]
    public void SourceArchive_StaysUnder650KB()
    {
        var root = RepositoryRoot();
        var git = new ProcessStartInfo("git", ["archive", "--worktree-attributes", "--format=zip", "HEAD"])
        {
            WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
        };
        Process process;
        try
        {
            process = Process.Start(git)!;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Assert.Ignore("git is not installed");
            return;
        }

        using (process)
        {
            using var archive = new MemoryStream();
            process.StandardOutput.BaseStream.CopyTo(archive);
            process.WaitForExit();
            if (process.ExitCode != 0) Assert.Ignore($"not a git checkout: {process.StandardError.ReadToEnd().Trim()}");
            Assert.That(archive.Length, Is.LessThan(Limit),
                $"the source zip is {archive.Length / 1024} KB: leave what building does not need out of it with " +
                "export-ignore in .gitattributes, or make it smaller");
        }
    }

    private static string RepositoryRoot([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, ".."));
}
