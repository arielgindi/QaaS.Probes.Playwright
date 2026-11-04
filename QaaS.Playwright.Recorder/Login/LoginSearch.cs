namespace QaaS.Playwright.Recorder.Login;

/// <summary>
/// What the search for the login flow looked at, step by step, for the start screen's Why sheet: the projects found, the build
/// used, the DLLs scanned, the flows found and why each was chosen or not. <see cref="Why"/> is the first reason it
/// found none, said in one line.
/// </summary>
internal sealed class LoginSearch
{
    private readonly List<(int Depth, string Text)> _lines = [];

    /// <summary>Each line said, and how far in it goes, from 0.</summary>
    public IReadOnlyList<(int Depth, string Text)> Lines => _lines;

    /// <summary>Why no login flow was found, e.g. "no test project in /repo, above it or 3 folders below it"; null when one was.</summary>
    public string? Why { get; private set; }

    public void Say(string text, int depth = 0) => _lines.Add((depth, text));

    /// <summary>Says why none was found, and keeps the first reason as <see cref="Why"/>.</summary>
    public void None(string why, int depth = 1)
    {
        Why ??= why;
        Say($"No login flow: {why}.", depth);
    }
}
