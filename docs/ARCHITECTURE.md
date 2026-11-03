# Architecture

How QaaS.Playwright plugs into QaaS, what happens in a run, and why it is built the way it is.

## QaaS in one page

[QaaS](https://github.com/TheSmokeTeam) is a .NET test platform configured in YAML. Its extension points are *hooks*:
C# classes found by their class name, each with a typed settings record bound from YAML.

| Hook | Base class | YAML section |
|---|---|---|
| Generator | `BaseGenerator<T>` | `GeneratorConfiguration:` |
| Probe | `BaseProbe<T>` | `ProbeConfiguration:` |
| Assertion | `BaseAssertion<T>` | `AssertionConfiguration:` |
| Processor | `ITransactionProcessor` | `ProcessorConfiguration:` |

A test lists `Sessions` (each with its probes, publishers and consumers) and `Assertions` that check what the
sessions recorded. QaaS finds each hook by name, binds its section with `BindToObject<T>()`, and calls it.

## Where this plugin fits

- `PlaywrightFlowProbe` is a probe (`BaseProbe<PlaywrightFlowConfig>`). It connects to Chrome, opens `BaseUrl`, and
  runs the flows the YAML names, in order, on one page.
- A *flow* is a C# class (`BasePlaywrightFlow<TConfig>`), found by its class name like any hook, with its own typed
  settings under `FlowConfiguration:<FlowName>`. The recorder writes flows; people edit them like any code.
- Each flow's outcome (passed, or why not, with a screenshot) is kept per session in the run's `Context`.
- `PlaywrightFlowAssertion` reads those outcomes for the sessions it selects and reports them to Allure.

## A run

1. QaaS creates the probe. The probe checks its settings and each flow's own (see below), then connects to Chrome:
   one Playwright driver and one connection per browser for the whole process.
2. It opens a browser context of its own (unless `IsolateContext: false`) and a page, and goes to `BaseUrl`.
3. It runs `SetupFlows`, then `Flows`. With `ForEach`, workers each run `SetupFlows` once and then take items.
4. The first flow that throws stops the run. Its failure is recorded with the page's screenshot and URL, each pop-up
   it left open, and the whole exception, secrets hidden, then rethrown so the session fails.
5. The context closes. `PlaywrightFlowAssertion` later turns the recorded outcomes into the Allure result.

## Design decisions

### Flows are C#, not YAML

Every QaaS hook keeps its behavior in C#. C# gives flows compile-time checks, an IDE, and all of Playwright; a YAML
step language would need an interpreter that never covers everything.

### One probe, many flows

The probe owns the browser side (connecting, the context, the page, failure screenshots); a flow only acts on the
page. Flows in one run share the page, so a login carries over to the flows after it.

### The probe checks its settings itself

QaaS 4.8 binds a value that does not convert as its type's default with only a warning, only warns about unknown
keys, passes an unresolved `${...}` through as text, and checks a probe's own validation errors before it loads the
probe, so the probe never sees them. So `StrictBinder` checks the raw configuration against the settings type and the
probe fails the session with every problem. A failed run becomes a session failure the assertion reports; a throw
while loading would stop the whole run with no Allure result.

### The probe opens BaseUrl

Flows do not hard-code where the app is, so one YAML line switches environments, and a recorded flow addresses
other pages relative to `BaseUrl`.

## Repository layout

```
QaaS.Playwright/                 The library (NuGet package QaaS.Playwright)
  IPlaywrightFlow.cs, BasePlaywrightFlow.cs     What a flow implements
  PlaywrightFlowProbe.cs         The probe: opens the page, runs the flows, records outcomes
  PlaywrightFlowAssertion.cs     The assertion: reports the outcomes
  PlaywrightFlowOutcome.cs, PlaywrightFlowResults.cs   The outcomes, and how they reach the assertion
  Configuration/                 The probe's settings, the strict check of settings, secrets
  Browser/                       Connecting to Chrome, starting a local one, the page and context of a run
  Flows/                         Finding flows by class name and running them
  Reporting/                     The assertion's message and trace, and the run's warnings
  browser-defaults.yaml          Built-in defaults (BrowserUrl, test-id attribute, ...)
QaaS.Playwright.Recorder/        The recorder (.NET tool qaas-playwright)
  Capture/                       Its own Chrome session, the capture script and its overlay
  Generation/                    From a recording to a flow class and its YAML
  Panel/                         The panel beside the recorded window, and its page (Panel/ui)
  TestRun/                       Testing the flow recorded so far: its build, and its run as the probe's
QaaS.Playwright.Tests/           NUnit tests
  EndToEnd/                      The real probe and assertion against a headless Chrome and a small web app
  Recorder/                      The code the recorder writes, against a golden recording, and that it builds;
                                 what a recording session shows and records on a local page
docs/                            Documentation
```
