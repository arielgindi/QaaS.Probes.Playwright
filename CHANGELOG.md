# Changelog

Notable changes, newest first. Versions follow [Semantic Versioning](https://semver.org/).

## Unreleased

- The panel's **Elements** view works as a browser's developer tools do. `Ctrl+Shift+C`, in the app or the panel,
  picks an element in the app: a box names what is under the pointer, the tree opens to it, and a click picks it
  without clicking it or recording anything, a menu that closes on blur still open. Pointing at a row boxes its element
  in the app and scrolls to it. Rows read as the element does: its role (`tr` is a `row`), accessible name and text,
  dimmed, and for a wrapper the start of the text inside it. Search finds by text, id, CSS or `role=button[name="Save"]`,
  picks the first match, steps with Enter and Shift+Enter, and boxes every match in the app. **Interactive only** leaves
  out the wrappers a person does not act on, a `⋯` where they were. A dialog, menu or list that opens is named above the
  tree, to go to it. The keys move through the tree, Enter makes the step marked in the inspector, and typing searches.
  Each step the element can become, **Use for**, **Add click**, **Add fill** and the new **Add check**, shows the lines
  it writes and how many elements its locator finds before you make it. A step added goes where the next action goes,
  so also into a part recorded again. The tree draws only the rows in view, and shows shadow roots and same-origin
  frames.

- **Re-record** a step or an action in the panel, or **Record after this**: the recorded window goes back to the start
  of the flow and replays it up to there, as **Test** runs it, nothing recorded meanwhile; then what you do goes in
  place of the part, or after it, and the steps after it stay. `F2` (**Done**) keeps it, and `Esc` in the panel keeps
  it as it was; Undo brings the old part back. A replay that does not get there says where, as a test does, and offers
  to record again from the step that failed. A loop is recorded again whole. It works while recording and with
  `--continue`, and is kept in the recording.
- **Try**, `F7` in the app or **Try** in the panel: nothing is recorded, as when paused, but each action shows in the
  panel as a dimmed card of what the recorder would write: its locator and why the others lost, every locator with what
  it found, the auto checks after it, and the setting a value would be. **Add to flow** adds one where the next action
  goes. `F7` no longer turns on Chrome's caret browsing in the recorded window.
- The recorder finds your tests' login flow by itself, so `--login LoginFlow --login-assembly <dll>` is no longer
  needed: in your test project (the nearest `.csproj`), the flow its YAML runs in `SetupFlows` most, else one named like
  a login, in the project's build or a DLL beside it, as a shared package's flow is, with its values from the
  `FlowConfiguration` of a test that runs it. It says where the flow is (`LoginFlow, from
  bin/Debug/net10.0/YourShared.dll`), whether it needs values, and for those set nowhere, the YAML to add and where. It
  asks once per folder and remembers the answer; later runs only say what it logs in with. A project not built yet, or
  changed since, is offered a `dotnet build`. `--login <Flow>` alone finds the flow's DLL and values; `--no-login` forgets
  the answer.
- A login flow is hard to miss while it runs: the console, the page and the panel show how long it has run, the page it
  is on, and what it did last. Esc, in the console or the page, or **Log in by hand** in the panel, stops it, for you
  to log in by hand, as when it fails. It stops by itself after 2 minutes of nothing, and when the app asks the browser
  to sign in (with Windows, or HTTP authentication), which a flow cannot answer.
- `--login-assembly` with the test project's own DLL finds a login flow of a package it references, and a YAML of no
  `FlowConfiguration` (`{}`) gives a flow of no settings its defaults instead of being refused.
- **Test** starts far sooner: the flow is compiled by the .NET SDK's own C# compiler, with the references, options and
  implicit usings `dotnet build` gives it, through the compiler's server, without starting MSBuild, which took up to
  15 s where an antivirus scans each DLL it loads. The recorder compiles once in the background, at a low priority, as
  the panel opens, so the first test is as fast as the next. Where that compiler or the reference assemblies are not
  found, or it fails for another reason than the flow's code, it builds with `dotnet build` as before.
- Until the first step runs, the panel's test bar says what is going on, with its time so far: building the flow,
  opening the browser window, opening the start page, logging in. The console prints how long each part took after
  each test, e.g. `build 0.2 s · window 0.6 s · start page 1.1 s · run 4.3 s`.

## 2.0.1

- Fixed: the recorder stopped with a NullReferenceException writing a flow whose first step is in a dialog no recorded
  step opened, such as one open when the recording started, or after deleting the click that opened it; and then at
  every start, offering that recording.
- A recording the recorder cannot write no longer stops it: it is still offered when the recorder starts, and saving it
  says why and keeps it.

## 2.0.0

- Checks you choose: `F6` in the recorded page, or **Check** in the panel, turns on check mode, where a click picks an
  element instead of reaching the app, and a chooser beside it offers the checks that fit, the likeliest chosen: its
  text, a field's value, a checkbox's state, how many items there are, a table row by its cells, visible or gone,
  enabled or disabled, or the page's address. Enter adds it after the last action, as an `Expect` marked `// check`,
  which `--no-auto-checks` keeps. A value typed before reads its setting; a new record's number is matched as a
  message's is.
- The auto checks under control: a switch in the panel turns them all off or on, live, as `--no-auto-checks`, and each
  one's ✕ on its action's card drops it; a dropped check stays dropped, kept in the recording.
- The steps can be edited in the panel: delete an action (Delete), rename a step's method, change a setting's name or
  default, have a value typed unique per run, or type a date as days from today (`DueInDays: 3`), which the panel
  suggests for a date typed near the day of the recording. Undo takes back each change in turn. The edits and checks
  are part of the recording, so `--keep-recording`, autosave and `--continue` keep them.
- A later locator that shows a value typed before, such as a link to the ticket just named, reads its setting, as text
  and checks do.
- `BasePlaywrightFlow` has a protected `Unique(value)`: the value with the run's stamp appended (`Printer offline
  1103-142501-k7q`), the same for the whole run. Flows that mark a value unique need this version of QaaS.Playwright.
- The recorder never loses a recording: it writes it to `~/.qaas/recordings/` after every change, atomically and for
  the user's eyes only, one file per flow and folder, without the passwords and secrets it hides elsewhere. A recording
  that did not end in a save is offered when the recorder starts next: save it now without a browser, continue
  recording, or discard it. A file it cannot read is skipped with a warning.
- `qaas-playwright --continue <flow or .cs>` goes on with a saved flow: Chrome opens where it started, the login flow
  runs, the flow's steps replay in the recorded window as a test runs them, nothing recorded, and what is recorded
  then comes after them. When a step fails, keep recording from there or stop. Saving regenerates the flow and replaces
  it, unless it was changed by hand since. Asked for a name that is there already, the recorder offers to continue that
  flow, overwrite it, or take another name.
- The recorder writes cleaner flows. Each step method has a one-line summary of what it does, and is named after what
  was clicked (`AssignTicketAsync` for a row's "Assign" button); a step ends where the page answered a click with a
  message or a busy indicator, and a search or a login of one or two values stays one step with its click. A locator
  used twice, or a row two elements are in, is a local; long statements break between calls at 120 columns; the class
  comment says where it was recorded and how to run it.
- Actions repeated three times or more, differing only in the values typed or chosen, become a loop over a list setting
  (`foreach (var task in Configuration.NewTasks)`), a list of records for repeats of several values, or a count for
  repeats of none. Anything else that differs leaves them as recorded.
- A busy indicator that came up after an action and went away (`role="progressbar"`, as MUI's progress components are,
  or `aria-busy="true"`) is waited for after it, without a sleep.
- Checks are more precise: each is marked `// auto check`; an address is not checked when a dialog, a tab or a closed
  dialog with a message says the same; a message with a new record's number in it is found by the words around it.
  `--no-auto-checks` writes a flow with none, keeping the waits.
- Settings read better: a choice drops its field's `Id` (`MaintenanceType`), settings of one name are told apart by
  what they are for (`TicketToAssign`, `TicketToClose`), number fields are `int` or `decimal`, and a long record is
  grouped by step.
- The recorder's panel tests the flow recorded so far: **Test** (`Ctrl+Enter` or `F5` in the panel) builds the code
  exactly as it will be saved, with the .NET SDK and without restoring anything, and runs it as the probe does, in a
  browser context and window of its own in the recorder's Chrome: from the page the recording started on, after the
  recording's login flow, with the recorded values as its settings, the probe's timeouts, failure screenshot and
  hidden secrets. Each step shows as it runs, passes with its time, or fails, opened on what it waited for, on which
  page and line, the screenshot, and what to check by hand; the code marks each step's lines, the failed one in red,
  and the console prints the result. **Run to here** (`Ctrl+Shift+Enter`) runs the flow up to the selected step,
  **Stop** (`Esc`, `Shift+F5`) ends a run, and slow motion waits 500 ms before each action. A loop's line is followed
  repeat by repeat, and a failure is told at its repeat. Nothing a test does is recorded: the recording pauses while it
  runs.
- `PlaywrightPlatform` set in a `Directory.Packages.Local.props`, e.g. `win-x64`, packs the recorder with Playwright's
  driver for those platforms only.

## 1.3.0

- The recorder can log in first with a login flow of your tests: `--login LoginFlow --login-assembly
  bin/Debug/net10.0/MyTests.dll [--login-settings Flows/LoginFlow.flow.yaml]`. The flow runs as the probe runs a setup
  flow, nothing it does is recorded, the recording starts on the page it leaves, and the flow recorded says it runs
  after it. When it fails, the recorder says why, secrets hidden, and waits for you to log in by hand. The options are
  remembered per project folder; `--no-login` forgets them.
- `F8` in the recorded window, or **Pause** in the panel, pauses the recording, and resumes it. While paused nothing is
  recorded, and the overlay, the panel and the console say so. A page the app went to meanwhile is opened by its
  address after the pause, and listed to check by hand.
- The panel's **Elements** view shows the recorded page's elements as a tree, searchable by text, id or CSS selector, to
  pick one a hover cannot reach: it lists its locators, and re-targets the selected step to it, or adds a click or a
  fill on it. `F4` freezes the page's elements, so the items of a menu that closes when the focus leaves can be picked
  after it closed.
- The panel shows XML doc comments readably (their tags dimmed), wraps long lines between calls rather than inside
  names, and got a polish pass: states for paused, logging in and saving, and a layout that holds at narrow widths.
- Actions of a login flow in the recorder time out as in the probe: the recorder's own driver no longer runs in
  Playwright's debug mode, which waits forever.
- `ENV=local` or `Headless: false` runs in the Chrome on this machine again, as in earlier versions: the run uses
  `http://localhost:9222`, starting a Chrome with a window there if none answers, instead of a remote `BrowserUrl`.
- The recorder's console has a new look: a compact header with the keys, one line per step with the element's name,
  the value and its locator, cut to the console's width and with right-to-left names in place, what to check by hand
  in amber as you record, and a summary of the files, the counts, each line to check and the next steps. The questions
  say what is wrong with an answer and ask again, Ctrl+C stops politely, `--help` has sections and examples, and errors
  go to the standard error. Colours turn off when the output is redirected or `NO_COLOR` is set, glyphs fall back to
  ASCII where the console cannot show them, and `FORCE_COLOR` turns colours on.

## 1.2.1

- A `Directory.Packages.Local.props` beside `Directory.Packages.props`, if there is one, changes package versions, e.g.
  to those of your own feed, without editing the repository's files.
- Builds with several package sources configured (NU1507 no longer fails the build).

## 1.2.0

- While recording, a box follows the pointer over the element a click records, labelled with the locator the flow
  will use and why each locator tried before it lost (`id: found 2 (1 hidden)`, `id: looks generated`,
  `testid: none`). F9 hides or shows it.
- `Alt+↑` and `Alt+↓` pick the element the next click records: the one around the box, or back toward the pointer.
- A click on a plain element that looks clickable (a hand pointer, `onclick`, `tabindex`) records that element, like a
  button. When only Playwright's own locator finds the element, as for a carousel's wrapper, the element the click
  hits inside it with a stable id or test id is recorded instead.
- `--keep-recording` saves the raw recording beside the flow, with every locator tried, to share when an element was
  located badly.
- The recorder opens a panel beside the recorded window that shows the flow exactly as it will be saved, step by step,
  with its code and YAML. For each step it lists every locator found for the element and what each found, highlights
  it in the app, and takes another; it also undoes the last step, renames the flow and saves.
  `qaas-playwright record ... --no-panel` records without it.
- The recorder saves a flow into a folder that does not exist yet, instead of failing after the recording.

## 1.1.0

First public release.

- `PlaywrightFlowProbe` runs C# flows (`BasePlaywrightFlow<TConfig>`) in Chrome over CDP, each with typed settings
  from `FlowConfiguration:<FlowName>`; `PlaywrightFlowAssertion` reports each flow's result with a failure
  screenshot, every pop-up the flow left open, and the run's warnings.
- Settings are checked strictly before the browser opens: unknown keys, values that do not convert, unresolved
  `${...}`, and each flow's own settings, all reported at once.
- Each run gets a browser context of its own (`IsolateContext`); a login can be saved once and reused
  (`SaveStorageStatePath`, `LoadStorageStatePath`); `ForEach` runs a flow once per generated item, with
  `Parallelism` workers.
- `BrowserUrl` defaults to `http://localhost:9222`, where the probe starts Chrome when none answers.
- `EmulateDesktopPointer` makes a headless Chrome render the desktop layout.
- Secrets are hidden in failures, warnings, the report and log properties.
- The recorder, the .NET tool `qaas-playwright`, records in its own Chrome session and writes clean flows: stable
  locators, one method per step, an Expect after each action, and every typed or chosen value as a setting.
