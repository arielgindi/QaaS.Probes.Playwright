# QaaS.Probes.Playwright

Playwright browser automation probe for QaaS. Record browser flows, replay them as part of QaaS test sessions.

## Quick Start

### 1. Record a flow
```bash
dotnet run --project QaaS.Probes.Playwright.Recorder
```
Uses your system Google Chrome — no extra browser to install.
Interactive mode asks you: URL, flow name, where to save. A browser opens — click around, close it when done. A C# flow class is saved automatically.

### 2. Use in your QaaS project

Add the reference:
```xml
<PackageReference Include="QaaS.Probes.Playwright" Version="1.0.0" />
```

Add to your `test.qaas.yaml`:
```yaml
Sessions:
  - Name: MySession
    Probes:
      - Name: BrowserFlow
        Probe: PlaywrightFlowProbe
        ProbeConfiguration:
          BaseUrl: https://my-app.com
          Flows: [LoginFlow]
```

Run:
```bash
dotnet run -- run test.qaas.yaml
```

## Local vs Cluster Browser

Controlled by the `ENV` environment variable.

| `ENV` | Behavior |
|---|---|
| unset (or `cluster` / `remote`) | Connect via CDP to `RemoteBrowserUrl` (defaults to `BrowserDefaults.RemoteUrl`). Used in CI inside OpenShift. |
| `local` | Attach to a local Chrome at `LocalBrowserUrl` (defaults to `http://localhost:9222`). Auto-launches Chrome from the standard install path if it isn't running. |

Anything else (typos like `true`, `1`, etc.) throws — no silent fallthrough.

`Headless: false` also forces local mode automatically (cluster Chrome runs in a headless container and can't show a window).

```yaml
ProbeConfiguration:
  BaseUrl: https://my-app.com
  Headless: false               # visible browser → local mode automatically + SlowMo 2000
  Flows: [LoginFlow]
  # Optional overrides:
  # RemoteBrowserUrl: ws://chrome.<other-ns>.svc:3000?token=...
  # LocalBrowserUrl:  http://localhost:9222
  # BrowserExecutablePath: C:\Program Files\Google\Chrome\Application\chrome.exe
```

**Local mode notes**
- Chrome opens with a dedicated profile at `~/.qaas/chrome-profile`. Persistent — log in once per site, sessions stay between runs. (Chrome 136+ blocks `--remote-debugging-port` on your default Chrome profile, so we use a separate one.)
- The probe attaches to the existing default context (your cookies/sessions), not an incognito-like new context.
- On Linux/macOS, Chrome is launched via `nohup … &` so it survives the test process.

## Built-in defaults — single source of truth

`BrowserDefaults.cs` holds the cluster URL and other constants. Edit them once for your org:

```csharp
public const string RemoteUrl =
    "ws://chrome.<your-namespace>.svc.cluster.local:3000?token=internal";
```

After you fork this repo, replace `<your-namespace>` with your actual OpenShift namespace. Every test repo that consumes this package inherits it automatically — no per-project YAML config needed.

## Passing Configuration

Each flow has a typed config record. Add properties, reference them in the flow, pass values from YAML:

```csharp
public class LoginFlow : BasePlaywrightFlow<LoginFlowConfig>
{
    public override async Task RunAsync(IPage page)
    {
        await page.GetByLabel("Username").FillAsync(Configuration.Username);
        await page.GetByLabel("Password").FillAsync(Configuration.Password);
        await page.GetByRole(AriaRole.Button, new() { Name = "Login" }).ClickAsync();
    }
}

public record LoginFlowConfig
{
    public string Username { get; set; } = null!;
    public string Password { get; set; } = null!;
}
```

```yaml
ProbeConfiguration:
  BaseUrl: https://my-app.com
  Flows: [LoginFlow]
  FlowConfiguration:
    LoginFlow:
      Username: admin
      Password: secret123
```

Uses QaaS's `BindToObject<T>()` — supports nested objects, arrays, dictionaries, enums, validation attributes. Same mechanism as all QaaS hooks.

## Multiple Flows with Separate Configs

Each flow gets its own section under FlowConfiguration:

```yaml
ProbeConfiguration:
  BaseUrl: https://my-app.com
  SetupFlows: [LoginFlow]
  Flows: [AddTodosFlow, CompleteTodosFlow, DeleteCompletedFlow]
  FlowConfiguration:
    LoginFlow:
      Username: admin
      Password: secret
    AddTodosFlow:
      Items: [Buy groceries, Walk the dog, Write QaaS probe]
    CompleteTodosFlow:
      ItemsToComplete: [Buy groceries]
    DeleteCompletedFlow:
      ExpectedRemaining: 2
```

All flows share one browser — login cookies carry to subsequent flows.

## Complex Configuration (Arrays of Objects)

Same pattern as `CreateRabbitMqExchanges` with its `Exchanges[]` array:

```csharp
public record CreateMissionsFlowConfig
{
    public MissionConfig[]? Missions { get; set; }
}

public record MissionConfig
{
    public string Name { get; set; } = null!;
    public string Priority { get; set; } = null!;
    public TeamConfig Team { get; set; } = null!;
}

public record TeamConfig
{
    public string Lead { get; set; } = null!;
    public string[] Members { get; set; } = [];
}
```

```yaml
FlowConfiguration:
  CreateMissionsFlow:
    Missions:
      - Name: Alpha Strike
        Priority: High
        Team:
          Lead: John
          Members: [Alice, Bob]
```

## Environments

Change one line to switch environments:

```yaml
BaseUrl: https://staging.my-app.com   # staging
BaseUrl: https://my-app.com           # production
```

Or use QaaS overwrite arguments:
```bash
dotnet run -- run test.qaas.yaml -r ProbeConfiguration:BaseUrl=https://staging.my-app.com
```

## Debugging

Set `Headless: false` to watch the browser. Everything adjusts automatically:

```yaml
ProbeConfiguration:
  BaseUrl: https://my-app.com
  Headless: false
  KeepOpen: true
  Flows: [LoginFlow]
```

- Browser becomes visible
- 1 second delay between flows so you can watch
- Images and CSS load normally
- Browser stays open after completion

## Configuration Reference

| Option | Default | Description |
|--------|---------|-------------|
| `BaseUrl` | *(required)* | Target URL — probe navigates here first |
| `Flows` | `[]` | Flow class names to run in order |
| `SetupFlows` | `[]` | Flows that run once before main flows (login, etc) |
| `FlowConfiguration` | `{}` | Per-flow config sections, bound via `BindToObject<T>()` |
| `Headless` | `true` | Invisible browser. `false` = visible + auto SlowMo |
| `KeepOpen` | `false` | Keep browser open (only with `Headless: false`) |
| `SlowMo` | `0` | Delay (ms) between every Playwright action. Auto 2000 when Headless=false |
| `BlockAssets` | `true` | Block images/fonts in headless mode |
| `DisableAnimations` | `true` | Kill CSS animations in headless mode |
| `DefaultTimeout` | `30000` | Max ms to wait for elements |

## Build & Test

```bash
dotnet restore QaaS.Probes.Playwright.slnx
dotnet build QaaS.Probes.Playwright.slnx -c Release
dotnet test QaaS.Probes.Playwright.slnx -c Release
```

## Documentation

- [ARCHITECTURE.md](ARCHITECTURE.md) — How the probe works internally
- [RECORDING.md](RECORDING.md) — How to record and parameterize flows
- [QAAS-CONTEXT.md](QAAS-CONTEXT.md) — QaaS platform context for developers
