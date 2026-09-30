using QaaS.Playwright.Browser;

namespace QaaS.Playwright.Tests.Browser;

[TestFixture]
public class AtomicFileWriterTests
{
    private string _dir = null!;

    [SetUp]
    public void SetUp() => _dir = Path.Combine(Path.GetTempPath(), $"qaas-afw-{Guid.NewGuid():N}");

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Test]
    public async Task WriteAsync_WritesTheContents_ToTheTarget()
    {
        var path = Path.Combine(_dir, "state.json");

        await AtomicFileWriter.WriteAsync(path, "hello");

        Assert.That(await File.ReadAllTextAsync(path), Is.EqualTo("hello"));
    }

    [Test]
    public async Task WriteAsync_CreatesMissingParentDirectories()
    {
        var path = Path.Combine(_dir, "nested", "deep", "state.json");

        await AtomicFileWriter.WriteAsync(path, "x");

        Assert.That(File.Exists(path), Is.True);
    }

    [Test]
    public async Task WriteAsync_OverwritesAnExistingFile()
    {
        var path = Path.Combine(_dir, "state.json");
        Directory.CreateDirectory(_dir);
        await File.WriteAllTextAsync(path, "old");

        await AtomicFileWriter.WriteAsync(path, "new");

        Assert.That(await File.ReadAllTextAsync(path), Is.EqualTo("new"));
    }

    [Test]
    public async Task WriteAsync_LeavesNoTempFileBehind()
    {
        var path = Path.Combine(_dir, "state.json");

        await AtomicFileWriter.WriteAsync(path, "data");

        Assert.That(Directory.GetFiles(_dir, "*.tmp"), Is.Empty);
    }

    [Test]
    public async Task WriteAsync_ReturnsTheAbsolutePath()
    {
        var path = Path.Combine(_dir, "state.json");

        var returned = await AtomicFileWriter.WriteAsync(path, "x");

        Assert.That(returned, Is.EqualTo(Path.GetFullPath(path)));
        Assert.That(Path.IsPathRooted(returned), Is.True);
    }

    [Test]
    public void WriteAsync_BlankPath_Throws()
    {
        Assert.That(
            async () => await AtomicFileWriter.WriteAsync("   ", "x"),
            Throws.InstanceOf<ArgumentException>());
    }
}
