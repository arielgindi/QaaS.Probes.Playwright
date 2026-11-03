# Changelog

Notable changes, newest first. Versions follow [Semantic Versioning](https://semver.org/).

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
