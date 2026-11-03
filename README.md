# QaaS.Playwright

Browser tests for [QaaS](https://github.com/TheSmokeTeam). Record a flow in Chrome, get a readable C# class, and run
it from a QaaS test: `PlaywrightFlowProbe` runs the flows, and `PlaywrightFlowAssertion` reports which passed and why
one failed, with a screenshot.

- **A recorder that writes clean code.** Stable locators (form field names, ids, roles), one method per step, a check
  after every action, and every typed or chosen value as a setting. While you record, a box under the pointer shows
  what a click records and why, and a panel beside the browser shows the flow as it is written and lets you pick
  another locator before you save. See [Recording flows](https://github.com/arielgindi/QaaS.Probes.Playwright/blob/master/docs/RECORDING.md).
- **Typed, validated settings.** Each flow binds its own settings record; a typo or a wrong value fails the session
  before the browser opens, with every problem listed.
- **Fast and isolated.** Sessions run in parallel in browser contexts of their own; a login can be saved once and
  reused; `ForEach` runs a flow once per generated item, in parallel.
- **Failures you can act on.** The flow, the reason, the element it waited for, the page URL, a screenshot, and
  secrets hidden everywhere.

## Requirements

- .NET 10 SDK
- Google Chrome, on your machine or a remote one over CDP (for example [Browserless](https://www.browserless.io/))
- A QaaS test project (QaaS.Runner)

## Quick start

1. **Record a flow.** Install the recorder once, then run it in your test project's folder:

   ```bash
   dotnet tool install --global QaaS.Playwright.Recorder
   qaas-playwright record login-flow https://my-app.com
   ```

   Work through the site, then close the browser. The flow is saved as `Flows/LoginFlow.cs` with its values in
   `Flows/LoginFlow.flow.yaml`, and the lines worth a second look are listed.

2. **Reference the library** from the test project:

   ```xml
   <PackageReference Include="QaaS.Playwright" Version="1.2.1" />
   ```

   Also add `<WarningsAsErrors>CS4014</WarningsAsErrors>`: a flow that forgets to `await` a Playwright call then
   fails the build instead of passing before the call fails.

3. **Run it** from a session, and assert on it:

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

The probe opens `BaseUrl`, then runs `SetupFlows` and `Flows` in order on one page; the first flow that fails stops
the run. By default it uses the Chrome at `http://localhost:9222`, and starts one there if none answers; set
`BrowserUrl` to use another. `ENV=local` or `Headless: false` always run in that local Chrome.

## A flow

A flow is a class with a typed settings record; the recorder writes them, and you can write or edit them by hand:

```csharp
public sealed class LoginFlow : BasePlaywrightFlow<LoginFlowConfig>
{
    public override async Task RunAsync(IPage page)
    {
        await page.GetByLabel("Username").FillAsync(Configuration.Username);
        await page.GetByLabel("Password").FillAsync(Configuration.Password);
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign in" }).ClickAsync();
        await Expect(page.GetByText($"Hello, {Configuration.Username}")).ToBeVisibleAsync();
    }
}

public sealed record LoginFlowConfig
{
    public string Username { get; init; } = "";
    public string Password { get; init; } = "";
}
```

## Documentation

- [Recording flows](https://github.com/arielgindi/QaaS.Probes.Playwright/blob/master/docs/RECORDING.md): the recorder, the code it writes, and what to check by hand
- [Configuration](https://github.com/arielgindi/QaaS.Probes.Playwright/blob/master/docs/CONFIGURATION.md): every setting, the browser, logins, parallel runs, `ForEach`, and the report
- [Examples](https://github.com/arielgindi/QaaS.Probes.Playwright/blob/master/docs/EXAMPLES.md): complete flows and YAML to copy
- [Known issues](https://github.com/arielgindi/QaaS.Probes.Playwright/blob/master/docs/KNOWN-ISSUES.md): QaaS and Playwright behavior a suite has to know about
- [Architecture](https://github.com/arielgindi/QaaS.Probes.Playwright/blob/master/docs/ARCHITECTURE.md): how the plugin fits into QaaS
- [Changelog](https://github.com/arielgindi/QaaS.Probes.Playwright/blob/master/CHANGELOG.md)

## Building from source

```bash
dotnet build
dotnet test
```

The tests run the real probe and assertion against a headless Chrome they start themselves and a small web app
served in-process, and check the code the recorder writes. The Chrome tests are skipped when Chrome is not installed.

To build against your own package feed, add a `NuGet.config` for it and, for versions the feed has instead of the ones
pinned in `Directory.Packages.props`, a `Directory.Packages.Local.props` beside it:

```xml
<Project>
  <ItemGroup>
    <PackageVersion Update="Microsoft.Playwright" Version="1.50.0" />
  </ItemGroup>
</Project>
```
