using QaaS.Playwright.Recorder.Login;

namespace QaaS.Playwright.Tests.Recorder;

/// <summary>The copies of the builds the tests load flows from go to a folder of the run's, never the person's.</summary>
[SetUpFixture]
public class ShadowRoot
{
    private string _folder = null!;

    [OneTimeSetUp]
    public void UseAFolderOfTheRun()
    {
        _folder = Directory.CreateTempSubdirectory("qaas-shadow-").FullName;
        ShadowCopy.Root = _folder;
    }

    [OneTimeTearDown]
    public void DeleteIt()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            // Loaded still, on Windows.
        }
    }
}
