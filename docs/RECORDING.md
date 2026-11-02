# Recording flows

The recorder opens your installed Google Chrome, watches what you do, and saves it as a flow class the probe can run,
with the values you typed and chose as the flow's settings.

```bash
dotnet tool install --global QaaS.Playwright.Recorder            # once; or as a local tool of your test repo

qaas-playwright                                                  # asks for the URL, name and folder
qaas-playwright record login-flow https://my-app.com             # the same, without questions
qaas-playwright record login-flow https://my-app.com --output-dir UiFlows
```

Run it in your test project's folder: the flow is saved in `Flows/` there, in the project's namespace. From a clone
of this repository, `dotnet run --project QaaS.Playwright.Recorder -- record ...` does the same.

Work through the site. Each action is printed as it is recorded, with what it caused:

```text
  ✓ click button "Actions for HL-775-ZE"
  ✓ click menuitem "Schedule maintenance"
      ↳ dialog opened: Schedule maintenance
  ✓ choose [id='maintenanceTypeId'] → Brake inspection
  ✓ fill [name='dueDate'] = 03/11/2026
  ✓ click button "Schedule"
      ↳ dialog closed: Schedule maintenance
      ↳ alert: Maintenance scheduled for HL-775-ZE
```

When you are done, close the browser, or press Enter in the console. The recorder saves two files: `Flows/LoginFlow.cs`
(the name `login-flow` in PascalCase) and `Flows/LoginFlow.flow.yaml`, the values it recorded. It then prints the YAML
to run the flow, with the page you started on as `BaseUrl`.

- Your login is kept between recordings in `~/.qaas/auth.json`, so you log in once. It is saved when the recording
  ends, also when you closed the window. When it cannot be written (the folder is read-only, or the file is open
  elsewhere), the recorder says so and still saves the flow.
- Recording again under the same name never overwrites anything you may have edited: the new recording is saved
  beside the flow as `LoginFlow.recorded.txt` with `LoginFlow.recorded.flow.yaml`, then `LoginFlow.recorded-2.txt`
  and so on, to merge by hand. They are not `.cs` files, so the project still builds while they declare the same class.
