# Recording Flows

## Interactive mode (recommended)

```bash
dotnet run --project QaaS.Probes.Playwright.Recorder
```

The tool asks three questions:
1. **URL** — the website to record
2. **Flow name** — e.g. `login`, `create-mission`, `checkout`
3. **Output folder** — defaults to `Flows/`

Then a browser opens. Click around normally. When you close the browser, the tool:
- Extracts all your actions from Playwright codegen output
- Wraps them in a C# class implementing `BasePlaywrightFlow<T>`
- Saves to `Flows/YourFlowName.cs`
- Prints the YAML snippet to add to your test.qaas.yaml

## Quick mode

```bash
dotnet run --project QaaS.Probes.Playwright.Recorder -- record login https://my-app.com
```

Same thing, no questions asked.

## What gets generated

If you record a flow named `login` on `https://my-app.com`:

```csharp
using Microsoft.Playwright;
using QaaS.Probes.Playwright;

namespace Flows;

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

This is raw Playwright codegen output wrapped in a class. It works immediately as-is.

## Parameterizing a flow

When you want the same flow to work with different values:

### Step 1: Add properties to the config record
```csharp
public record LoginFlowConfig
{
    public string Username { get; set; } = null!;
    public string Password { get; set; } = null!;
}
```

### Step 2: Replace hardcoded values with Configuration.Property
```csharp
await page.GetByLabel("Username").FillAsync(Configuration.Username);
await page.GetByLabel("Password").FillAsync(Configuration.Password);
```

### Step 3: Pass values from YAML
```yaml
FlowConfiguration:
  LoginFlow:
    Username: admin
    Password: secret123
```

### Step 4: Switch environments by changing YAML only
```yaml
# staging
FlowConfiguration:
  LoginFlow:
    Username: staging-user
    Password: staging-pass

# production
FlowConfiguration:
  LoginFlow:
    Username: prod-user
    Password: prod-pass
```

## Re-recording

Run the recorder again with the same name. It overwrites the file and prints "UPDATED" instead of "SAVED". Your config record and parameterization will be lost — back up first if you've customized the flow.

## Complex config (arrays, nested objects)

Config records support anything QaaS's BindToObject supports:

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

## Tips

- **Don't remove the GotoAsync line** from recorded flows unless you're sure the probe's BaseUrl navigation covers it
- **Use BaseUrl** for the domain: flows can access `BaseUrl` property for sub-page navigation
- **One flow per transaction**: keep flows focused — login is one flow, create mission is another, checkout is another
- **Record on any environment**: the flow captures actions (click, fill, navigate), not URLs. Parameterize the URL parts that change between environments.
