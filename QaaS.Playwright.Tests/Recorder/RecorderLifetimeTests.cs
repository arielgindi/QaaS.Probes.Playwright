using System.Diagnostics;
using QaaS.Playwright.Recorder;

namespace QaaS.Playwright.Tests.Recorder;

/// <summary>
/// How a recording ends, whatever ends it: one table of reasons, one ordered shutdown, every step bounded, a second
/// Ctrl+C or signal forcing it, and the child processes never outliving it.
/// </summary>
[TestFixture]
public class RecorderLifetimeTests
{
    private static readonly TimeSpan Step = TimeSpan.FromMilliseconds(150);

    [Test]
    public async Task EveryReason_DrainsSnapshotsThenSavesOrKeeps_DisposesNewestFirst_AndEndsWithItsCode()
    {
        foreach (var reason in Enum.GetValues<StopReason>())
        {
            var (lifetime, steps, logged) = Lifetime();
            lifetime.Listening = true;

            lifetime.RequestStop(reason);
            var exit = await lifetime.ShutdownAsync(_ => { steps.Add("save"); return Task.FromResult(7); });

            var plan = RecorderLifetime.PlanFor(reason);
            Assert.Multiple(() =>
            {
                Assert.That(steps, Is.EqualTo(plan.Save
                    ? new[] { "drain", "snapshot", "save", "panel", "session" }
                    : new[] { "drain", "snapshot", "panel", "session" }), $"{reason}: in order, newest scope first");
                Assert.That(exit, Is.EqualTo(plan.Save ? 7 : plan.ExitCode), reason.ToString());
                Assert.That(logged, plan.Logged ? Is.Not.Empty : Is.Empty, reason.ToString());
                Assert.That(lifetime.Ending.IsCancellationRequested, $"{reason}: the runs it owns are told to stop");
            });
        }
    }

    [Test]
    public async Task CtrlC_BeforeTheRecordingListens_OrAgainAsItEnds_StopsAtOnce_TheRecordingKept()
    {
        // Before: logging in, replaying, a question in the console.
        var (early, earlySteps, _) = Lifetime();
        early.RequestStop(StopReason.CtrlC);
        var earlyExit = await early.ShutdownAsync(_ => Task.FromResult(0));

        // Again, while a save goes on that gives way, and one that does not, which the watchdog ends.
        var exits = new List<int>();
        var (later, laterSteps, _) = Lifetime(exits.Add);
        later.Listening = true;
        later.RequestStop(StopReason.CtrlC);
        var saving = new TaskCompletionSource();
        var shutdown = later.ShutdownAsync(async token => { saving.SetResult(); await Task.Delay(Timeout.Infinite, token); return 0; });
        await saving.Task;
        var forced = later.RequestStop(StopReason.CtrlC);
        var laterExit = await shutdown;

        var (stuck, _, _) = Lifetime(exits.Add);
        stuck.Listening = true;
        stuck.RequestStop(StopReason.Enter);
        var stuckSaving = new TaskCompletionSource();
        _ = stuck.ShutdownAsync(_ => { stuckSaving.SetResult(); return new TaskCompletionSource<int>().Task; });
        await stuckSaving.Task;
        stuck.RequestStop(StopReason.Terminated);
        for (var wait = 0; wait < 50 && !exits.Contains(143); wait++) await Task.Delay(20);

        Assert.Multiple(() =>
        {
            Assert.That((earlyExit, earlySteps.Contains("save")), Is.EqualTo((130, false)));
            Assert.That((forced, laterExit), Is.EqualTo((RecorderLifetime.Outcome.Forced, 130)));
            Assert.That(laterSteps, Does.Contain("snapshot").And.Contain("session"), "kept, and disposed all the same");
            Assert.That(exits, Is.EqualTo(new[] { 143 }), "a save that never gives way is ended with the signal's code");
            Assert.That(later.RequestStop(StopReason.Enter), Is.EqualTo(RecorderLifetime.Outcome.Ignored));
        });
    }

    [Test]
    public async Task AStepThatThrowsOrHangs_IsLoggedAndPassed_AndAChildProcess_IsKilled()
    {
        var (lifetime, steps, logged) = Lifetime();
        lifetime.Listening = true;
        lifetime.OnDrain(_ => new TaskCompletionSource().Task);
        lifetime.Own("broken", () => throw new InvalidOperationException("broken"));
        using var child = Process.Start(OperatingSystem.IsWindows()
            ? new ProcessStartInfo("ping", "-n 60 127.0.0.1") { CreateNoWindow = true }
            : new ProcessStartInfo("sleep", "60"))!;
        using var owned = lifetime.Children.Own(child);

        lifetime.RequestStop(StopReason.Enter);
        var exit = await lifetime.ShutdownAsync(_ => Task.FromResult(0));
        child.WaitForExit(5000);

        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.Zero);
            Assert.That(steps, Is.EqualTo(new[] { "snapshot", "panel", "session" }), "the drain that hung is passed");
            Assert.That(logged, Has.Some.Contains("broken"));
            Assert.That(child.HasExited, "the child outlived the recorder");
            Assert.That(lifetime.Children.Ids, Is.Empty);
        });
    }

    // A lifetime whose steps say themselves, in a short time per step, never ending the test's process.
    private static (RecorderLifetime Lifetime, List<string> Steps, List<string> Logged) Lifetime(Action<int>? exit = null)
    {
        var steps = new List<string>();
        var logged = new List<string>();
        var lifetime = new RecorderLifetime(exit ?? (_ => { }), (what, failure) => { lock (logged) logged.Add($"{what} {failure?.Message}"); return true; }, Step);
        lifetime.OnDrain(_ => { steps.Add("drain"); return Task.CompletedTask; });
        lifetime.OnSnapshot(_ => { steps.Add("snapshot"); return Task.FromResult(true); });
        lifetime.Own("session", () => { steps.Add("session"); return ValueTask.CompletedTask; });
        lifetime.Own("panel", () => { steps.Add("panel"); return ValueTask.CompletedTask; });
        return (lifetime, steps, logged);
    }
}
