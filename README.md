# QaaS.Probes.Playwright

Playwright browser automation probe for QaaS. Record browser flows, replay them as part of QaaS test sessions.

## Quick Start

### 1. Install Chromium (one time)
```bash
dotnet run --project QaaS.Probes.Playwright.Recorder -- install
```

### 2. Record a flow
```bash
dotnet run --project QaaS.Probes.Playwright.Recorder
```
Interactive mode asks you: URL, flow name, where to save. A browser opens — click around, close it when done. A C# flow class is saved automatically.

### 3. Use in your QaaS project

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

The probe ships with sensible defaults — **no YAML needed** for the common cases.

**Cluster mode** (default, used in CI): the probe connects via CDP to the built-in
`DefaultRemoteBrowserUrl` (the org's Chrome pod in OpenShift). No env var, no YAML.

**Local mode** (development on your laptop): `export BROWSER_MODE=local`. The probe
attaches to your local Chrome at the built-in `DefaultLocalBrowserUrl`
(`http://localhost:9222`). Start your Chrome once a day:
```bash
google-chrome --remote-debugging-port=9222 --user-data-dir="$HOME/chrome-qaas-dev"
```
Attaching keeps your auth/cookies/fingerprint between runs.

**Overrides** (rare — only when you need something non-standard):
```yaml
ProbeConfiguration:
  BaseUrl: https://my-app.com
  RemoteBrowserUrl: http://other-cluster:9222   # override cluster URL for this project
  LocalBrowserUrl:  http://localhost:9333       # override local URL (custom port)
  BrowserExecutablePath: /opt/google/chrome/chrome   # use specific Chrome binary
  Flows: [LoginFlow]
```

To change the built-in defaults for everyone using this probe, edit the constants at
the top of `PlaywrightFlowProbe.cs` — one change, every project picks it up.

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
| `SlowMo` | `0` | Delay between flows in ms. Auto 1000 when visible |
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
