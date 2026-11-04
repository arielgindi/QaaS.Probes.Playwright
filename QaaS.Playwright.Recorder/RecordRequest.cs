using QaaS.Playwright.Recorder.Login;

namespace QaaS.Playwright.Recorder;

/// <summary>
/// What to record: the flow's class name, the page to start on, and the folder to save the flow in; as the start screen
/// builds it, checked by <see cref="Setup.SetupRules"/>.
/// </summary>
internal sealed record RecordRequest(string ClassName, string Url, string OutputDir)
{
    /// <summary>Save the raw recording beside the flow, with every locator tried for each element.</summary>
    public bool KeepRecording { get; init; }

    /// <summary>Whether the flow checks what each action did when recorded (Auto checks, on the start screen).</summary>
    public bool AutoChecks { get; init; } = true;

    /// <summary>A flow of the person's tests that logs in before the recording starts; null to start at once.</summary>
    public LoginFlow? Login { get; init; }

    /// <summary>The login flow is the one last used in this folder, not chosen now.</summary>
    public bool LoginRemembered { get; init; }

    /// <summary>The person turned Log in first off: none is looked for.</summary>
    public bool NoLogin { get; init; }

    /// <summary>Goes on from the flow's recording: its steps replay first, and what is recorded now comes after them.</summary>
    public bool Continue { get; init; }

    /// <summary>
    /// The flow file saving replaces, by its <see cref="Autosave.Hash"/>: the flow continued, or the one the person chose
    /// to replace. When the file changed since, or this is null, saving keeps it, as it keeps any other.
    /// </summary>
    public string? Replaces { get; init; }

    /// <summary>The flow's class file.</summary>
    public string CodePath => Path.Combine(OutputDir, $"{ClassName}.cs");

    /// <summary>
    /// The same request for a flow named <paramref name="name"/>, checked as a new name is. It replaces no flow file.
    /// </summary>
    /// <exception cref="ArgumentException">The flow cannot have that name.</exception>
    public RecordRequest Renamed(string name) => this with { ClassName = Setup.SetupRules.ClassName(name), Replaces = null };
}
