using QaaS.Playwright.Recorder.Capture;

namespace QaaS.Playwright.Recorder.Generation;

/// <summary>
/// Names each step after what a person does in it: <c>OpenBookAppointmentAsync</c>, <c>FillBookAppointmentAsync</c>,
/// <c>SaveChangesAsync</c>. A dialog is named by its title when that is English, else by the id of the button that
/// opened it, else by the flow's own name.
/// </summary>
internal static class StepNamer
{
    private static readonly string[] ElementSuffixes = ["Button", "Btn", "Link", "MenuItem", "Item", "Tab"];

    public static void Name(FlowPlan plan)
    {
        var subjects = new Dictionary<string, string>();
        var used = new HashSet<string>(StringComparer.Ordinal) { "RunAsync", "FileToUpload" };
        var flowNameTaken = false;

        string DialogSubject(string title, CapturedDialog? info)
        {
            if (subjects.TryGetValue(title, out var known)) return known;
            var titleWords = info?.Prefix is { Length: > 0 } prefix ? prefix : title;
            var subject = (CSharp.IsAsciiText(titleWords) ? CSharp.Pascal(titleWords) : null) ?? OpenerStem(plan, title);
            if (subject is null && !flowNameTaken)
            {
                subject = plan.ClassName;
                flowNameTaken = true;
            }

            subject ??= $"Dialog{subjects.Count + 1}";
            subjects[title] = subject;
            return subject;
        }

        for (var index = 0; index < plan.Steps.Count; index++)
        {
            var step = plan.Steps[index];
            var last = step.Actions[^1];
            var verb = Verb(last);
            var hasValues = step.Actions.Any(action => action.Value is not null || action.Kind == "upload");
            string name;
            if (step.Opens is { } opened)
                name = "Open" + DialogSubject(opened.Title, opened);
            else if (step.OpensPage is not null)
                name = "Open" + (verb ?? "Page");
            else if (step.Dialog is { } title)
            {
                var subject = DialogSubject(title, last.Source.Dialog);
                name = hasValues ? "Fill" + subject
                    : last.Has("dialogClosed") ? verb ?? "Submit" + subject
                    : verb ?? "Use" + subject;
            }
            else
            {
                var subject = PageSubject(last.Source.Url) ?? plan.ClassName;
                name = hasValues ? "Fill" + subject
                    : last.Has("urlChanged") ? verb ?? "Leave" + subject
                    : verb ?? "Use" + subject;
            }

            var unique = name + "Async";
            for (var copy = 2; !used.Add(unique); copy++) unique = $"{name}{copy}Async";
            step.Name = unique;
        }
    }

    // What the clicked element says, when it is English: "Save changes" gives SaveChanges.
    private static string? Verb(PlannedAction action)
    {
        if (action.Kind is not ("click" or "dblclick" or "press")) return null;
        var text = action.Kind == "press" ? null : action.Source.Target?.Text;
        return text is { Length: <= 40 } && CSharp.IsAsciiText(text) && !text.Any(char.IsDigit) ? CSharp.Pascal(text) : null;
    }

    // The button that opened the dialog, by its id or test id: "bookAppointmentButton" gives BookAppointment.
    private static string? OpenerStem(FlowPlan plan, string title)
    {
        var opener = plan.Steps.SelectMany(step => step.Actions).FirstOrDefault(action =>
            action.Effects.Any(effect => effect.Kind == "dialogOpened" && effect.Dialog?.Title == title));
        var target = opener?.Source.Target;
        var id = new[] { target?.Id, target?.TestId }.FirstOrDefault(value => !UnstableId.IsUnstable(value));
        var stem = CSharp.Pascal(id);
        if (stem is null) return null;
        foreach (var suffix in ElementSuffixes)
            if (stem.Length > suffix.Length && stem.EndsWith(suffix, StringComparison.Ordinal))
                return stem[..^suffix.Length];
        return stem;
    }

    // The page's last path segment that is no record's id: "/vehicles/VH-1001/notes" gives Notes.
    private static string? PageSubject(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        var segment = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault(part => !part.Any(char.IsDigit) && CSharp.IsAsciiText(part));
        return CSharp.Pascal(segment);
    }
}
