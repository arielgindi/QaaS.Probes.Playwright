# QaaS.Playwright

Browser tests for QaaS. Record a flow in Chrome, get a C# class, and run it from a QaaS test with
`PlaywrightFlowProbe`; `PlaywrightFlowAssertion` reports which flows passed and why one failed, with a screenshot.

## Quick start

1. Record a flow (uses your installed Google Chrome):

   ```bash
   dotnet run --project QaaS.Playwright.Recorder
   ```

   Answer three questions, click through the site, close the browser. The flow is saved as `Flows/<Name>.cs`.
   See [docs/RECORDING.md](docs/RECORDING.md).

2. Reference the package from your QaaS test project:

   ```xml
   <PackageReference Include="QaaS.Playwright" Version="1.0.0" />
   ```

3. Run the flow in a session and assert on it:

   ```yaml
   Sessions:
     - Name: Checkout
       Probes:
         - Name: Browser
           Probe: PlaywrightFlowProbe
           ProbeConfiguration:
             BaseUrl: https://my-app.com
             SetupFlows: [LoginFlow]
             Flows: [AddToCartFlow, CheckoutFlow]
             FlowConfiguration:
               LoginFlow:
                 Username: smoke-user
   Assertions:
     - Name: CheckoutWorks
       Assertion: PlaywrightFlowAssertion
       SessionNames: [Checkout]
   ```

   ```bash
   dotnet run -- run test.qaas.yaml
   ```

The probe opens `BaseUrl`, then runs `SetupFlows` and `Flows` in order on the same page. The first flow that fails
stops the run, and so does an error page at `BaseUrl` (HTTP 400 or above), since flows starting there verify nothing. Each flow reads its own `FlowConfiguration:<FlowName>` section as typed settings; see
[docs/EXAMPLES.md](docs/EXAMPLES.md).

## Settings

| Setting | Default | What it does |
|---|---|---|
| `BaseUrl` | required | The site. The probe opens it before the first flow. |
| `SetupFlows`, `Flows` | none | Flow class names, run in that order on one page. |
| `FlowConfiguration` | none | One section per flow, bound to that flow's settings record. |
| `ForEach` | none | A DataSource: run `Flows` once per item it generates. See below. |
| `Parallelism` | `1` | With `ForEach`, how many workers go through the items at the same time. |
| `BrowserUrl` | `browser-defaults.yaml` | The Chrome to run in, over CDP. See below. |
| `BrowserExecutablePath` | found automatically | The Chrome the probe starts for a `BrowserUrl` on this machine. |
| `Headless` | `true` | Whether nobody is watching. See below. |
| `BlockAssets` | `true` | Block images and fonts while `Headless`, for speed. |
| `SlowMo` | `0`, or `2000` when `Headless: false` | Pause before each action, in ms. |
| `KeepOpen` | `false` | Leave the page open in the Playwright inspector (`Headless: false`, in a terminal). |
| `DefaultTimeout` | `30000` | The longest any action waits, in ms. |
| `ViewportWidth`, `ViewportHeight` | `1920`, `1080` | The page size, and so the failure screenshot's. |
| `FullPageScreenshot` | `false` | Screenshot the whole page on failure, not only the viewport. |
| `SaveStorageStatePath` | none | Save the login (cookies, localStorage) here after every flow passed. |
| `LoadStorageStatePath` | none | Start already logged in from a saved file. |
| `IsolateContext` | `true` | Run in a fresh browser context of its own; `false` shares the browser's. |
| `EmulateDesktopPointer` | `false` | Make the page report a mouse. |

A mistake in the settings fails the session before the browser opens, with one message that lists every problem and
its path: a key that is no setting (`Flow:` for `Flows:`), a value that does not convert (`Headless: offf`), a single
value where a list belongs (`Flows: LoginFlow`), a `${...}` placeholder QaaS left unresolved, a `FlowConfiguration`
section of a flow that does not run, settings that do not work together, and every mistake in each flow's own settings
(`FlowConfiguration:LoginFlow:Usernmae: not a setting (known: Username)`), nested ones included.

### BrowserUrl

`BrowserUrl` is a CDP endpoint: a Browserless pod such as `ws://chrome.<namespace>.svc.cluster.local:3000?token=...`
(see [openshift/chrome.yaml](openshift/chrome.yaml)), or a Chrome on your machine such as `http://localhost:9222`.
The default comes from `QaaS.Playwright/browser-defaults.yaml`, which is built into the package: edit it once when
you fork, and every test inherits it.

When the URL is on this machine and nothing answers there, the probe starts Chrome itself with the profile
`~/.qaas/chrome-profile`. That Chrome keeps running, so later runs reuse it and its logins.

To run a shared test against your own browser without editing it, use a QaaS placeholder and set an environment
variable only on your machine:

```yaml
BrowserUrl: ${BROWSER_URL ?? ws://chrome.my-namespace.svc.cluster.local:3000?token=my-token}
```

### Headless

`Headless` does not decide whether Chrome shows a window; that depends only on how Chrome was started. It says
whether a person is watching: `true` blocks images and fonts for speed, `false` slows every action down (`SlowMo`)
and allows `KeepOpen`. With `Headless: false` on a Chrome that runs headless, the probe warns that no window will
appear.

### Logging in once: storage state

A session that logs in can save its login, and later sessions can start from it:

```yaml
# Stage 1
ProbeConfiguration: { BaseUrl: https://my-app.com, Flows: [LoginFlow], SaveStorageStatePath: state/admin.json }
# Stage 2, in any number of parallel sessions
ProbeConfiguration: { BaseUrl: https://my-app.com, Flows: [OrdersFlow], LoadStorageStatePath: state/admin.json }
```

