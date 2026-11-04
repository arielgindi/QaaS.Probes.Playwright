using QaaS.Playwright.Recorder.State;

namespace QaaS.Playwright.Recorder.Setup;

// What the window's start screen shows, as JSON (Panel/ui/src/setup/types.ts): each field as the person set it, what is
// wrong with it, what was found, and what Start will do; or, once a flow is saved, what was saved.

internal sealed record SetupView(
    string Screen, ModeView Mode, string Version, ProjectView? Project, IReadOnlyList<RecentView> Recent, DraftView Draft,
    NameView? Name, string? FixUrl, IReadOnlyList<UrlView> Urls, IReadOnlyList<FlowView> Flows, IReadOnlyList<RecoveredView> Recovered,
    LoginView Login, IReadOnlyDictionary<string, string> Problems, string WillDo, string StartLabel, bool CanStart, bool Starting,
    string? Notice, IReadOnlyList<WhyLine>? Why, BrowseView? Browse, DoneView? Done);

internal sealed record ProjectView(string Folder, string Name, string Found, string? Namespace, bool Gone);

internal sealed record RecentView(string Folder, string Name, string When, bool Gone);

internal sealed record DraftView(string Tab, string Name, string Url, string IfExists, string? ContinueFlow, string? Recovered, bool LogIn,
    bool AutoChecks, bool KeepRecording, string OutputDir);

internal sealed record NameView(string ClassName, string Target);

internal sealed record UrlView(string Url, string From);

internal sealed record FlowView(string Name, string Detail, bool Kept, string? Why);

internal sealed record RecoveredView(string Path, string Flow, string Detail, bool Here);

internal sealed record LoginView(string State, string? Flow, string? Detail, string? BaseUrl, IReadOnlyList<LoginChoiceView> Flows,
    IReadOnlyList<LoginProjectView> Projects, string? Settings, IReadOnlyList<LoginProjectView> SettingsFiles, IReadOnlyList<LoginValueView> Values,
    string? Stale, string? BuildCommand, bool Building, string? BuildLog, IReadOnlyList<string> Problems, string? None);

internal sealed record LoginChoiceView(string Name, string Detail);

internal sealed record LoginProjectView(string File, string Name);

/// <summary>A value of the login flow: where it comes from, whether it is missing or a secret; never a secret's value.</summary>
internal sealed record LoginValueView(string Name, string From, bool Missing, bool Secret, string Value);

internal sealed record WhyLine(int Depth, string Text);

internal sealed record BrowseView(string Path, string? Parent, string Mode, IReadOnlyList<BrowseEntry> Entries, IReadOnlyList<BrowsePlace> Places,
    string? Note, string? Error);

internal sealed record BrowseEntry(string Name, string Path, bool Folder, string? Detail);

internal sealed record BrowsePlace(string Name, string Path);

internal sealed record DoneView(bool Saved, string Flow, string Summary, IReadOnlyList<DoneFile> Files, IReadOnlyList<string> Notes,
    IReadOnlyList<string> Checks, string? Error);

internal sealed record DoneFile(string Path, string What);
