using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using QaaS.Playwright.Recorder.Capture;
using QaaS.Playwright.Recorder.Generation;
using QaaS.Playwright.Recorder.TestRun;
using static QaaS.Playwright.Tests.Recorder.GeneratorFuzz;

namespace QaaS.Playwright.Tests.Recorder;

/// <summary>
/// The generator on recordings no person recorded as such, with no browser (<see cref="GeneratorFuzz"/>): 300 mutants
/// checked without a build in every run, and with <c>QAAS_FUZZ=1</c> the whole fuzz: <c>QAAS_FUZZ_COUNT</c> mutants
/// (5000) from <c>QAAS_FUZZ_SEED</c>, built, each failure cut down to its smallest recording in <c>QAAS_FUZZ_OUT</c>;
/// <c>QAAS_FUZZ_BUILD=0</c> leaves out the builds, and <c>QAAS_FUZZ_REPLAY</c> checks and cuts down one recording. The
/// recordings failures were cut down to stay in the suite.
/// </summary>
[TestFixture]
[Category("EndToEnd")]
public class GeneratorFuzzTests
{
    [Test]
    public async Task Fuzz_MutantsOfEveryRecording_GiveFlowsThatBuild_WithLinesThatMatch_AndYamlThatBinds()
    {
        // Without QAAS_FUZZ=1, a few hundred mutants, checked without a build, in seconds.
        var full = Environment.GetEnvironmentVariable("QAAS_FUZZ") == "1";
        // QAAS_FUZZ_REPLAY names one recording to check and cut down, as a failure's smallest.
        var replay = Environment.GetEnvironmentVariable("QAAS_FUZZ_REPLAY") is { Length: > 0 } given ? given : null;
        var count = replay is not null ? 1 : full ? Number("QAAS_FUZZ_COUNT", 5000) : 300;
        var batch = Number("QAAS_FUZZ_BATCH", 500);
        // QAAS_FUZZ_BUILD=0 checks only what needs no build, in seconds.
        var build = full && Environment.GetEnvironmentVariable("QAAS_FUZZ_BUILD") != "0";
        var random = new Random(Number("QAAS_FUZZ_SEED", 1104));
        var named = Environment.GetEnvironmentVariable("QAAS_FUZZ_OUT") is { Length: > 0 } asked ? asked : null;
        var folder = Directory.CreateTempSubdirectory("qaas-fuzz-build-").FullName;
        var seeds = Seeds().ToList();
        var failures = new Dictionary<string, List<(Mutant Mutant, string Detail)>>();
        var (rejected, built) = (0, 0);
        var building = new List<(Mutant Mutant, GeneratedFlow Flow)>();
        var batches = 0;
        try
        {
            for (var number = 0; number < count; number++)
            {
                var mutant = replay is null
                    ? Mutate(seeds[random.Next(seeds.Count)], number, random)
                    : new Mutant(number, replay, await File.ReadAllTextAsync(replay), ["as recorded"], Number("QAAS_FUZZ_AUTOCHECKS", 1) == 1)
                    {
                        ClassName = Environment.GetEnvironmentVariable("QAAS_FUZZ_CLASS") is { Length: > 0 } name ? name : $"Fuzz{number:D5}",
                    };
                if (Read(mutant.Json) is not { } recording)
                {
                    rejected++;
                    continue;
                }

                var outcome = Check(recording, mutant.ClassName, mutant.AutoChecks, mutant.Namespace);
                if (outcome.Problem is { } problem) Failed(failures, problem, mutant, outcome.Detail ?? "");
                else if (build) building.Add((mutant, outcome.Flow!));
                if (building.Count == 0 || (building.Count < batch && number < count - 1)) continue;
                foreach (var (failed, (why, detail)) in await BuildAsync(folder, building, batches++))
                    Failed(failures, why, building.Single(flow => flow.Mutant.Number == failed).Mutant, detail);
                built += building.Count;
                building.Clear();
                TestContext.Progress.WriteLine($"{number + 1} mutants, {failures.Values.Sum(list => list.Count)} failing");
            }

            // Where the smallest recordings and the report go: the folder named, else one of its own once anything failed.
            var output = named is not null ? Directory.CreateDirectory(named).FullName
                : failures.Count > 0 || full ? Directory.CreateTempSubdirectory("qaas-fuzz-").FullName
                : null;
            var report = new StringBuilder($"{count} mutants of {seeds.Count} recordings: {rejected} not a recording, {built} built, " +
                                           $"{failures.Values.Sum(list => list.Count)} failing in {failures.Count} ways.\n");
            foreach (var (problem, failed) in failures.OrderByDescending(pair => pair.Value.Count))
            {
                var (mutant, detail) = failed.OrderBy(each => each.Mutant.Json.Length).First();
                // As many tries as there is time for: thousands without a build, hundreds of the compiler's, tens of a project's.
                var budget = !problem.StartsWith("does not build", StringComparison.Ordinal) && !problem.StartsWith("its ", StringComparison.Ordinal) ? 3000
                    : ByTheCompiler(problem) ? 600 : 60;
                var smallest = await MinimiseAsync(mutant.Json, json => FailsAsync(json, mutant, problem, folder), budget);
                var file = Path.Combine(output!, $"{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(problem)))[..8]}.recording.json");
                await File.WriteAllTextAsync(file, smallest);
                var renamed = failed.Select(each => each.Mutant.ClassName).Where(name => !name.StartsWith("Fuzz", StringComparison.Ordinal)).Distinct().Order().ToList();
                report.Append($"\n{failed.Count} × {problem}{(renamed.Count > 0 ? $" (flows named {string.Join(", ", renamed)})" : "")}\n" +
                              $"  e.g. {mutant.ClassName} of {mutant.Seed}: {string.Join("; ", mutant.Changes)}\n" +
                              $"  smallest: {file}\n  {detail.Split('\n')[0]}\n");
            }

            if (output is not null) await File.WriteAllTextAsync(Path.Combine(output, "report.txt"), report.ToString());
            TestContext.Out.WriteLine(report);
            Assert.That(failures, Is.Empty, report.ToString());
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [TestCaseSource(typeof(GeneratorFuzz), nameof(Regressions))]
    public void AnUnusualRecording_GivesAFlow_ThatWritesEveryAction_OrSaysToCheckIt_InLinesOfItsCode(string name)
    {
        Assert.Multiple(() =>
        {
            foreach (var autoChecks in new[] { true, false })
            {
                var outcome = Check(Regression(name), name, autoChecks);
                Assert.That(outcome.Problem, Is.Null, $"{(autoChecks ? "with" : "without")} auto checks: {outcome.Detail}");
            }
        });
    }

    private static int _alone;

    // An error or warning of the compiler itself, not of an analyzer nor of a doc comment, which the test build tells.
    private static bool ByTheCompiler(string problem) =>
        problem.StartsWith("does not build: CS", StringComparison.Ordinal) && !problem.StartsWith("does not build: CS15", StringComparison.Ordinal);

    private static int Number(string variable, int otherwise) =>
        int.TryParse(Environment.GetEnvironmentVariable(variable), out var number) ? number : otherwise;

    private static Recording? Read(string json)
    {
        try
        {
            return Recording.FromJson(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void Failed(Dictionary<string, List<(Mutant, string)>> failures, string problem, Mutant mutant, string detail) =>
        (failures.TryGetValue(problem, out var list) ? list : failures[problem] = []).Add((mutant, detail));

    // The recording fails the same way: offline, or in a build of its flow alone.
    private static async Task<bool> FailsAsync(string json, Mutant mutant, string problem, string folder)
    {
        if (Read(json) is not { } recording) return false;
        var outcome = Check(recording, mutant.ClassName, mutant.AutoChecks, mutant.Namespace);
        if (outcome.Problem is { } offline) return offline == problem;
        if (outcome.Flow is not { } flow) return false;
        // A compiler error or warning the test build tells is found by it, much faster than a build of a project.
        if (ByTheCompiler(problem))
        {
            var code = problem["does not build: ".Length..].Split(':')[0];
            using var build = new FlowBuild();
            try
            {
                return (await build.BuildAsync(flow, CancellationToken.None)).Warnings.Any(warning => warning.Contains($": {code}:", StringComparison.Ordinal));
            }
            catch (FlowBuildException failure)
            {
                return (failure.Detail ?? failure.Message).Contains($": {code}:", StringComparison.Ordinal);
            }
        }

        // A build of its own each time: an assembly loaded is kept by its path.
        var alone = Path.Combine(folder, $"alone{++_alone}");
        var problems = await BuildAsync(alone, [(mutant, flow)], _alone);
        return problems.TryGetValue(mutant.Number, out var found) && found.Problem == problem;
    }
}
