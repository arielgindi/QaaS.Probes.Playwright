# Changelog

Notable changes, newest first. Versions follow [Semantic Versioning](https://semver.org/).

## Unreleased

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
