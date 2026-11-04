# Configuration

Everything `PlaywrightFlowProbe` and `PlaywrightFlowAssertion` can be told, and how a run behaves.

## Probe settings

| Setting | Default | What it does |
|---|---|---|
| `BaseUrl` | required | The site. The probe opens it before the first flow. |
| `SetupFlows`, `Flows` | none | Flow class names, run in that order on one page. |
| `SetupFlowsBeforeBaseUrl` | `false` | Run `SetupFlows` on a blank page, then open `BaseUrl`, unless they left the page on it or under it: for a login that plants a token with `AddInitScriptAsync` before any page loads. |
| `FlowConfiguration` | none | One section per flow, bound to that flow's settings record. |
| `ForEach` | none | A DataSource: run `Flows` once per item it generates. See below. |
| `Parallelism` | `1` | With `ForEach`, how many workers go through the items at the same time. |
| `BrowserUrl` | `http://localhost:9222` | The Chrome to run in, over CDP. See below. |
| `BrowserExecutablePath` | found automatically | The Chrome the probe starts for a `BrowserUrl` on this machine. |
| `Headless` | `true` | Whether nobody is watching. See below. |
| `BlockAssets` | `true` | Block images and fonts while `Headless`, for speed. |
| `SlowMo` | `0`, or `2000` when `Headless: false` | Pause before each action, in ms. |
| `KeepOpen` | `false` | Leave the page open in the Playwright inspector (`Headless: false`, in a terminal). |
| `DefaultTimeout` | `30000` | The longest an action waits, in ms; `Expect(...)` keeps 5 s. See below. |
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

Secrets show as `***` in failures, warnings and the report: the value of every `FlowConfiguration` key that contains
`password`, `secret`, `token`, `apikey` or `pwd` (any case), and the password and query values of `BrowserUrl`.
Values shorter than four characters, `true` or `false`, and numbers in the query, such as Browserless's
`timeout=30000`, are left as they are, as hiding them would hide ordinary text too; a number whose key contains one
of those words, as in `token=12345678`, is still hidden.

### BrowserUrl

