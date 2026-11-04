namespace QaaS.Playwright.Recorder.Generation;

/// <summary>
/// The names a scope of the flow's code has, RunAsync's or a step's: the variables declared in it, and the names it
/// must not declare, as a dialog's step has no <c>page</c> but its page. A new variable gets a name of its own; a
/// variable given a value again, as a tab of a name opened before, is assigned, never declared twice.
/// </summary>
internal sealed class Symbols
{
    private readonly HashSet<string> _declared;
    private readonly HashSet<string> _taken;

    public Symbols(IEnumerable<string> declared, IEnumerable<string>? reserved = null)
    {
        _declared = new HashSet<string>(declared, StringComparer.Ordinal);
        _taken = new HashSet<string>(_declared.Concat(reserved ?? []), StringComparer.Ordinal);
    }

    public bool IsDeclared(string name) => _declared.Contains(name);

    public bool IsTaken(string name) => _taken.Contains(name);

    /// <summary>A variable of its own, named <paramref name="name"/>, else with the first number from 2 that is free.</summary>
    public string New(string name)
    {
        var unique = name;
        for (var copy = 2; !_taken.Add(unique); copy++) unique = $"{name}{copy}";
        _declared.Add(unique);
        return unique;
    }

    /// <summary>How a statement gives the variable its value: <c>var name</c> the first time, <c>name</c> after.</summary>
    public string Assign(string name)
    {
        _taken.Add(name);
        return _declared.Add(name) ? $"var {name}" : name;
    }
}
