# QaaS.Probes.Playwright

Playwright browser automation probe for QaaS. Record browser flows, replay them headlessly as part of QaaS test sessions.

## Quick Start

### 1. Install Chromium (one time)

```bash
dotnet run --project QaaS.Probes.Playwright.Recorder -- install
```

### 2. Record a flow

```bash
dotnet run --project QaaS.Probes.Playwright.Recorder -- record login https://my-app.com
```

A browser opens. Click around — fill forms, navigate, submit. Close the browser when done. A C# flow class is saved to `Flows/LoginFlow.cs`.

### 3. Use in your QaaS project

Reference the package:

```xml
<PackageReference Include="QaaS.Probes.Playwright" Version="1.0.0" />
```

Add the probe to your `test.qaas.yaml`:

```yaml
Sessions:
  - Name: MySession
    Probes:
      - Name: BrowserSetup
        Probe: PlaywrightFlowProbe
        ProbeConfiguration:
          BaseUrl: https://my-app.com
          Flows: [LoginFlow]
```

Run:

```bash
dotnet run -- run test.qaas.yaml
```

## How It Works

The recorder wraps Playwright's built-in `codegen` tool and saves the output as a C# class:

```csharp
public class LoginFlow : BasePlaywrightFlow<LoginFlowConfig>
{
    public override async Task RunAsync(IPage page)
    {
        await page.GotoAsync("https://my-app.com/login");
        await page.GetByLabel("Username").FillAsync("admin");
        await page.GetByLabel("Password").FillAsync("secret");
        await page.GetByRole(AriaRole.Button, new() { Name = "Login" }).ClickAsync();
    }
}

public record LoginFlowConfig { }
```

The probe discovers the class by name, binds config from YAML, and calls `RunAsync` with a Playwright page.

## Passing Configuration

Add properties to the config record and use `Configuration.Property` in the flow:

```csharp
public class LoginFlow : BasePlaywrightFlow<LoginFlowConfig>
{
    public override async Task RunAsync(IPage page)
    {
        await page.GotoAsync("https://my-app.com/login");
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

YAML:

```yaml
ProbeConfiguration:
  BaseUrl: https://my-app.com
  Flows: [LoginFlow]
  FlowConfiguration:
    Username: admin
    Password: secret123
```

This uses QaaS's `BindToObject<T>()` — same mechanism as all QaaS hooks. Supports nested objects, arrays, dictionaries, enums, validation attributes.

## Complex Configuration

Same pattern as `CreateRabbitMqExchanges` with its `Exchanges[]` array:

```csharp
public record CreateMissionsFlowConfig
{
    [Required, MinLength(1)]
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

public class CreateMissionsFlow : BasePlaywrightFlow<CreateMissionsFlowConfig>
{
    public override async Task RunAsync(IPage page)
    {
        foreach (var mission in Configuration.Missions!)
        {
            await page.GetByLabel("Name").FillAsync(mission.Name);
            await page.GetByLabel("Priority").SelectOptionAsync(mission.Priority);
            await page.GetByLabel("Lead").FillAsync(mission.Team.Lead);

            foreach (var member in mission.Team.Members)
            {
                await page.GetByLabel("Add Member").FillAsync(member);
                await page.GetByRole(AriaRole.Button, new() { Name = "Add" }).ClickAsync();
            }

            await page.GetByRole(AriaRole.Button, new() { Name = "Create" }).ClickAsync();
        }
    }
}
```

```yaml
ProbeConfiguration:
  BaseUrl: https://my-app.com
  SetupFlows: [LoginFlow]
  Flows: [CreateMissionsFlow]
  FlowConfiguration:
    Username: admin
    Password: secret
    Missions:
      - Name: Alpha Strike
        Priority: High
        Team:
          Lead: John
          Members: [Alice, Bob]
      - Name: Beta Recon
        Priority: Low
        Team:
          Lead: Jane
          Members: [Charlie]
```

## Setup Flows

Use `SetupFlows` for actions that run once before the main flows. Both share the same browser context — login cookies carry over.

```yaml
ProbeConfiguration:
  BaseUrl: https://my-app.com
  SetupFlows: [LoginFlow]
  Flows: [CreateMissionFlow, VerifyDashboardFlow]
```

## Debugging

Set `Headless: false` to watch the browser. Asset blocking and animation disabling are automatically turned off. SlowMo defaults to 1 second between steps.

```yaml
ProbeConfiguration:
  BaseUrl: https://my-app.com
  Headless: false
  KeepOpen: true
  Flows: [LoginFlow]
```

## Configuration Reference

| Option | Default | Description |
|--------|---------|-------------|
| `BaseUrl` | *(required)* | Target site URL |
| `Flows` | *(required)* | Flow class names to run |
| `SetupFlows` | `[]` | Flows that run once before main flows |
| `FlowConfiguration` | `{}` | Passed to each flow's `Configuration` via `BindToObject<T>()` |
| `Headless` | `true` | Invisible browser. `false` = visible + SlowMo + full CSS |
| `KeepOpen` | `false` | Keep browser open after completion (with `Headless: false`) |
| `SlowMo` | `0` | Delay between steps in ms. Auto 1000 when `Headless: false` |
| `BlockAssets` | `true` | Block images/fonts in headless mode |
| `DisableAnimations` | `true` | Kill CSS animations in headless mode |
| `DefaultTimeout` | `30000` | Max ms to wait for elements |

## Build & Test

```bash
dotnet restore QaaS.Probes.Playwright.slnx
dotnet build QaaS.Probes.Playwright.slnx -c Release
dotnet test QaaS.Probes.Playwright.slnx -c Release
```
