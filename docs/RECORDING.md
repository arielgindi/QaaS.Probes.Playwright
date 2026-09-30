# Recording flows

The recorder wraps Playwright codegen. It opens your installed Google Chrome, records what you do, and saves it as a
flow class the probe can run.

```bash
dotnet run --project QaaS.Playwright.Recorder                                          # asks for the URL, name and folder
dotnet run --project QaaS.Playwright.Recorder -- record login-flow https://my-app.com  # the same, without questions
dotnet run --project QaaS.Playwright.Recorder -- record login-flow https://my-app.com --output-dir UiFlows
```

Click through the site, then close the browser. The recorder saves `Flows/LoginFlow.cs` (the name `login-flow` in
PascalCase) and prints the YAML to run it, with the page you started on as `BaseUrl`.

- Your login is kept between recordings in `~/.qaas/auth.json`, so you log in once.
- Elements with a `data-testid` attribute are recorded as `GetByTestId(...)`, and the probe resolves them the same
  way (the attribute is set in `browser-defaults.yaml`).
- Recording again under the same name never overwrites anything you may have edited: the new recording is saved
  beside the flow as `LoginFlow.recorded.txt`, then `LoginFlow.recorded-2.txt` and so on, to merge by hand. They are
  not `.cs` files, so the project still builds while they declare the same class.
- The namespace follows the folder: `Flows/` in a project with root namespace `MyApp.Tests` gives `MyApp.Tests.Flows`
  (a folder named like a C# keyword gets an `@`, e.g. `@internal`).

## What gets generated

```csharp
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using QaaS.Playwright;
using static Microsoft.Playwright.Assertions;

namespace MyApp.Tests.Flows;

/// <summary>
/// Recorded browser flow. To parameterize: add properties to <see cref="LoginFlowConfig"/>,
/// then replace hardcoded values with Configuration.PropertyName.
/// </summary>
public sealed class LoginFlow : BasePlaywrightFlow<LoginFlowConfig>
{
    public override async Task RunAsync(IPage page)
    {
        await page.GetByLabel("Username").FillAsync("admin");
        await page.GetByLabel("Password").FillAsync("secret");
        await page.GetByRole(AriaRole.Button, new() { Name = "Log in" }).ClickAsync();
    }
}

/// <summary>Configuration for LoginFlow. Pass values from FlowConfiguration:LoginFlow:.</summary>
public sealed record LoginFlowConfig;
```

The first navigation is left out: the probe opens `BaseUrl` itself, so the same flow runs in every environment.
Assertions you record (`Expect(...)`) are kept, and so are pop-ups: codegen's wait for one becomes
`var page1 = await page.RunAndWaitForPopupAsync(...)`, and what you did in it uses `page1`. Recorded text, such as a
label or a typed value, is kept exactly as recorded.

## Parameterizing a flow

Move the values that change into the settings record, and read them from `Configuration`:

```csharp
public sealed record LoginFlowConfig
{
    public string Username { get; init; } = "";
    public string Password { get; init; } = "";
}

await page.GetByLabel("Username").FillAsync(Configuration.Username);
await page.GetByLabel("Password").FillAsync(Configuration.Password);
```

```yaml
FlowConfiguration:
  LoginFlow:
    Username: admin
    Password: secret
```

Use `BaseUrl` for pages the flow opens itself: `await page.GotoAsync($"{BaseUrl}/orders");`.

## Tips

- One flow per task: log in in one flow, create an order in another, so they combine in `SetupFlows` and `Flows`.
- Prefer labels, roles and test ids over CSS selectors; they survive redesigns.
- If the recorded page looks different in the test run (e.g. a date picker is a read-only field), the test's Chrome
  reports no mouse: see "Mobile layout" in the [README](../README.md).