- The namespace follows the folder: `Flows/` in a project with root namespace `MyApp.Tests` gives `MyApp.Tests.Flows`
  (a folder named like a C# keyword gets an `@`, e.g. `@internal`).
- A name the flow cannot have is refused before Chrome opens: one of a type the flow's code uses (`task`, `i-page`), a
  Windows device (`nul`, `com1`), or one too long for a file name.

## What gets generated

```csharp
public sealed class ScheduleMaintenance : BasePlaywrightFlow<ScheduleMaintenanceConfig>
{
    public override async Task RunAsync(IPage page)
    {
        var dialog = await OpenScheduleMaintenanceAsync(page);
        await FillScheduleMaintenanceAsync(dialog);
        await ScheduleAsync(dialog);
    }

    private async Task<ILocator> OpenScheduleMaintenanceAsync(IPage page)
    {
        await page.GetByRole(AriaRole.Button, new() { Name = $"Actions for {Configuration.PlateNumber}", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Menuitem, new() { Name = "Schedule maintenance", Exact = true }).ClickAsync();
        var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Schedule maintenance" });
        await Expect(dialog).ToBeVisibleAsync();
        return dialog;
    }

    private async Task FillScheduleMaintenanceAsync(ILocator dialog)
    {
        var maintenanceTypeId = dialog.Locator("[id='maintenanceTypeId']");
        await maintenanceTypeId.ClickAsync();
        await dialog.Page.GetByRole(AriaRole.Option, new() { Name = Configuration.MaintenanceTypeId, Exact = true }).ClickAsync();
        await Expect(maintenanceTypeId).ToHaveTextAsync(Configuration.MaintenanceTypeId);
        await dialog.Locator("[name='dueDate']").FillAsync(Configuration.DueDate);
        await dialog.GetByRole(AriaRole.Radio, new() { Name = Configuration.Priority, Exact = true }).CheckAsync();
    }

    private async Task ScheduleAsync(ILocator dialog)
    {
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Schedule", Exact = true }).ClickAsync();
        await Expect(dialog).ToBeHiddenAsync(new() { Timeout = 30000 });
        await Expect(dialog.Page.GetByRole(AriaRole.Alert).Filter(new() { HasText = $"Maintenance scheduled for {Configuration.PlateNumber}" })).ToBeVisibleAsync();
    }
}

public sealed record ScheduleMaintenanceConfig
{
    public string PlateNumber { get; init; } = "HL-775-ZE";
    public string MaintenanceTypeId { get; init; } = "Brake inspection";
    public string DueDate { get; init; } = "03/11/2026";
    public string Priority { get; init; } = "High";
}
```

```yaml
FlowConfiguration:
  ScheduleMaintenance:
    PlateNumber: 'HL-775-ZE'
    MaintenanceTypeId: 'Brake inspection'
    DueDate: '03/11/2026'
    Priority: 'High'
```

- **Steps.** `RunAsync` calls one method per step. A step ends where a dialog opens or closes, the address changes,
  or a pop-up opens. The step that opens a dialog checks that it shows and returns it; the steps in it work inside it.
- **Settings.** Every value you type, choose, check or pick becomes a property of `<Name>Config`, with the recorded
  value as its default, and the same values go in `<Name>.flow.yaml`: paste that block under the probe's
  `ProbeConfiguration` and change it per case. A setting is named after the field's form name
  (`employee.firstName` gives `EmployeeFirstName`), else its id, test id or English label. Text on the page that
  shows a recorded value, like a row's key or a message, reads the setting too, so each case finds its own row.
- **The first navigation is left out**: the probe opens `BaseUrl` itself, so the same flow runs in every environment.
  A page you open by its address later (typed in the address bar, or a bookmark) is opened from `BaseUrl`
  (`$"{BaseUrl}/orders"`), and so is a tab you open with an address. Addresses the app goes to by itself are left out,
  as it goes there again when the flow runs: a redirect when the page loads, a sign-in round trip, the app's router,
  or a save that takes longer than 5 seconds to move on.

### Locators

For each element the recorder takes the first of these that finds it, and only it, on the page as you saw it:

1. The interactive element itself (the input, not the label or icon you clicked), by its form `name` (Formik's path,
   `[name='employee.firstName']`) or its own stable `id`. Generated ids, like React's `:r1:`, MUI's `mui-12`,
   `-option-3`, `-listbox`, long numbers and GUIDs, are never used.
2. An MUI Select by its `id`, else as the box around its hidden input (`div:has(> input[name='x'])`).
3. Its test id (`GetByTestId`, i.e. `data-testid`).
4. Its role and exact name, without direction marks and a required field's ` *`. A tab whose name counts something
   (`Open (3)`) by the start of its name.
5. In a table: the row by its key cell's text, then the element in it.
6. Last, Playwright's own locator, listed under "Check by hand".

An element in a dialog is located inside it (`dialog.GetByRole(...)`). The options of a select or autocomplete open
outside the dialog, so they come from `dialog.Page`.

### What each action expects

- A choice: the field shows it (`ToHaveTextAsync` for a select, `ToHaveValueAsync` for an autocomplete, which is typed
  into first).
- A tab: `ToHaveAttributeAsync("aria-selected", "true")`.
- A click that opened a dialog: the dialog is visible. One that closed it: `ToBeHiddenAsync`, with 30 seconds for the
  save behind it.
- A message that appeared (`role="alert"`): visible, with its text.
- A new address: `ToHaveURLAsync`, with ids in the path matched as any value. In an app that routes by the fragment
  (`#/orders/5`), the route is checked the same way.

The flow never clicks through JavaScript, sleeps, checks styles, asks `IsVisibleAsync`, picks the `Nth` or `First`
match, or catches exceptions: what the page should do is expected, and a run fails where it does not.

## What to check by hand

When it saves a flow, the recorder lists the lines to look at, with their numbers and why:

- **A locator that did not find the element alone**, or Playwright's own locator when it relies on position, a style
  class or a generated id.
- **Uploads.** The browser tells only the name of a file you upload. The flow asks its `FileToUpload` method for each
  file, which throws until you make it return the file's full path, e.g. from a setting.
- **Browser dialogs** (alert, confirm): the flow accepts them. Dismiss where you dismissed. One that showed before
  your first action on a page is listed without a line: the flow cannot answer it.
- **Menus that opened on hover**: add `HoverAsync()` on what opens them.
- **Drag and drop**: check where it drops. A drag the recorder could not follow is saved as a hover.
- **Pop-ups and tabs you opened yourself**: check what the flow does in them.

Not recorded at all: what you do inside an iframe. Picking a date from a calendar is recorded as typing the date the
field then shows. Only Enter and Escape are recorded as key presses; typing is recorded as the field's value.

## How it works

The recorder runs its own Chrome session through Playwright, not Playwright's codegen. A script added to every page
listens, without changing anything, to what you click, type and choose, and reports each action with the candidates
for its locator, checked against the page with Playwright's own selector engine. That engine is only available when
the driver runs with `PWDEBUG=console`, which the recorder sets for its own driver alone. The recorder then plans the
steps, names the settings and writes the flow.

## Tips

- One flow per task: log in in one flow, create an order in another, so they combine in `SetupFlows` and `Flows`.
- Give the fields your app's forms a `name` or a stable `id`, and buttons a visible name; the recorder needs nothing
  else.
- If the recorded page looks different in the test run (e.g. a date picker is a read-only field), the test's Chrome
  reports no mouse: see [EmulateDesktopPointer](CONFIGURATION.md#mobile-layout-in-a-headless-chrome-emulatedesktoppointer).
