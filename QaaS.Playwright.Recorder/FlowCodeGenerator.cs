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
        Generate(RecordingReader.Read(recording), className, namespaceName, autoChecks);

    /// <inheritdoc cref="Generate(Recording, string, string, bool)"/>
    public static GeneratedFlow Generate(ValidRecording recording, string className, string namespaceName,
        bool autoChecks = true) =>
        PlanAndWrite(recording, className, namespaceName, autoChecks).Flow;

    /// <summary>What to check by hand starts with this for an action the planner could not plan, which it left out.</summary>
    public const string NotPlanned = "could not plan action";

    /// <summary>
    /// The plan and the flow written from it, whatever one action does: an action the planner or the writer throws on,
    /// as a whole, is found, left out and said to redo by hand (<see cref="NotPlanned"/>), and the rest is planned and
    /// written. Each action the writer cannot write alone is left out by the writer itself.
    /// </summary>
    public static (FlowPlan Plan, GeneratedFlow Flow) PlanAndWrite(Recording recording, string className, string namespaceName,
        bool autoChecks = true) =>
        PlanAndWrite(RecordingReader.Read(recording), className, namespaceName, autoChecks);

    /// <inheritdoc cref="PlanAndWrite(Recording, string, string, bool)"/>
    /// <param name="recording">What was recorded, as the reader read it.</param>
    /// <param name="className">The flow's class name.</param>
    /// <param name="namespaceName">The flow's namespace.</param>
    /// <param name="autoChecks">Whether to check after each action what it did when recorded.</param>
    /// <param name="planner">Plans a recording; <see cref="FlowPlanner.Plan(ValidRecording, string)"/> unless a test says otherwise.</param>
    public static (FlowPlan Plan, GeneratedFlow Flow) PlanAndWrite(ValidRecording recording, string className, string namespaceName,
        bool autoChecks = true, Func<ValidRecording, string, FlowPlan>? planner = null)
    {
        planner ??= FlowPlanner.Plan;
        return Isolated(recording, valid =>
        {
            var plan = planner(valid, className);
            return (plan, FlowWriter.Write(plan, namespaceName, autoChecks));
        }, (written, left) => written.plan.NotRecorded.AddRange(left));
    }

    /// <summary>The plan alone, as the console numbers the steps, whatever one action does to the planner.</summary>
    public static FlowPlan PlanIsolated(Recording recording, string className) =>
        Isolated(RecordingReader.Read(recording), valid => FlowPlanner.Plan(valid, className), (plan, left) => plan.NotRecorded.AddRange(left));

    // Makes what the recording gives, without the actions it fails on, each found as the first whose recording up to it
    // fails, and said; then tells the result what was left out.
    private static T Isolated<T>(ValidRecording recording, Func<ValidRecording, T> make, Action<T, IReadOnlyList<string>> tell)
    {
        var left = new List<string>();
        for (var valid = recording; ;)
        {
            try
            {
                var made = make(valid);
                tell(made, left);
                return made;
            }
            catch (Exception failure) when (failure is not OutOfMemoryException && valid.Actions.Count > 0)
            {
                var at = FirstFailing(valid, make);
                var action = valid.Actions[at];
                left.Add($"{NotPlanned} {action.Id} (a '{action.Kind}'), as {failure.GetType().Name}: {failure.Message} Redo it by hand.");
                valid = ValidRecording.Of(valid.Recording with { Actions = [.. valid.Actions.Where((_, index) => index != at)] }, valid.Unrecorded);
            }
        }
    }

    // The index of the action that makes the recording up to it fail, by halves: the whole recording fails, none does.
    private static int FirstFailing<T>(ValidRecording valid, Func<ValidRecording, T> make)
    {
        bool Fails(int count)
        {
            try
            {
                make(ValidRecording.Of(valid.Recording with { Actions = [.. valid.Actions.Take(count)] }, []));
                return false;
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                return true;
            }
        }

        var (low, high) = (1, valid.Actions.Count);
        while (low < high)
        {
            var middle = (low + high) / 2;
            if (Fails(middle)) high = middle;
            else low = middle + 1;
        }

        return low - 1;
    }

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
