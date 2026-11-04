using System.Runtime.InteropServices;
using System.Runtime.Loader;
using QaaS.Playwright.Files;
using QaaS.Playwright.Recorder.Login;
using QaaS.Playwright.Recorder.TestRun;
using QaaS.Playwright.Tests.Files;

namespace QaaS.Playwright.Tests.Recorder;

/// <summary>What the recorder needs of the person's machine: a build it may load without locking, and the SDK.</summary>
[TestFixture]
public class ShadowCopyAndSdkTests
{
    [Test]
    public void ABuild_IsLoadedFromACopyOfWhatLoadingNeeds_MadeOncePerBuild_AndUnloadsWithItsContext()
    {
        var work = Directory.CreateTempSubdirectory("qaas-shadow-").FullName;
        try
        {
            var build = Path.Combine(work, "bin");
            var platform = Path.Combine(build, "runtimes", RuntimeInformation.RuntimeIdentifier, "native");
            foreach (var folder in (string[])[platform, Path.Combine(build, "runtimes", "other-x86", "native"), Path.Combine(build, "de"), Path.Combine(build, ".playwright")])
                Directory.CreateDirectory(folder);
            var dll = Path.Combine(build, "Microsoft.Bcl.AsyncInterfaces.dll");
            File.Copy(Path.Combine(AppContext.BaseDirectory, "Microsoft.Bcl.AsyncInterfaces.dll"), dll);
            foreach (var file in (string[])["Tests.deps.json", "Tests.pdb", "de/Tests.resources.dll", ".playwright/driver.js", "runtimes/other-x86/native/other.so"])
                File.WriteAllText(Path.Combine(build, file), file);
            File.WriteAllText(Path.Combine(platform, "native.so"), "n");
            var root = Path.Combine(work, "shadow");

            var copy = ShadowCopy.Of(dll, FileStore.Machine, root);
            var again = ShadowCopy.Of(dll, FileStore.Machine, root);
            var copied = Directory.EnumerateFiles(Path.GetDirectoryName(copy)!, "*", SearchOption.AllDirectories)
                .Select(file => Path.GetRelativePath(Path.GetDirectoryName(copy)!, file).Replace('\\', '/')).Order().ToList();
            var (location, unloaded) = LoadAndUnload(copy);
            File.SetLastWriteTimeUtc(dll, DateTime.UtcNow.AddMinutes(1));
            var rebuilt = ShadowCopy.Of(dll, FileStore.Machine, root);

            Assert.Multiple(() =>
            {
                Assert.That(copied, Is.EqualTo(new[] { "Microsoft.Bcl.AsyncInterfaces.dll", "Tests.deps.json", "Tests.pdb", "de/Tests.resources.dll",
                    $"runtimes/{RuntimeInformation.RuntimeIdentifier}/native/native.so" }.Order()), "what loading needs, and nothing else");
                Assert.That(again, Is.EqualTo(copy), "an unchanged build is copied once");
                Assert.That(location, Is.EqualTo(copy), "loaded from the copy, so the build stays free to be built again");
                Assert.That(unloaded, Is.True, "its context unloads");
                Assert.That(rebuilt, Is.Not.EqualTo(copy), "a new build, a new copy");
            });
        }
        finally
        {
            Directory.Delete(work, recursive: true);
        }
    }

    [Test]
    public void TheSdk_IsFoundOnPath_WhenTheRuntimeTheRecorderRunsOnHasNone()
    {
        var files = new SimulatedFiles(ignoresCase: true);
        files.Write("/machine/dotnet/dotnet.exe", "", createNew: false, ownerOnly: false);
        files.Write("/home/me/.dotnet/dotnet.exe", "", createNew: false, ownerOnly: false);
        files.CreateFolder("/home/me/.dotnet/sdk/10.0.100", ownerOnly: false);
        var environment = new Dictionary<string, string?> { ["PATH"] = "/home/me/.dotnet;/usr/bin", ["USERPROFILE"] = "/home/me" };
        var candidates = SdkHost.Candidates("/machine/dotnet/shared/Microsoft.NETCore.App/10.0.0", name => environment.GetValueOrDefault(name), windows: true).ToList();

        var (host, looked) = SdkHost.Find(candidates, 10, files);
        var (none, lookedForNine) = SdkHost.Find(candidates, 9, files);

        Assert.Multiple(() =>
        {
            Assert.That(candidates[0], Is.EqualTo("/machine/dotnet/dotnet.exe"), "beside the runtime first");
            Assert.That(host, Is.EqualTo("/home/me/.dotnet/dotnet.exe"));
            Assert.That(looked, Is.EqualTo(new[] { "/machine/dotnet/dotnet.exe: no .NET 10 SDK in /machine/dotnet/sdk" }));
            Assert.That((none, lookedForNine.Count), Is.EqualTo(((string?)null, 2)), "none, and each host it looked at");
        });
    }

    // Loaded in a collectible context of its own, from the path given; its location, and whether it unloaded.
    private static (string Location, bool Unloaded) LoadAndUnload(string path)
    {
        var (location, context) = Load(path);
        for (var collect = 0; collect < 20 && context.IsAlive; collect++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        return (location, !context.IsAlive);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static (string Location, WeakReference Context) Load(string path)
    {
        var context = new AssemblyLoadContext("a build", isCollectible: true);
        var location = context.LoadFromAssemblyPath(path).Location;
        context.Unload();
        return (location, new WeakReference(context));
    }
}
