using QaaS.Playwright.Recorder;

namespace QaaS.Playwright.Tests.Recorder;

[TestFixture]
public class ProjectNamespaceTests
{
    private DirectoryInfo _root = null!;

    [SetUp]
    public void CreateFolder() => _root = Directory.CreateTempSubdirectory("qaas-ns-");

    [TearDown]
    public void DeleteFolder() => _root.Delete(recursive: true);

    [Test]
    public void Of_FolderInNoProject_IsFlows() =>
        Assert.That(ProjectNamespace.Of(_root.FullName), Is.EqualTo("Flows"));

    [Test]
    public void Of_SubfolderOfAProject_IsTheRootNamespacePlusTheSubfolder()
    {
        File.WriteAllText(Path.Combine(_root.FullName, "MyApp.Tests.csproj"),
            "<Project><PropertyGroup><RootNamespace>MyApp.Tests</RootNamespace></PropertyGroup></Project>");
        var flows = _root.CreateSubdirectory("Ui/2fa-flows");

        Assert.That(ProjectNamespace.Of(flows.FullName), Is.EqualTo("MyApp.Tests.Ui._2faflows"));
    }

    [Test]
    public void Of_FolderNamedLikeAKeyword_IsEscaped()
    {
        File.WriteAllText(Path.Combine(_root.FullName, "Shop.Tests.csproj"), "<Project />");

        Assert.That(ProjectNamespace.Of(_root.CreateSubdirectory("internal").FullName), Is.EqualTo("Shop.Tests.@internal"));
    }

    [Test]
    public void Of_ProjectWithoutRootNamespace_UsesTheProjectFileName()
    {
        File.WriteAllText(Path.Combine(_root.FullName, "Shop.Tests.csproj"), "<Project />");

        Assert.That(ProjectNamespace.Of(_root.FullName), Is.EqualTo("Shop.Tests"));
    }
}