`BrowserUrl` is a CDP endpoint: a Chrome on your machine such as `http://localhost:9222` (the default), or a remote
one such as a [Browserless](https://www.browserless.io/) server, `ws://browserless:3000?token=...`.

When the URL is on this machine and nothing answers there, the probe starts Chrome itself with the profile
`~/.qaas/chrome-profile-<port>`. That Chrome keeps running, so later runs reuse it and its logins. Use a name or an
IPv4 address, such as `http://localhost:9222`: Chrome's debugging port does not listen on `[::1]`.

To run a shared test in a Chrome on your machine without editing it, set `ENV=local` (`$env:ENV = "local"` in
PowerShell) or `Headless: false`: the run then uses `http://localhost:9222`, and starts a Chrome with a window there
if none answers, instead of a remote `BrowserUrl`. A `BrowserUrl` that already is on this machine is kept. The log
says when the local Chrome is used instead.

For another browser per machine, use a QaaS placeholder and set an environment variable only on that machine:

```yaml
BrowserUrl: ${BROWSER_URL ?? ws://browserless:3000?token=my-token}
```

### Headless

`Headless` says whether a person is watching. `true` blocks images and fonts for speed. `false` runs in the Chrome on
this machine, as `ENV=local` does (see [BrowserUrl](#browserurl)), slows every action down (`SlowMo`) and allows
`KeepOpen`. If that Chrome was started headless by hand, the probe warns that no window will appear.

### Timeouts

`DefaultTimeout` bounds each action: a click, a fill, a navigation, a locator's wait. An `Expect(...)` assertion
keeps Playwright's own 5 s unless the flow gives it one, e.g.
`await Expect(page.GetByText("Saved")).ToBeVisibleAsync(new() { Timeout = 15_000 });`. Neither bounds a whole flow: a
flow that awaits something that never ends, such as a JavaScript promise that never settles, waits forever.

### Logging in once: storage state

A session that logs in can save its login, and later sessions can start from it:

```yaml
# A session with Stage: 1
ProbeConfiguration: { BaseUrl: https://my-app.com, Flows: [LoginFlow], SaveStorageStatePath: state/admin.json }
# Any number of sessions with Stage: 2, in parallel
ProbeConfiguration: { BaseUrl: https://my-app.com, Flows: [OrdersFlow], LoadStorageStatePath: state/admin.json }
```

The file is written only after every flow passed, and atomically, so a reader never sees half of it. It holds the
login, so on Linux and macOS only its owner may read it; on Windows it takes the folder's permissions, so keep it in a
folder only you can read. The saving session must finish first. sessionStorage is not saved. A file saved before the
run started, e.g. because `-a` or a category filter skipped the saving session, is loaded with a warning that says how
old it is.

### IsolateContext

Each run gets a fresh browser context of its own, disposed afterwards, so parallel sessions that log in as different
users cannot overwrite each other's login, and do not wait for each other to render. `IsolateContext: false` shares
the browser's default context instead, e.g. to reuse the logins of your local Chrome; the run then closes the page and
pop-ups it opened, and no other. It is rejected with `ForEach`, where every worker needs a context of its own.

### Mobile layout in a headless Chrome: EmulateDesktopPointer

A headless Chrome started by hand reports no mouse, so responsive apps render their mobile layout: MUI date pickers,
for one, become read-only fields with other labels than the recorder saw. The probe warns about it in every run.
Fix it where Chrome starts:

```bash
google-chrome --headless=new --remote-debugging-port=9222 \
  --blink-settings=primaryPointerType=4,availablePointerTypes=4,primaryHoverType=2,availableHoverTypes=2
```

or set `EmulateDesktopPointer: true`, which makes `matchMedia` report a mouse before any script runs, in the run's
page and its pop-ups. It covers single `pointer`/`hover` queries from JavaScript (what MUI uses), not CSS media
queries. With `IsolateContext: false` it leaves the shared context's other pages alone, so a pop-up opened without a
link to its opener, as a `target="_blank"` link opens one, reports a mouse only from the second page it loads.

## One flow per item: ForEach

To do the same thing for every item of a list, e.g. create the 50 tickets a generator makes from a list in the
YAML, pass the DataSource to the probe and name it in `ForEach`:

```yaml
Probes:
  - Name: CreateTickets
    Probe: PlaywrightFlowProbe
    DataSourceNames: [Tickets]
    ProbeConfiguration:
      BaseUrl: https://my-app.com
      SetupFlows: [LoginFlow]      # once per worker
      Flows: [CreateTicketFlow]   # once per item
      ForEach: Tickets
      Parallelism: 5               # workers at the same time (default 1)
```

The flow reads its item as `Item`, the item's body as JSON (`Item.Deserialize<T>()` gives a typed record), and
its position as `ItemIndex`. Both are null without `ForEach`.

```csharp
public sealed class CreateTicketFlow : BasePlaywrightFlow<CreateTicketFlowConfig>
{
    public override async Task RunAsync(IPage page)
    {
        await page.GotoAsync($"{BaseUrl}/tickets/new");
        await page.GetByLabel("Name").FillAsync(Item!["Name"]!.GetValue<string>());
        await page.GetByRole(AriaRole.Button, new() { Name = "Create" }).ClickAsync();
    }
}
```

Each worker opens a browser context of its own, runs `SetupFlows` once, then takes the next item until none is
left. Every item is reported on its own, e.g. `CreateTicketFlow[17]`, with its time in the log. A failed item gets
its screenshot, and its worker goes back to `BaseUrl` and carries on; a worker whose `SetupFlows` fail stops, and the
others take its items. The session fails if any item or worker failed, and says which, and when the DataSource
produced no items at all. `SaveStorageStatePath` and `IsolateContext: false` are rejected with `ForEach`, and
`KeepOpen` is ignored with a warning.

## Many cases, fast

- Isolation is the default: each session works in a browser context of its own, so sessions in one stage run truly
  in parallel, never share a login, and do not wait for each other to render.
- Log in once and reuse it: one session saves the login with `SaveStorageStatePath`, and the sessions after it load
  it with `LoadStorageStatePath` and skip their login flow (see [Logging in once](#logging-in-once-storage-state)). A
  saved login lasts only as long as the app's own session does.
- All runs in a process share one Playwright driver and one connection per browser, and `BlockAssets` keeps the
  browser's HTTP cache, so the app's bundle is not downloaded again on every page.

## What the assertion checks

`PlaywrightFlowAssertion` has no settings. It fails when:

- a flow failed. The message names the flow, the reason, the element it waited for and the page URL, e.g.
  `CheckoutFlow failed (1/2 flows passed): Timeout 30000ms exceeded (waiting for GetByRole(AriaRole.Button, new() { Name = "Pay" })) on https://my-app.com/cart. Passed: LoginFlow.`
  An exception's innermost cause is added (`Checkout failed caused by: card declined`), and a crashed page is named
  as the reason rather than the timeout after it. The trace has the whole exception, with its call log and stack,
  and names the session and probe of each flow when there are several; the page's screenshot is attached, or the
  trace says why none could be taken. The page is the one the flow ran on: nothing says which page a failed call was
  on, so each pop-up the flow left open is named too (`..., with pop-up https://my-app.com/help open`), and its
  screenshot attached;
- a session recorded a failure, e.g. a mistake in the settings, or the browser could not be reached;
- nothing was verified: no session is attached, or an attached session ran no flow (a session without the probe, or
  an `assert` command after a separate `act`, see [Known issues](KNOWN-ISSUES.md#qaas-48)).

Every warning the probes logged, such as a browser without a mouse, is listed under `Warnings:` at the end of the
trace, passing or not, and the message says how many there are.
