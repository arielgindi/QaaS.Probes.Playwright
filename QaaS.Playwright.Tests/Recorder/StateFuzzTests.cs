using System.Globalization;
using QaaS.Playwright.Browser;

namespace QaaS.Playwright.Tests.Recorder;

/// <summary>
/// The recorder's modes in every order a person may press them (<see cref="StateFuzz"/>): the sequences that broke it
/// once, each as short as it still broke, in the normal suite; and, with <c>QAAS_FUZZ=1</c>, random ones.
/// </summary>
[TestFixture]
[Category("EndToEnd")]
public class StateFuzzTests
{
    [SetUp]
    public void NeedsChrome()
    {
        if (ChromeExecutable.Find() is null) Assert.Ignore("Google Chrome is not installed.");
    }

    /// <summary>Each sequence that broke the recorder, as short as it still broke it.</summary>
    private static IEnumerable<TestCaseData> Regressions() =>
    [
        new TestCaseData("start:/helpdesk | click:New ticket | key:F8 | fill:Title=Printer jammed | key:F8 | save:ctrl-s")
            .SetName("Paused_RecordsNothing_AndRecordsAgainOnResume"),
        // F4 in check or inspect mode said "Elements frozen" in the pill for good, in place of the mode's.
        new TestCaseData("start:/helpdesk | key:F6 | key:F4 | key:Control+Shift+C | key:F4 | save:enter")
            .SetName("AKeyInCheckOrInspectMode_GivesTheModesPillBack"),
        // The status strip said "Re-recording: do it again, then press Done" while the part was paused, tried or checked.
        new TestCaseData("start:/helpdesk | click:New ticket | fill:Title=Printer jammed | click:Create ticket | edit:rerecord@1 | wait | " +
                         "key:F7 | key:F7 | key:F8 | key:F8 | key:F6 | key:F6 | key:F2 | save:enter")
            .SetName("PausedTriedOrCheckedInAPartRecordedAgain_TheStripSaysSo"),
    ];

    [TestCaseSource(nameof(Regressions))]
    public async Task Sequence_KeepsEveryCheck(string sequence)
    {
        var failure = await StateFuzz.RunAsync(sequence);
        Assert.That(failure, Is.Null, failure?.ToString());
    }

    /// <summary>
    /// Random sequences, only with <c>QAAS_FUZZ=1</c>: <c>QAAS_FUZZ_SEEDS</c> of them (200), from <c>QAAS_FUZZ_FIRST</c>
    /// (1), each of 10 to 40 steps; or the one <c>QAAS_FUZZ_SEQUENCE</c> gives. Each that breaks is made as short as it
    /// still breaks the same way; the report goes to <c>QAAS_FUZZ_REPORT</c>, or the temporary folder.
    /// </summary>
    [Test]
    [CancelAfter(24 * 3600 * 1000)]
    public async Task RandomSequences_KeepEveryCheck()
    {
        if (Environment.GetEnvironmentVariable("QAAS_FUZZ") != "1") Assert.Ignore("Set QAAS_FUZZ=1 to fuzz the recorder's modes.");
        var report = Environment.GetEnvironmentVariable("QAAS_FUZZ_REPORT") ?? Path.Combine(Path.GetTempPath(), "qaas-state-fuzz.txt");
        var console = Console.Out;
        Console.SetOut(TextWriter.Synchronized(new StreamWriter(report + ".console.txt") { AutoFlush = true }));
        try
        {
            if (Environment.GetEnvironmentVariable("QAAS_FUZZ_SEQUENCE") is { Length: > 0 } given)
            {
                var once = await StateFuzz.RunAsync(given);
                await File.WriteAllTextAsync(report, once?.ToString() ?? "passed\n");
                Assert.That(once, Is.Null, once?.ToString());
                return;
            }

            var seeds = Number("QAAS_FUZZ_SEEDS", 200);
            var first = Number("QAAS_FUZZ_FIRST", 1);
            var workers = Number("QAAS_FUZZ_WORKERS", 1);
            var failures = new List<string>();
            var writing = new SemaphoreSlim(1, 1);
            async Task Say(string text)
            {
                await writing.WaitAsync();
                try { await File.AppendAllTextAsync(report, text); }
                finally { writing.Release(); }
            }

            await File.WriteAllTextAsync(report, $"Fuzzing seeds {first} to {first + seeds - 1}, {workers} at a time\n");
            await Task.WhenAll(Enumerable.Range(0, workers).Select(worker => Task.Run(async () =>
            {
                for (var seed = first + worker; seed < first + seeds; seed += workers)
                {
                    var length = 10 + new Random(seed * 7919).Next(31);
                    var started = DateTime.UtcNow;
                    var failure = await StateFuzz.FuzzAsync(seed, length);
                    var took = (DateTime.UtcNow - started).TotalSeconds;
                    if (failure is null)
                    {
                        await Say($"seed {seed}: passed, {length} steps, {took:0} s\n");
                        continue;
                    }

                    await Say($"seed {seed}: FAILED after {took:0} s\n{failure}\n");
                    var shortest = Environment.GetEnvironmentVariable("QAAS_FUZZ_MINIMIZE") == "0" ? failure : await MinimizeAsync(failure, seed);
                    lock (failures) failures.Add($"seed {seed}: {shortest.Kind}\n  {shortest.Sequence}");
                    await Say($"seed {seed} shortest:\n{shortest}\n\n");
                }
            })));

            await Say($"\n{failures.Count} of {seeds} failed\n{string.Join("\n", failures)}\n");
            Assert.That(failures, Is.Empty, string.Join("\n", failures));
        }
        finally
        {
            Console.SetOut(console);
        }
    }

    // The shortest sequence that breaks the same way, found by leaving out ever smaller runs of steps.
    private static async Task<StateFuzz.Failure> MinimizeAsync(StateFuzz.Failure failure, int seed)
    {
        var shortest = failure;
        var steps = failure.Steps.ToList();
        var tries = 0;
        for (var chunk = Math.Max(1, (steps.Count - 1) / 2); chunk >= 1 && tries < 60; chunk /= 2)
        {
            for (var at = 1; at < steps.Count && tries < 60;)
            {
                var fewer = steps.Take(at).Concat(steps.Skip(at + chunk)).ToList();
                tries++;
                var again = await StateFuzz.RunAsync(string.Join(" | ", fewer), seed);
                if (again?.Kind == failure.Kind)
                {
                    (steps, shortest) = (again.Steps.ToList(), again);
                    continue;
                }

                at += chunk;
            }
        }

        return shortest;
    }

    private static int Number(string name, int otherwise) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            ? number
            : otherwise;
}
