using QaaS.Playwright.Recorder;
using QaaS.Playwright.Recorder.Capture;

namespace QaaS.Playwright.Tests.Recorder;

/// <summary>What the recorder prints for a step, with text from a page it cannot trust to be short or plain.</summary>
[TestFixture]
public class ConsoleUiTests
{
    [Test]
    public void StepLine_FitsTheTerminal_AndPrintsNoEscapeSequence()
    {
        var terminal = Terminal.Fixed(TextWriter.Null, width: 60);
        var action = new CapturedAction
        {
            Id = "a1", Kind = "fill", Value = "hello\u001b[31m",
            Target = new CapturedTarget
            {
                Tag = "input", Text = "Surname",
                Candidates =
                [
                    new CapturedCandidate
                    {
                        Kind = "name", Value = "employee.address.firstName", Count = 1, Matches = true,
                        Parts = [new LocatorPart { M = "css", Css = "[name='employee.address.firstName']" }],
                    },
                ],
            },
        };

        var line = ConsoleUi.StepLine(terminal, 3, action)!;

        Assert.Multiple(() =>
        {
            Assert.That(Terminal.WidthOf(line), Is.EqualTo(59), line);
            Assert.That(line.Contains("Surname = hello [31m", StringComparison.Ordinal), line);
            Assert.That(line.Contains('\u001b'), Is.False, "a page's escape sequence reached the terminal");
            Assert.That(line, Does.StartWith("    3 ≡ fill    ").And.EndWith("[name='employee.add…"));
        });
    }

    [Test]
    public void Pausing_IsSaid_AfterALoginThatFailed_Too()
    {
        var said = Said(() =>
        {
            ConsoleUi.StateChanged(RecordingState.Recording, say: false);
            // The login flow failed: the recording pauses, which the login's own lines tell. Then F8 records.
            ConsoleUi.StateChanged(RecordingState.Paused, say: false);
            ConsoleUi.StateChanged(RecordingState.Recording);
            ConsoleUi.StateChanged(RecordingState.Paused);
            ConsoleUi.NotRecordedWhilePaused("click");
        });

        Assert.That(said, Does.Contain("Recording").And.Contain("again, from the page the app is on")
            .And.Contain("Paused").And.Contain("nothing you do is recorded; F8 resumes")
            .And.Contain("Paused: that click was not recorded (F8 resumes)"));
    }

    [Test]
    public void CtrlC_WhileRecording_EndsItAsSaveDoes_AndAgainStopsAtOnce()
    {
        var lifetime = new RecorderLifetime(exit: _ => { }, log: (_, _) => true) { Listening = true };

        var said = Said(() => ConsoleUi.CtrlC(lifetime));
        var again = Said(() => ConsoleUi.CtrlC(lifetime));

        Assert.Multiple(() =>
        {
            Assert.That(lifetime.Stopped.IsCompletedSuccessfully && lifetime.Stopped.Result == StopReason.CtrlC, "the recording ends as on Save");
            Assert.That(said, Does.Contain("Ctrl+C: saving what you recorded, as Save does"));
            Assert.That(again, Does.Contain("at once: what is not saved yet is kept"));
        });
    }

    // What the console printed meanwhile.
    private static string Said(Action print)
    {
        var before = Console.Out;
        using var output = new StringWriter();
        Console.SetOut(output);
        try
        {
            print();
        }
        finally
        {
            Console.SetOut(before);
        }

        return output.ToString();
    }
}
