# Examples

## Flows with their own settings

Three flows against the TodoMVC demo. Each reads its own `FlowConfiguration:<FlowName>` section.

```csharp
using Microsoft.Playwright;
using QaaS.Playwright;
using static Microsoft.Playwright.Assertions;

public sealed class AddTodosFlow : BasePlaywrightFlow<AddTodosFlowConfig>
{
    public override async Task RunAsync(IPage page)
    {
        var newTodo = page.GetByPlaceholder("What needs to be done?");
        foreach (var item in Configuration.Items)
        {
            await newTodo.FillAsync(item);
            await newTodo.PressAsync("Enter");
        }
    }
}

public sealed record AddTodosFlowConfig
{
    public string[] Items { get; init; } = [];
}

public sealed class CompleteTodosFlow : BasePlaywrightFlow<CompleteTodosFlowConfig>
{
    public override async Task RunAsync(IPage page)
    {
        foreach (var item in Configuration.ItemsToComplete)
            await page.GetByRole(AriaRole.Listitem).Filter(new() { HasText = item }).GetByRole(AriaRole.Checkbox).CheckAsync();
    }
}

public sealed record CompleteTodosFlowConfig
{
    public string[] ItemsToComplete { get; init; } = [];
}

public sealed class ClearCompletedFlow : BasePlaywrightFlow<ClearCompletedFlowConfig>
{
    public override async Task RunAsync(IPage page)
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Clear completed" }).ClickAsync();
        await Expect(page.GetByTestId("todo-item")).ToHaveCountAsync(Configuration.ExpectedRemaining);
    }
}

public sealed record ClearCompletedFlowConfig
{
    public int ExpectedRemaining { get; init; }
}
```

```yaml
Sessions:
  - Name: Todos
    Probes:
      - Name: ManageTodos
        Probe: PlaywrightFlowProbe
        ProbeConfiguration:
          BaseUrl: https://demo.playwright.dev/todomvc/#/
          Flows: [AddTodosFlow, CompleteTodosFlow, ClearCompletedFlow]
          FlowConfiguration:
            AddTodosFlow:
              Items: [Buy groceries, Walk the dog, Write the probe]
            CompleteTodosFlow:
              ItemsToComplete: [Buy groceries]
            ClearCompletedFlow:
              ExpectedRemaining: 2
Assertions:
  - Name: TodosWork
    Assertion: PlaywrightFlowAssertion
    SessionNames: [Todos]
```

A flow fails by throwing; a Playwright `Expect(...)` that does not hold throws with a clear message.

## Log in first

`SetupFlows` run before `Flows` on the same page, so the flows after the login are logged in.

```yaml
ProbeConfiguration:
  BaseUrl: https://my-app.com
  SetupFlows: [LoginFlow]
  Flows: [CreateOrderFlow, VerifyOrderFlow]
  FlowConfiguration:
    LoginFlow: { Username: admin, Password: secret }
    CreateOrderFlow: { ProductName: Widget Pro, Quantity: 3 }
    VerifyOrderFlow: { ExpectedTotal: "$29.97" }
```

## Log in once for many sessions

The first stage logs in and saves the login; the sessions of the next stage start from it and run in parallel.

```yaml
Sessions:
  - Name: Login
    Stage: 1
    Probes:
      - Name: Browser
        Probe: PlaywrightFlowProbe
        ProbeConfiguration:
          BaseUrl: https://my-app.com
          Flows: [LoginFlow]
          SaveStorageStatePath: state/admin.json
  - Name: Orders
    Stage: 2
    Probes:
      - Name: Browser
        Probe: PlaywrightFlowProbe
        ProbeConfiguration:
          BaseUrl: https://my-app.com
          Flows: [OrdersFlow]
          LoadStorageStatePath: state/admin.json
```

## Parallel sessions as different users

Sessions in one stage run at the same time, each in a browser context of its own, so they can log in as different
users:

```yaml
ProbeConfiguration:
  BaseUrl: https://my-app.com
  SetupFlows: [LoginFlow]
  Flows: [ApproveRequestFlow]
  FlowConfiguration:
    LoginFlow: { Username: manager }
```

## Create every item of a list

`Missions` is a DataSource that generates one item per mission, e.g. `{ "Name": "Apollo", "Priority": "High" }`.
Five workers log in once each, then create the missions between them:

```yaml
Sessions:
  - Name: Missions
    Probes:
      - Name: CreateMissions
        Probe: PlaywrightFlowProbe
        DataSourceNames: [Missions]
        ProbeConfiguration:
          BaseUrl: https://my-app.com
          SetupFlows: [LoginFlow]
          Flows: [CreateMissionFlow]
          ForEach: Missions
          Parallelism: 5
          FlowConfiguration:
            LoginFlow: { Username: planner }
```

```csharp
public sealed class CreateMissionFlow : BasePlaywrightFlow<CreateMissionFlowConfig>
{
    public override async Task RunAsync(IPage page)
    {
        var mission = Item!.Deserialize<Mission>()!;
        await page.GotoAsync($"{BaseUrl}/missions/new");
        await page.GetByLabel("Name").FillAsync(mission.Name);
        await page.GetByLabel("Priority").SelectOptionAsync(mission.Priority);
        await page.GetByRole(AriaRole.Button, new() { Name = "Create" }).ClickAsync();
        await Expect(page.GetByText($"Mission {mission.Name} created")).ToBeVisibleAsync();
    }
}

public sealed record Mission(string Name, string Priority);

public sealed record CreateMissionFlowConfig;
```

The report lists every item, `CreateMissionFlow[0]` to `CreateMissionFlow[49]`.

## Nested settings

Flow settings bind like any QaaS hook's: nested records, arrays and dictionaries work.

```csharp
public sealed record CreateMissionsFlowConfig
{
    public MissionConfig[] Missions { get; init; } = [];
}

public sealed record MissionConfig
{
    public string Name { get; init; } = "";
    public string Priority { get; init; } = "";
    public string[] Members { get; init; } = [];
}
```

```yaml
FlowConfiguration:
  CreateMissionsFlow:
    Missions:
      - Name: Alpha Strike
        Priority: High
        Members: [Alice, Bob]
      - Name: Beta Recon
        Priority: Low
        Members: [Charlie]
```

## Watching a run on your machine

Point the probe at a Chrome on your machine; it starts one when nothing answers there, with a window.

```yaml
ProbeConfiguration:
  BaseUrl: https://my-app.com
  BrowserUrl: http://localhost:9222
  Headless: false   # every action waits 2 s (SlowMo) so you can follow it
  KeepOpen: true    # stay in the Playwright inspector afterwards
  Flows: [LoginFlow]
```

## Another environment

Change `BaseUrl` in the YAML, or override it on the command line by its full path:

```bash
dotnet run -- run test.qaas.yaml -r Sessions:0:Probes:0:ProbeConfiguration:BaseUrl=https://staging.my-app.com
```
