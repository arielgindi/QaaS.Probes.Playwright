using QaaS.Playwright.Recorder;
using QaaS.Playwright.Recorder.Capture;

namespace QaaS.Playwright.Tests.Recorder;

/// <summary>What the recorder prints for a step, with text from a page it cannot trust to be short, plain or left-to-right.</summary>
[TestFixture]
public class ConsoleUiTests
{
    [Test]
    public void StepLine_FitsTheTerminal_KeepsRightToLeftTextInPlace_AndPrintsNoEscapeSequence()
    {
        var terminal = Terminal.Fixed(TextWriter.Null, width: 60);
        var action = new CapturedAction
        {
            Id = "a1", Kind = "fill", Value = "مرحبا\u001b[31m",
            Target = new CapturedTarget
            {
                Tag = "input", Text = "اسم أول",
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
            // Each in an isolate of its own, so a terminal that lays out bidirectional text keeps the name first.
            Assert.That(line.Contains("\u2068اسم أول\u2069 = \u2068مرحبا [31m\u2069", StringComparison.Ordinal), line);
            Assert.That(line.Contains('\u001b'), Is.False, "a page's escape sequence reached the terminal");
            Assert.That(line, Does.StartWith("    3 ≡ fill    ").And.EndWith("[name='employee.add…"));
        });
    }
}
