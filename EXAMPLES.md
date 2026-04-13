# Examples

## Example 1: TodoMVC — 3 flows, each with its own config

This is the working example in the `PlaywrightDemo/` folder.

### Flows

**AddTodosFlow.cs** — adds items from a config array:
```csharp
public class AddTodosFlow : BasePlaywrightFlow<AddTodosFlowConfig>
{
    public override async Task RunAsync(IPage page)
    {
        foreach (var todo in Configuration.Items!)
        {
            await page.GetByPlaceholder("What needs to be done?").FillAsync(todo);
            await page.GetByPlaceholder("What needs to be done?").PressAsync("Enter");
        }
    }
}

public record AddTodosFlowConfig
{
    public string[]? Items { get; set; }
}
```

**CompleteTodosFlow.cs** — checks off specific items:
```csharp
public class CompleteTodosFlow : BasePlaywrightFlow<CompleteTodosFlowConfig>
{
    public override async Task RunAsync(IPage page)
    {
        foreach (var todo in Configuration.ItemsToComplete!)
        {
            await page.GetByRole(AriaRole.Listitem)
                .Filter(new() { HasText = todo })
                .GetByRole(AriaRole.Checkbox)
                .CheckAsync();
        }
    }
}

public record CompleteTodosFlowConfig
{
    public string[]? ItemsToComplete { get; set; }
}
```

**DeleteCompletedFlow.cs** — clears completed and verifies count:
```csharp
public class DeleteCompletedFlow : BasePlaywrightFlow<DeleteCompletedFlowConfig>
{
    public override async Task RunAsync(IPage page)
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Clear completed" }).ClickAsync();

        var remaining = await page.Locator(".todo-list li").CountAsync();
        if (remaining != Configuration.ExpectedRemaining)
            throw new Exception(
                $"Expected {Configuration.ExpectedRemaining} remaining but found {remaining}");
    }
}

public record DeleteCompletedFlowConfig
{
    public int ExpectedRemaining { get; set; }
}
```

### YAML

```yaml
MetaData:
  Team: Smoke
  System: TodoMVC

Sessions:
  - Name: TodoWorkflow
    Probes:
      - Name: ManageTodos
        Probe: PlaywrightFlowProbe
        ProbeConfiguration:
          BaseUrl: https://demo.playwright.dev/todomvc/#/
          Headless: false
          KeepOpen: true
          Flows: [AddTodosFlow, CompleteTodosFlow, DeleteCompletedFlow]
          FlowConfiguration:
            AddTodosFlow:
              Items:
                - Buy groceries
                - Walk the dog
                - Write QaaS probe
                - Deploy to production
                - Go to sleep
            CompleteTodosFlow:
              ItemsToComplete:
                - Buy groceries
                - Write QaaS probe
            DeleteCompletedFlow:
              ExpectedRemaining: 3
```

### Run it
```bash
cd PlaywrightDemo/PlaywrightDemo
dotnet run -- run test.qaas.yaml --no-process-exit
```

### What happens
1. Browser opens at `https://demo.playwright.dev/todomvc/#/`
2. **AddTodosFlow** types 5 todos, pressing Enter after each
3. **CompleteTodosFlow** checks the checkbox on "Buy groceries" and "Write QaaS probe"
4. **DeleteCompletedFlow** clicks "Clear completed", verifies 3 items remain
5. Browser stays open for inspection

---

## Example 2: Login + Actions (setup flow pattern)

When you need to login first, then do other things:

```yaml
ProbeConfiguration:
  BaseUrl: https://my-app.com
  SetupFlows: [LoginFlow]
  Flows: [CreateOrderFlow, VerifyOrderFlow]
  FlowConfiguration:
    LoginFlow:
      Username: admin
      Password: secret
    CreateOrderFlow:
      ProductName: Widget Pro
      Quantity: 3
    VerifyOrderFlow:
      ExpectedTotal: "$29.97"
```

- `SetupFlows` runs `LoginFlow` once — cookies are set
- `Flows` runs `CreateOrderFlow` then `VerifyOrderFlow` — both see the logged-in session

---

## Example 3: Complex nested config (missions with teams)

```csharp
public class CreateMissionsFlow : BasePlaywrightFlow<CreateMissionsFlowConfig>
{
    public override async Task RunAsync(IPage page)
    {
        foreach (var mission in Configuration.Missions!)
        {
            await page.GetByRole(AriaRole.Button, new() { Name = "New Mission" }).ClickAsync();
            await page.GetByLabel("Name").FillAsync(mission.Name);
            await page.GetByLabel("Priority").SelectOptionAsync(mission.Priority);
            await page.GetByLabel("Lead").FillAsync(mission.Team.Lead);

            foreach (var member in mission.Team.Members)
            {
                await page.GetByLabel("Add Member").FillAsync(member);
                await page.GetByRole(AriaRole.Button, new() { Name = "Add" }).ClickAsync();
            }

            await page.GetByRole(AriaRole.Button, new() { Name = "Create" }).ClickAsync();
            await page.GetByText("Mission created").WaitForAsync();
        }
    }
}

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
      - Name: Beta Recon
        Priority: Low
        Team:
          Lead: Jane
          Members: [Charlie]
      - Name: Gamma Patrol
        Priority: Medium
        Team:
          Lead: Mike
          Members: [Dave, Eve, Frank]
```

Same pattern as QaaS's `CreateRabbitMqExchanges` with its `Exchanges[]` array. YAML auto-binds to the nested C# records.

---

## Example 4: Headless production run

For CI/CD — no browser window, fast execution:

```yaml
ProbeConfiguration:
  BaseUrl: https://production.my-app.com
  Flows: [LoginFlow, SmokeTestFlow]
  FlowConfiguration:
    LoginFlow:
      Username: smoke-user
      Password: "${env:SMOKE_PASSWORD}"
    SmokeTestFlow:
      PagesToCheck:
        - /dashboard
        - /orders
        - /settings
```

No `Headless` (defaults to true), no `KeepOpen`, no `SlowMo`. Runs in seconds. Images/fonts blocked, animations disabled.

---

## Example 5: Multiple environments

Same flows, different YAML per environment:

**test.staging.qaas.yaml:**
```yaml
ProbeConfiguration:
  BaseUrl: https://staging.my-app.com
  Flows: [LoginFlow, CreateOrderFlow]
  FlowConfiguration:
    LoginFlow:
      Username: staging-user
      Password: staging-pass
```

**test.production.qaas.yaml:**
```yaml
ProbeConfiguration:
  BaseUrl: https://my-app.com
  Flows: [LoginFlow, CreateOrderFlow]
  FlowConfiguration:
    LoginFlow:
      Username: prod-smoke-user
      Password: prod-smoke-pass
```

Or use QaaS overwrite arguments:
```bash
dotnet run -- run test.qaas.yaml -r ProbeConfiguration:BaseUrl=https://staging.my-app.com
```
