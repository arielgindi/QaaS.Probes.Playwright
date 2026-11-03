using QaaS.Playwright.Recorder.Capture;
using QaaS.Playwright.Recorder.Generation;

namespace QaaS.Playwright.Recorder;

/// <summary>Turns a recording into a flow class, the YAML of its values, and the list of what to check by hand.</summary>
internal static class FlowCodeGenerator
{
    /// <param name="recording">What was recorded.</param>
    /// <param name="className">The flow's class name.</param>
    /// <param name="namespaceName">The flow's namespace.</param>
    /// <param name="autoChecks">Whether to check after each action what it did when recorded.</param>
    public static GeneratedFlow Generate(Recording recording, string className, string namespaceName,
        bool autoChecks = true) =>
        FlowWriter.Write(FlowPlanner.Plan(recording, className), namespaceName, autoChecks);

    /// <summary>Turns a name such as <c>add-to-cart</c> into a type name (<c>AddToCart</c>).</summary>
    /// <exception cref="ArgumentException">The name has no letter or digit.</exception>
    public static string ToPascalCase(string name)
    {
        var words = name.Split(['-', '_', ' '], StringSplitOptions.RemoveEmptyEntries)
            .Select(word => string.Concat(word.Where(char.IsLetterOrDigit)))
            .Where(word => word.Length > 0)
            .ToList();
        if (words.Count == 0) throw new ArgumentException($"Flow name '{name}' has no letters or digits.", nameof(name));

        var identifier = string.Concat(words.Select(word => char.ToUpperInvariant(word[0]) + word[1..]));
        // A type name cannot start with a digit: "2fa-login" becomes "Flow2faLogin".
        return char.IsDigit(identifier[0]) ? "Flow" + identifier : identifier;
    }
}