The file is written only after every flow passed, and atomically, so a reader never sees half of it. The saving
session must finish first. sessionStorage is not saved. A file saved before the run started, e.g. because `-a` or a
category filter skipped the saving session, is loaded with a warning that says how old it is.

### IsolateContext

Each run gets a fresh browser context of its own, disposed afterwards, so parallel sessions that log in as different
users cannot overwrite each other's login, and do not wait for each other to render. `IsolateContext: false` shares
the browser's default context instead, e.g. to reuse the logins of your local Chrome.

**Upgrading:** runs used to share the default context. A session that relied on a login left there by an earlier
stage must now save it with `SaveStorageStatePath` and load it with `LoadStorageStatePath`.

### Mobile layout in a headless Chrome: EmulateDesktopPointer

A headless Chrome started by hand reports no mouse, so responsive apps render their mobile layout: MUI date pickers,
for one, become read-only fields with other labels than the recorder saw. The probe warns about it in every run.
Fix it where Chrome starts:

```bash
google-chrome --headless=new --remote-debugging-port=9222 \
  --blink-settings=primaryPointerType=4,availablePointerTypes=4,primaryHoverType=2,availableHoverTypes=2
```

or set `EmulateDesktopPointer: true`, which makes the page's `matchMedia` report a mouse before any script runs. It
covers single `pointer`/`hover` queries from JavaScript (what MUI uses), not CSS media queries.

## One flow per item: ForEach

To do the same thing for every item of a list, e.g. create the 50 missions a generator makes from a list in the
YAML, pass the DataSource to the probe and name it in `ForEach`:

```yaml
Probes:
  - Name: CreateMissions
    Probe: PlaywrightFlowProbe
    DataSourceNames: [Missions]
    ProbeConfiguration:
      BaseUrl: https://app
      SetupFlows: [LoginFlow]      # once per worker
      Flows: [CreateMissionFlow]   # once per item
      ForEach: Missions
      Parallelism: 5               # workers at the same time (default 1)
```

The flow reads its item as `Item`, the item's body as JSON (`Item.Deserialize<T>()` gives a typed record), and
its position as `ItemIndex`. Both are null without `ForEach`.

```csharp
public sealed class CreateMissionFlow : BasePlaywrightFlow<CreateMissionFlowConfig>
{
    public override async Task RunAsync(IPage page)
    {
        await page.GotoAsync($"{BaseUrl}/missions/new");
        await page.GetByLabel("Name").FillAsync(Item!["Name"]!.GetValue<string>());
        await page.GetByRole(AriaRole.Button, new() { Name = "Create" }).ClickAsync();
    }
}
```

Each worker opens a browser context of its own, runs `SetupFlows` once, then takes the next item until none is
left. Every item is reported on its own, e.g. `CreateMissionFlow[17]`, with its time in the log. A failed item gets
its screenshot, and its worker goes back to `BaseUrl` and carries on; a worker whose `SetupFlows` fail stops, and the
others take its items. The session fails if any item or worker failed, and says which, and when the DataSource
produced no items at all. `SaveStorageStatePath` and `IsolateContext: false` are rejected with `ForEach`, and
`KeepOpen` is ignored with a warning.

## Many cases, fast

- Isolation is the default: each session works in a browser context of its own, so sessions in one stage run truly
  in parallel and never share a login. In a benchmark, ten parallel sessions took 5.1 s instead of 17.8 s, and none
  failed instead of 39%.
- Log in once and reuse it: one session saves the login with `SaveStorageStatePath`, and the sessions after it load
  it with `LoadStorageStatePath` and skip their login flow (see "Logging in once" above). Measured: 15% faster over
  50 cases, and 6.1 s instead of 10.0 s for three sessions in one case. A saved login lasts only as long as the
  app's own session does.
- All runs in a process share one Playwright driver and one connection per browser, and `BlockAssets` keeps the
  browser's HTTP cache, so the app's bundle is not downloaded again on every page.

## What the assertion checks

`PlaywrightFlowAssertion` has no settings. It fails when:

- a flow failed. The message names the flow, the reason, the element it waited for and the page URL, e.g.
  `CheckoutFlow failed (1/2 flows passed): Timeout 30000ms exceeded (waiting for GetByRole(AriaRole.Button, new() { Name = "Pay" })) on https://my-app.com/cart. Passed: LoginFlow.`
  The trace has the full error and call log, and the page's screenshot is attached;
- a session recorded a failure, e.g. a mistake in the settings, or the browser could not be reached;
- nothing was verified: no session is attached, or an attached session ran no flow (a session without the probe).

Every warning the probes logged, such as a browser without a mouse, is listed under `Warnings:` at the end of the
trace, passing or not, and the message says how many there are.

## Build and test

```bash
dotnet build
dotnet test
dotnet test --filter TestCategory!=EndToEnd   # skip the tests that start Chrome
```

The `EndToEnd` tests run the real probe and assertion against a headless Chrome they start and stop themselves, and
a small web app served in-process. They take a few seconds, and are skipped when Chrome is not installed.

## More

- [docs/RECORDING.md](docs/RECORDING.md): recording and parameterizing flows
- [docs/EXAMPLES.md](docs/EXAMPLES.md): complete flows and YAML to copy
- [docs/QAAS-CONTEXT.md](docs/QAAS-CONTEXT.md): how QaaS hooks work and where this plugin fits
