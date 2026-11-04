# Recording flows

The recorder opens your installed Google Chrome, watches what you do, and saves it as a flow class the probe can run,
with the values you typed and chose as the flow's settings.

```bash
dotnet tool install --global QaaS.Playwright.Recorder            # once; or as a local tool of your test repo
qaas-playwright                                                  # opens the recorder's window
```

It takes no options: everything is set up in its window. `qaas-playwright --version` prints the version and
`--help` says where things are kept; anything else is said in one line, and the window opens anyway. From a clone of
this repository, `dotnet run --project QaaS.Playwright.Recorder` does the same.

### The start screen

The recorder opens a window of Chrome on its start screen; the app's window opens only when you press **Start**,
beside it, and the same window then becomes the [panel](#the-panel). From top to bottom:

- **Project**: the test project the flows are saved in, found from where you started the recorder (the nearest
  `.csproj` at or above that folder, else that folder), else the one you recorded in last. Started from a shortcut,
  a double-click or your home folder, it asks for one. **Browse...** opens a folder browser of the window's own, which
  marks the folders that hold a project or a solution; a path can be pasted too. **Save flows in** is the output
  folder, `Flows` by default, with the namespace its flows get.
- **What to record**:
  - **New flow**: its **Name** (`new-order` becomes the class `NewOrder`, in `Flows/NewOrder.cs`), checked as you
    type, and the **Start page**, which offers the pages you recorded on here and every probe `BaseUrl` of your test
    YAML. When a flow of that name is there already, you choose: **Continue it**, **Replace it with a new
    recording**, or another name.
  - **Continue a saved flow**: the flows of the folder, each with when it was recorded, its start page and the login
    it ran after ([continuing a flow](#continuing-a-flow)).
  - **Recovered**: shown only when a recording never ended in a save ([nothing is lost](#nothing-is-lost)).
- **Log in first**: the login flow of your tests, found by itself, with a switch to turn it off
  ([logging in first](#logging-in-first)).
- **Options**: **Auto checks**, on by default ([what each action expects](#what-each-action-expects)), and **Keep the
  raw recording beside the flow**, with every locator tried, to continue it anywhere.
- **Start** (`Ctrl+Enter`), which says what it will do, e.g. `Chrome opens https://shop.test/orders, logs in with
  LoginFlow, then records NewOrder into Flows/NewOrder.cs.`, and is off, saying why, while a field is wrong.

What you choose is remembered for each project in `~/.qaas/recorder-projects.json`: the output folder, the last five
start pages, the switches, the login's values file, and the values you typed that are not secrets. The console asks
nothing: it says what goes on, and prints each action as the panel shows it.

Work through the site. Each action is printed as it is recorded: its number, what you did, the element's name and
the value, then dimmer the locator the flow will use, and under it what the page did. The numbers are the step
numbers the panel, a test run and the summary use, the flow's: a click that only opens a select is no step, and a
click on no control shows only once it changed something, as the flow leaves it out otherwise. "Enter in a form's
field may send the form" is said of a field in a form only:

```text
    1 • click   Actions for HL-775-ZE         button "Actions for HL-775-ZE"
    2 • click   Schedule maintenance          menuitem "Schedule maintenance"
        └ opened dialog  Schedule maintenance
    3 ▼ choose  Maintenance type → Brake inspection  [id='maintenanceTypeId']
    4 ≡ fill    Due date = 03/11/2026         [name='dueDate']
    5 ■ pick    High                          radio "High"
    6 ≡ fill    Notes = Brakes squeak         [name='notes']
    7 • click   Schedule                      button "Schedule"
        └ alert  Maintenance scheduled for HL-775-ZE
        └ closed dialog  Schedule maintenance
```

The verbs are the panel's: a checkbox is ticked (`tick`, `untick`) and a radio picked (`pick`), as `check` is a
[check](#checking-what-the-page-shows) of what the page shows. What the recorder is doing shows in a line of its own,
its word in a column, as the panel's status strip says it: `● Recording`, `║ Paused`, `◇ Trying`, `● Re-recording`,
`› Testing`, then `✓ Passed` or `× Failed`.

A step to [check by hand](#what-to-check-by-hand) gets an amber `▲` line under it, saying why; Enter pressed in a
form's field gets a dim line saying it may send the form, as a Submit does. Passwords and other secrets show as
`••••••••`. Long names and locators are cut to the console's width, and right-to-left names keep their place in the
line. The console is coloured when it is a terminal; `NO_COLOR` turns colours off and `FORCE_COLOR` on (e.g. for a CI
log), and a console that cannot show the glyphs gets ASCII ones.

### What a click records

While you record, a box follows the pointer over the element a click there records: the control the click is for, the
card or option it picks, as the recording finds it (see
[Which element a click records](#which-element-a-click-records)), not the text or icon right under the pointer. Its label says, at a glance, what a click does there and on what, then
the locator the flow will use, as the console prints it, with how many elements it found: a green dot when it finds
this one alone, an amber one, with a line saying why, when it needs a look. Rest the pointer on the element and the
label adds why each locator tried before it lost, leaving out those the element has none of:

```text
Click  button “Save order”
●  button "Save order"   1 match
   id: found 2 (1 hidden)
```

Here the button's id is not used: two elements have it, one of them hidden (a copy a responsive layout keeps for
another width), so the flow finds it by its role and name. The reasons:

| Reason | Means |
|--------|-------|
| `found 2`, `found 2 (1 hidden)` | It finds more elements than this one, some of them maybe hidden. |
| `found another element` | It finds one element, but not this one. |
| `found none` | It finds nothing where the flow looks, e.g. inside the dialog the element is in. |
| `looks generated` | The value looks generated by a framework (`:r1:`, `mui-12`, a GUID, a long number): it changes between builds. |
| `none`, `no name` | The element has none: no id, no test id, no role, or a role without a name. |

An amber line under the locator is what "Check by hand" will list for it, such as Playwright's own locator picking the
element by its position. A grey line says when the element is not a control: a click on it stays in the flow only if
the page reacts (a dialog, a message, a new address).

It is one box in every mode, its colour and the tag of its label saying which: indigo while recording (the tag is the
verb: `Click`, `Fill`, `Choose`, `Tick`, `Pick`), amber for an element picked with `Alt+↑` (`Picked · Click`), green
in check mode (`Check`), teal while trying (`Try · Click`), violet in inspect mode (`Inspect`), and indigo again for
what the panel points at (`Step`, `Locator`, `Element`). The box glides from element to element, and the label keeps
the last element's words, dimmed, until the new ones come, so passing over many elements is quiet; both are drawn
once a frame, without animation when the system asks for reduced motion. The label goes below the element, else above
it, else inside it, always on the screen and never over the pointer. The overlay steps aside while you drag or select
text, or while the pointer is on a scrollbar, and comes back as you let go; once you click an element, it steps aside
until the pointer moves onto another one, as a menu or a list the click opens shows where the label was.

When the recording is in another mode than recording, a pill at the top of the page says which, in its colour, and the
key that leaves it: `Paused: nothing you do is recorded; F8 resumes`, `Trying · F7`, `Check · Esc`, `Inspect · Esc`, the part being recorded
again with `F2`, or the login flow's progress; while the flow is tested or replayed, it is at the bottom, off the app's
header. It fades as the pointer comes near it, so what is under it can be seen and clicked.

The overlay never changes the page: it is drawn in a closed shadow root on an element of its own outside `<body>`,
takes no pointer events, no focus and no room, and Playwright's selectors do not see into it. It shows in the main page,
not in frames, which are not recorded; with the focus in a frame, `F8`, `F7` and `F2` still work.

| Key, in the browser | Does |
|---------------------|------|
| `F6` | Checks something on the page: the next click picks an element, then you choose the check (see [Checking what the page shows](#checking-what-the-page-shows)). |
| `F8` | Pauses or resumes the recording (see [Pausing](#pausing)). |
| `F4` | Freezes the page's elements for the panel's [Elements view](#the-elements-view), e.g. with a menu open. |
| `Ctrl+Shift+C` | Picks an element for the panel's [Elements view](#the-elements-view): the box follows the pointer, and a click offers what is under it without clicking it (see [Picking an element](#picking-an-element)). `Esc` leaves. |
| `F9` | Hides or shows the overlay. Hidden, the recorder records as it would without it. |
| `F7` | Tries things without recording them: each action shows in the panel as the recorder would write it (see [Trying things](#trying-things)). |
| `F2` | While a part of the flow is recorded again, ends it and keeps what you did (see [Recording a part again](#recording-a-part-again)). Otherwise it is the page's. |
| `Alt+↑` | Picks the element around the one in the box (the box turns orange): the next click records it instead. |
| `Alt+↓` | Goes back toward the element under the pointer, the same way. |

Once you pick, the label shows the way as a breadcrumb, from the outside in, with the element picked marked:
`div.paper › div.cardGrid › div.card › span.cardTitle`, each by its tag and its id or first class, without the hash a
framework puts in a class (`css-1x2y3z-card` shows as `card`). The way goes out of a web component's shadow root, passes
over what takes no room (a slot), and ends at the dialog the element is in, or below the root of the app or of the
portal a menu is in: the page's own containers are never offered. A pick holds while the pointer moves within the
element picked, and is dropped when it leaves it. These keys do nothing in Chrome by themselves, type no text, and
cannot be a page's access key; they still reach the page as for a person, so with a select or an autocomplete field
focused, `Alt+↓` also opens its list. On Windows, `Alt` pressed and let go alone puts the focus on Chrome's menu, but
not when an arrow is pressed with it, as here. On macOS, `Alt` is `Option`, and the function keys may need `fn`.
Chrome's own `F7`, caret browsing, is kept from it: its prompt would take the focus and close the very menu being
frozen, which is also why freezing is on `F4`. So is `Ctrl+Shift+C`, which would open Chrome's developer tools.

When you are done, click **Save** in the [panel](#the-panel), or close the app's window. Ctrl+C in the console saves
too; pressed again before the flow is written, it stops at once, and the recording is kept, to save from the start
screen. The recorder saves two files: `Flows/LoginFlow.cs` (the name `login-flow` in PascalCase) and
`Flows/LoginFlow.flow.yaml`, the values it recorded. The window then shows what was saved: the files, how many steps
and actions the flow has, what to set before it runs, and each line to check by hand, with **Record another**,
**Continue** it and **Open the folder**; closing the window ends the recorder. The console prints the same summary,
with the YAML to run the flow and the page you started on as `BaseUrl`.

- Your login is kept between recordings in `~/.qaas/auth.json`, so you log in once. It is saved when the recording
  ends, also when you closed the window. When it cannot be written (the folder is read-only, or the file is open
  elsewhere), the recorder says so and still saves the flow. With a [login flow](#logging-in-first), the recording
  starts from no login instead, as the probe does, and the login flow logs in.
- Recording again under the same name never overwrites anything you may have edited: the new recording is saved
  beside the flow as `LoginFlow.recorded.txt` with `LoginFlow.recorded.flow.yaml`, then `LoginFlow.recorded-2.txt`
  and so on, to merge by hand. They are not `.cs` files, so the project still builds while they declare the same class.
  The start screen offers instead to [continue](#continuing-a-flow) the flow there, replace it, or take another name.
- The namespace follows the folder: `Flows/` in a project with root namespace `MyApp.Tests` gives `MyApp.Tests.Flows`
  (a folder named like a C# keyword gets an `@`, e.g. `@internal`). The project is the nearest one the flow can be
  built in, which references QaaS.Playwright; a project of something else above the folder, such as a tool's above a
  scratch folder, names nothing, and the namespace is `Flows`.
- A name the flow cannot have is refused before Chrome opens: one of a type the flow's code uses (`task`, `i-page`), a
  Windows device (`nul`, `com1`), or one too long for a file name. The start screen says why under the field; so it
  does for an address that is not http(s), and offers it with `https://` when the scheme is missing.

### Nothing is lost

While you record, the recording is written to `~/.qaas/recordings/` after every change: one file per flow and folder,
so two projects' `LoginFlow`s do not meet, written whole or not at all, readable by you only (on Windows, as the
folder allows), in the format of the raw recording kept beside the flow. It never holds a password you typed, as the console never
shows one, nor what you typed into a field whose setting is named as a secret (password, secret, token, API key,
pwd), as a test run hides those, nor a value you gave such a setting in the panel. It keeps your checks and your
edits in the panel, so a recording recovered or continued has them too. Once the flow is saved, the file stays, marked saved, for
[continuing](#continuing-a-flow) the flow; the newest 30 are kept.

When the recorder starts and finds a recording that did not end in a save (the recorder crashed or was stopped with
Ctrl+C twice, or the flow could not be saved), the start screen opens on its **Recovered** tab: `NewTicket · 12
actions · 5 minutes ago`, this project's first, with **Save it now**, **Continue recording** and **Discard...**, which
asks to confirm in place. Every warning and error says what happened, the likely cause, and what to do next.

An end nobody asked for never goes unsaid: an error the recorder did not expect, Chrome or its driver going away, or a
signal from outside (`kill`, a closed terminal: SIGTERM, SIGHUP, SIGQUIT) writes the recording as it is then, prints
`× The recorder was stopped from outside (SIGTERM). The recording is kept: ...` and writes what happened, with its
stack, to `~/.qaas/recorder-error.log`. Only `kill -9` ends it with nothing said, as no program sees that one; the
autosave of half a second before it is offered then.

**Save it now** writes the flow and its YAML, and the raw recording when it was recorded with Keep the raw recording
on, as a save at the end of the recording would, without opening Chrome, and shows what was saved; **Continue
recording** goes on from it as [continuing](#continuing-a-flow) a flow does; **Discard** deletes it. A file it cannot
read is skipped with a warning. A flow saved from it types an empty password, as the
recording holds none: the recorder says which setting to set in its YAML.

### Continuing a flow

On the start screen, **Continue a saved flow** lists the flows of the project's folder; pick one and press **Continue
NewTicket**. The same choice is offered when you name a new flow like one that is there, and on what was saved.

Chrome opens, your [login flow](#logging-in-first) runs first as usual, then the page the flow started on opens, and the flow's
steps replay in the recorded window itself, as **Test** runs them: built as saved, through the probe's runner, with its
timeouts and the flow's values. Nothing is recorded meanwhile; the overlay says `Testing the flow`. Then the recording
goes on from where they left the app: what you do is added after them, numbered on, and the panel shows a divider
`Continued from NewTicket.cs` in its steps.

- **When a step fails**, the console and the panel show it as a test run does, and the panel's status strip asks:
  **Keep recording from here**, without the steps from the failed one on (you record them again), or **Stop**, which
  keeps the flow as it is. When the login flow fails, nothing is replayed, and the recorder stops.
- **Saving regenerates the whole flow** and replaces `NewTicket.cs` and its YAML, unless you changed the `.cs` since it
  was recorded, or you kept recording without steps that did not replay: then the flow stays as it is, the flow
  continued is saved beside it as `NewTicket.recorded.txt`, as a re-recording is, and the recorder says so.
- **A flow that logs in itself**, its first actions typing a password, was recorded signed out: it is replayed, and
  tested, signed out too, never from the login the recorder saved for its Chrome, which would skip its login page.
- **The recording** is the recorder's own copy in `~/.qaas/recordings/`, or `NewTicket.recording.json` beside the flow
  (Keep the raw recording), whichever is newer. A flow recorded with an older version, on another machine, or before
  the last 30 saved has none: the start screen says so. Record with Keep the raw recording on to continue a flow
  anywhere.
- The password and the secrets the recorder's copy does not hold come from the flow's YAML, so the replay types them.
  The flow keeps its auto checks, or their absence. One recorded after a login flow logs in with it again: as given,
  found or remembered for the folder, else as it was recorded.
- Continuing needs the .NET SDK, as **Test** does.

### Logging in first

Your tests log in with a flow of their own, e.g. `LoginFlow`, which the probe runs before the others
(`SetupFlows: [LoginFlow]`). The recorder runs it too, so the recording starts logged in. Choose your test project's
folder, or the repository or solution folder above it, and the start screen's **Log in first** card finds the flow by
itself, as QaaS finds it when the tests run, and shows it: `LoginFlow`, from `bin/Debug/net10.0/YourShared.dll`, the
setup flow of 4 test files, its BaseUrl and where it comes from, and each of its values with where it comes from. Its
switch turns it off; when the tests have more than one flow that may log in, a select chooses among them.

**What the recorder does by itself:**

- **It finds your test project**: the nearest `.csproj` from the folder the flow is saved in, up; else from the current
  folder, up. Run from a folder above it, e.g. the repository's root or a solution's folder, it looks up to 3 folders
  down (skipping `bin`, `obj`, `node_modules` and hidden folders) and in the `.sln` and `.slnx` files there, and takes
  the projects that use QaaS: those whose YAML runs `SetupFlows`, else those whose restore (`obj/project.assets.json`)
  took a QaaS package. When several do, the card asks which, once, and remembers the answer for the folder.
- **It finds its newest build**: `bin/Debug/<tfm>/<Project>.dll`, or `bin/Release/...`. When there is none there, or it
  is older than the project, it asks MSBuild where the project builds to (`dotnet msbuild <csproj>
  -getProperty:TargetPath`, in Debug and Release), so a `Directory.Build.props` with `OutputPath` or `BaseOutputPath`,
  a repository-level `bin/`, or `UseArtifactsOutput` (`artifacts/bin/<project>/<config>/`) are found too. MSBuild's
  answer is kept in `~/.qaas/recorder-builds.json` until the project, or a `Directory.Build.props` or `.targets` above
  it, changes. When there is no build, or the project changed since, the card offers **Build now**, saying the command
  (`dotnet build`, or `dotnet build Tests/MyTests.csproj` from another folder); the status strip says `Building` while
  it builds, with **Stop**, and the card shows the errors if it fails.
- **It finds the flow** as QaaS does: in your project's DLL and everything it uses, its dependency closure from its
  `.deps.json` (a package a library's build does not copy is found in your NuGet package folder) and the DLLs of its
  output folder; an `IPlaywrightFlow` matched by its class name, case-insensitively. A name two flows have is said, as
  QaaS refuses it too. It takes the one your test YAML files (`*.yaml`, `*.yml`, outside `bin` and `obj`) run in
  `SetupFlows` most, else the one named like a login (`Login`, `LogIn`, `SignIn` or `Auth`). More than one: the card
  offers them to choose.
- **It says when your login flow cannot run in it.** A shared package of your team may be built against another
  QaaS.Playwright than the recorder's. When the flow uses what the recorder's has not, the recorder says so before
  Chrome opens, rather than failing in it:

  ```text
  ▲ LoginFlow (in Acme.Shared.dll) was built against QaaS.Playwright 1.15.1, and the recorder runs 2.0.3: it can't
    load it (BrowserDefaults.RemoteUrl is gone). Update that package to QaaS.Playwright 2.0.x, then build your tests
    again. Until then, turn Log in first off and log in by hand.
  ```

- **It finds its values** in the `FlowConfiguration: LoginFlow:` block of a test that runs it, and says whether it
  needs any (below), and **its BaseUrl** in the same test: the `BaseUrl` of the probe that runs it
  (`ProbeConfiguration: BaseUrl:`), with `${NAME ?? default}` resolved as QaaS resolves it. When no test gives one, it
  takes the address of the URL you record on, without its path.
- **It remembers the choice per folder**, in `~/.qaas/recorder-logins.json`, as you press Start. The console says
  `Logging in with LoginFlow, from bin/Debug/net10.0/YourShared.dll, its values from Tests/Smoke.qaas.yaml, BaseUrl
  https://my-app.com (from Tests/Smoke.qaas.yaml)…`, and find the flow again in the newest build.
- **It never stays silent.** When it finds no login flow, the card says why and where it looked, e.g.
  `No login flow: no test project in /home/me/repo, above it or up to 3 folders below it.` Turned off, it stays off
  in that folder until you turn it on.
- **Why this flow?** (or **Why no login?**) on the card shows the whole search, with **Copy** for a bug report: the projects found and why each was
  taken or not, the build used and how it was found, the DLLs looked through and the QaaS.Playwright each was built
  against, the flows found and why each was chosen or not, and what recording here would do.

**Where the DLL is.** In your test project's bin folder, once built: `bin/Debug/net10.0/`, or where its
`Directory.Build.props` builds it. Your project's own DLL is there, `MyTests.dll`, and the DLL of each package it
references, e.g. a shared package of your team that has the login flow: `bin/Debug/net10.0/YourShared.dll`. The
recorder looks in both.

**Whether it needs values.** Look for its block in your test YAML, under the probe's `ProbeConfiguration`:

```yaml
ProbeConfiguration:
  BaseUrl: ${APP_URL ?? https://my-app.com}
  SetupFlows: [LoginFlow]
  FlowConfiguration:
    LoginFlow:
      Username: smoke-user
      Password: ${LOGIN_PASSWORD}
```

No block: it needs no values, or keeps its defaults. The recorder says which: `It needs no values`, or
`Its values (Username, Url) come from Tests/Smoke.qaas.yaml`. A value it needs that is set nowhere (a required one, or
text that is empty unless set, as a user name is) gets a field on the card: type it there, or add the block to your
test YAML, under the probe's `ProbeConfiguration`. A secret's field (a password, a token) is masked, and what you type
in it is kept for this run of the recorder only, never written anywhere; other values typed there are remembered for
the project. **Values from** chooses the YAML the values are read from, or none, for the flow's defaults. `${NAME}`
reads an environment variable, and `${NAME ?? default}` has a default, as in QaaS, so a password need not be in the
file.

**How it runs.** As the probe runs a setup flow: created by its class name, given its `BaseUrl` (above) and a QaaS
`Context` whose logger writes to the console, its values bound and checked as strictly as the probe checks them, then
run first, in the order the probe that runs it in your tests runs its setup flows: on its `BaseUrl`, opened first; or,
when that probe sets `SetupFlowsBeforeBaseUrl: true`, on a page that has loaded nothing yet, before `BaseUrl` opens. So
a login flow that plants what the app needs before its first page loads, e.g. a token with
`page.AddInitScriptAsync(...)` that a proxy asks for, has it on every page after, while you record too. Once it logged in, the page goes to the URL you record on, unless it is there already; when that URL is
the app's root and the login left the app on a page of its own, the recording starts there, and the console says so.
The panel's **Test**, a flow continued and recording a part again run it in the same order. The DLLs load from their
folder and their dependency closure; QaaS.Playwright, Playwright and QaaS's SDK are the recorder's own, so the flow
runs on the recorder's pages whatever versions your project was built with, unless it uses what the recorder's have
not (above).

- **You see how it goes.** The console, a pill at the top of the page and the panel say
  `Logging in with LoginFlow… 12 s, on /signin`, the page's address without its query, secrets hidden, and what it
  did last there (`typed in “Username”`, `clicked button “Sign in”`, never a value). After 5 seconds they add
  `Taking long? Press Esc to stop the login flow and log in by hand in the browser.`
- **Esc stops it**, in the page, as does **Log in by hand** in the panel: its page is replaced by a
  fresh one at the same address, keeping the login so far, so whatever it waited for fails at once and it does nothing
  more. It also stops by itself after 2 minutes in which no page loaded and nothing went to the app or came back,
  saying what it waited for and the BaseUrl it was given, and when the app asks the browser to sign in (with Windows,
  or a user name and password of HTTP authentication), which a flow cannot answer. Its own timeouts are the probe's.
- **Nothing the login flow does is recorded**: not its typing and clicks, the pages it goes to, the pop-ups it opens,
  or the browser dialogs it gets, which Playwright dismisses unless the flow handles them, as in the probe.
- **The flow you record runs after it.** Its doc comment says so, and the console's YAML hint adds that it needs
  `SetupFlows: [LoginFlow]`, with the login's values, before it in the probe.
- **When the login flow fails or is stopped**, the console and the panel show why, as the probe reports it, secrets
  hidden, with the BaseUrl it was given and the page it was on: `LoginFlow waited 15 s on https://my-app.com/x/login:
  was BaseUrl right? It was given BaseUrl https://my-app.com/x, from Tests/Smoke.qaas.yaml.` The recording stays
  paused: log in by hand, then resume it (`F8`, or **Resume** in the panel), and nothing you did is recorded. To give
  up, close the browser.

### Pausing

Press `F8` in the recorded window, or **Pause** in the panel, to stop recording for a while, e.g. to look something up,
or to get the app where the next step starts without those clicks in the flow. While paused, nothing you do in the app
is recorded, and the page, the panel and the console all say `Paused: nothing you do is recorded; F8 resumes`; the
overlay shows no box, as it suggests no locator. A click, typing or a choice made while paused shows a toast beside it,
`Paused: that click was not recorded · F8 resumes`, and the first one in a pause prints a line in the console too.
`F8` again, or **Resume**, records from the page the app is on then. What you typed just before pausing is kept, from
the panel as with `F8`. A page opened, an address gone to or a browser dialog shown while paused is no later action's
doing.

If the app is on another page when you resume (you went there while paused), the flow goes to that address itself, as
if you had typed it (`await page.GotoAsync($"{BaseUrl}/orders/5")`), and lists the line under "Check by hand": the step
after the pause then runs on the page it was recorded on, where a replay would otherwise still be on the page before
the pause, but what led there was not recorded, and an address with a record's id in it may not exist on another run.
A tab that opened while paused is opened the same way. When the app is on the same page, nothing is added.

## The panel

Beside the recorded window, the recorder opens its panel: the flow exactly as it will be saved, written as you work.

- **The top bar**: the flow's name, what the middle shows, and the commands: **Pause**, **Check** and **Try**, each
  coloured while it is on, **Undo**, **Test** with slow motion beside it, and **Save**; then every command (`Ctrl+K`,
  in the panel or in the app, where Chrome's own search would take it otherwise), the keys (`?`) and the theme. A narrow panel keeps their icons, each named by its tooltip.
- **The status strip**, under it: the one place that says what the recorder is doing and how to leave it, in the
  mode's colour, with the button and the key that end it: `Recording`, `Paused` (**Resume**, `F8`), `Logging in`
  (**Log in by hand**), `Testing` with the step that runs (**Stop**, `Esc`), `Trying` (**Stop trying**, `F7`), `Check`
  and `Inspect` (`Esc`), `Re-recording` (**Done**, `F2`; **Keep it as it was**, `Esc`). A test's result shows there
  too, `Passed` or `Failed at step 4 of 6` with **Show step** and **Run again**; while another mode is on, the result
  is a small chip at the strip's end. It never grows or moves the panes.
- **Steps**, on the left: a card for each action, under the step (method) it is part of, with the value it typed or
  chose, the locator it uses, the checks the flow makes after it (a tick for an auto check, a ticked circle for one you
  chose), what else the page did (a pop-up, a browser dialog), and in amber what to check by hand.
- **The code**, in the middle, or the YAML of its values, or the page's [Elements](#the-elements-view) (**Code |
  YAML | Elements** at the top). The selected step's lines are marked, and a line to check by hand has a warning
  beside its number. Click a line to select its step.
- **The inspector**, on the right: every locator the recorder found for the selected step's element, each with what it
  found when you acted (`✓ 1 match`, `✗ 3 matches`, `✗ generated id`). Point at one to see in the app what it finds;
  click one to use it instead, and the code changes at once. **Use the recorder's choice** goes back to its own. Below
  them, under **Settings**, the values the step records as the flow's settings, which you can
  [change](#editing-the-steps), and **What the page did**.

| In the panel | Does |
|---|---|
| Point at a step or a locator | Boxes in the recorded window every element it finds, as the overlay's box does |
| Ctrl+K | Lists every command with its key; type to find one, Enter runs it |
| ? | Shows the keys, in the app and in the panel |
| The edge between two panes | Drag it, or focus it and press the arrows, to resize them; a double-click goes back to the default. The panel remembers it |
| ↑ / ↓ | Selects the step before or after |
| **Undo**, Ctrl+Z | Takes back your last change in the panel, or else leaves the last step out of the flow; the app stays as it is. Its tooltip says which. It also brings back a part [recorded again](#recording-a-part-again) as it was |
| **Check**, F6 | [Checks something](#checking-what-the-page-shows) in the app: the next click there picks an element |
| Ctrl+Shift+C | Picks an element in the app for the [Elements view](#the-elements-view) |
| **Auto checks**, above the steps | Turns every [auto check](#what-each-action-expects) on or off, as the start screen's switch |
| ✕ on a check | Drops that auto check, or deletes a check you chose; ↺ brings a dropped one back |
| The trash on a step, Delete | Deletes the action from the flow, with what led to it and its checks |
| A step's name, a setting's name or value | [Changes it](#editing-the-steps) in place |
| The flow's name, at the top | Renames the flow; a name it cannot have is refused, with why |
| **Pause** / **Resume**, F8 | Pauses or resumes the recording, as `F8` in the recorded window does |
| **Test**, Ctrl+Enter or F5 | [Tests the flow](#testing-the-flow) recorded so far, as it will be saved, in a window of its own |
| **Run to here**, Ctrl+Shift+Enter | Tests it up to the selected step, and that step too |
| **Stop**, Esc or Shift+F5 | Stops the test, or the replay up to a part recorded again |
| **Re-record**, on a step's name or an action (point at it) | [Records it again](#recording-a-part-again): the recorded window replays the flow up to it, then what you do takes its place |
| **Record after this**, on a step's name or an action | Replays the flow through it, then what you record goes after it, before the steps after it |
| **Done**, F2 / **Keep it as it was**, Esc | Ends recording a part again: keeps what you did, or the part as it was |
| **Try**, F7 | [Tries things](#trying-things) in the app: nothing is recorded, and each action shows as the recorder would write it |
| **Add to flow**, on a tried action | Adds it to the flow where the next action goes |
| The gauge, at the end of **Test** | Tests in slow motion: each action waits 500 ms first |
| **Save**, Ctrl+S | Ends the recording and saves the flow, as closing the browser does |

The panel is a window of the recorder's own Chrome in a browser context of its own: nothing you do in it is recorded,
and it needs no network or port. Closing it does not end the recording; closing the recorded window does. It follows
the system's light or dark theme, and the sun or moon at the top, or the palette, switches it. What you choose in it,
the theme, slow motion, the panes' sizes and **Interactive only**, is kept for the next recording in
`~/.qaas/recorder-panel.json`. A change you make is told by a toast at the bottom, one at a time, with **Undo** where
it can be undone; so is a command the panel refused, and why. On a screen with no room beside the
recorded window, which has the size of the recording's 1920×1080 viewport, the panel opens over that window's right
part: move it to another screen, or snap it aside. Before a recording it is the start screen, and after it, what was
saved.

### Picking an element

A click in check mode (`F6`) or inspect mode (`Ctrl+Shift+C`) is never sent to the app. A small menu beside the pointer
offers what is meaningful under it, as the recorder finds the element a click is for: that element first, then the
option, card, row, list or dialog around it, never a wrapper of no meaning, up to five. Each says what it is, its name or
what it shows, and its locator with how many elements that finds, e.g. `option  Express`, `card  Express · 2 days`,
`list  Shipping`. Pointing at one, or `↑` and `↓`, boxes it in the page; `Enter` or a click picks it, and `Esc` picks
nothing. When only one element is meaningful there, it is picked at once.

### Checking what the page shows

The recorder checks what each action did by itself ([auto checks](#what-each-action-expects)); to check something
else, press `F6` in the app, or **Check** in the panel. The box over the element under the pointer turns green, and the
pill at the top of the page and the panel's status strip say `Check`. A click there does nothing in the app: it offers
the elements under it to pick one ([Picking an element](#picking-an-element); `Alt+↑` before the click picks the one
around it at once), and a chooser opens beside the one picked with the checks that fit, the likeliest one chosen:

| Check | Offered for | Is written |
|---|---|---|
| Has text, Contains text | an element with text (chosen first) | `ToHaveTextAsync("…")`, `ToContainTextAsync("…")` |
| Has value | a field (chosen first) | `ToHaveValueAsync("…")` |
| Checked, Unchecked | a checkbox, radio or switch (its state now chosen first) | `ToBeCheckedAsync()`, `Not.ToBeCheckedAsync()` |
| Count | an item of a list, a table row (chosen first for the item itself) | `ToHaveCountAsync(3)` on the locator of them all, by the id or test id of what holds them |
| Row exists with | an element in a table row: you choose its cells | `Not.ToHaveCountAsync(0)` on the row by those cells |
| Visible, Gone | any element | `ToBeVisibleAsync()`, `ToBeHiddenAsync()` |
| Enabled, Disabled | a control | `ToBeEnabledAsync()`, `ToBeDisabledAsync()` |
| URL is | the page (also **URL is…** in the pill at the top, without an element) | `ToHaveURLAsync(new Regex(…))`, ids in the path matched as any |

`↑`/`↓` choose, typing changes the text, value or count expected, Enter adds the check, `Esc` goes back to picking and
`Esc` again leaves check mode. Nothing you do in check mode reaches the app, or is recorded as an action. A check goes
after the last action recorded, in its step, marked `// check`, and is listed on that action's card:

```csharp
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Create", Exact = true }).ClickAsync();
        await Expect(dialog.Page.GetByRole(AriaRole.Row).Filter(new() { HasText = Unique(Configuration.Title) })
            .Filter(new() { HasText = DueDate }).Filter(new() { HasText = Configuration.Priority }))
            .Not.ToHaveCountAsync(0); // check
```

The element is found as a click on it would be. A value typed or chosen before reads its setting, so each case checks
its own, where the check looks as well as in what it expects (a row by its key's setting, a cell by the city chosen
before). An element found by the very text it is checked to have is checked to show (`ToBeVisibleAsync`), as a check of
its text would pass whenever it is there. A number that is no recorded value, such as a new record's id, changes from
run to run: a text with one is checked by the words around it (`ToContainTextAsync("created")` for "Ticket TCK-1043
created"), as a message is, and a row's cell that is only one is left out. A row's cells are its texts as the filter
matches them, and those that change from load to load or with an edit (a count, a price, a date) are off at first. The **Auto checks** switches never remove these checks.

### Editing the steps

Everything you change here is kept in the recording, so the flow is planned again with it as you go on, and a
recording [continued](#continuing-a-flow) or kept with Keep the raw recording has it. **Undo** takes back each change in
turn, newest first, and a step recorded after it before it.

- **Delete an action**: the trash on its card, or Delete. What only led to it (opening a select) and the checks after
  it go with it. A toast at the bottom offers **Undo**.
- **Rename a step**: click its method name above its cards. Empty gives it back the recorder's name.
- **Change a value or a setting's name**: click either in the inspector. The default and the YAML change; text on the
  page that showed the value recorded reads the setting still. A value you changed shows the one recorded, with **Use
  it** to go back. In a loop, only the values of its list change.
- **Unique per run**: for a value typed that must not repeat, such as a new record's name, the flow appends its run's
  stamp: `FillAsync(Unique(Configuration.Title))` types `Printer offline 1103-142501-k7q`, and every later locator,
  text or check that shows the value reads the same stamped one. `Unique` is a method of `BasePlaywrightFlow`: the
  stamp is the time the run first asked for it and three random characters, the same for the whole run.
- **A date relative to today**: a date typed within a year of the day you record it, as `06/11/2026` or `2026-11-06`,
  is offered as days from today ("3 days after the day it was recorded", **Type it as 3 days from today**); it never
  changes by itself. The setting becomes the days (`DueInDays: 3`), and a property of the flow types the date in the
  format it was typed in:

  ```csharp
      private string DueDate =>
          DateTime.Today.AddDays(Configuration.DueInDays).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
  ```

### Suggestions

What a person does on the way that the flow likely needs not, the recorder suggests leaving out, on the cards of the
actions it is about, as it suggests a date relative to today: what it saw, and one button that applies it. Undo brings
it back. It never leaves these out by itself, as what looks like noise may be what the app needs:

| Suggested | When | Applying it |
|---|---|---|
| **Leave it out** | A value typed or chosen again in the same field later, before anything used it: no submit, no new address, no dialog or message, no check of yours. Enter pressed in it that did nothing counts as nothing. | Leaves out the first, with the Enter after it and the click that opened its list |
| **Leave the repeats out** | The same click again right after, and nothing showed after it: a button that seemed to do nothing, clicked again. | Keeps the first click |
| **Leave both out** | A choice of a multi-select taken back by its "Remove …" button before anything used it. | Leaves out the choice and the removal: the list is the choices that stayed |
| **Leave it out**, **Leave both out**, **Leave these 4 out** | What was done and undone before anything used it, so the page is as it was: a checkbox or a switch ticked and unticked, a toggle clicked twice ("Toggle light/dark mode", a menu's group opened and closed), a menu's choice made and made back (English, Français, English). Never a column's header clicked twice, which sorts it one way and then the other. | Leaves out all of it |
| **Leave these out** | In a recording of an older recorder, which did not watch what a click changed: clicks on what is no control that showed nothing. | Leaves them all out |

A checkbox or a switch ticked again and again is the state it ends in, one step; radios of one group picked one after
the other, the last. A select or a combobox opened and closed with Escape that showed nothing is left out, both; one
opened and filtered in its list's own field (Kendo's) is chosen in the list they left open, never opened again. Values
filled in are one step with the click after them only when it sends them (a submit, Save, Search, Log in) or a key
does: a click on a menu after a search's text is a step of its own. The address the app was at when a pause or a try ended is not gone to when the next click is on a
menu's item or a tab that goes elsewhere itself.

The console lists the suggestions left when you save. What the recorder leaves out by itself, without asking, is only
what cannot matter: a click that only put the focus in a field the next action types into or that you left, the click
that opened the list of the choice next (an arrow hidden from assistive technology included), a click on no control or
item after which nothing at all changed on the page, the clicks of a date picker's calendar or of a file chooser you
closed, and a link dragged a few pixels as you clicked it.

The letters you typed to find an option of an autocomplete are part of the choice, as its search: a setting of their
own, `CustomerSearch: 'Hipol'` beside `Customer: 'Hipolito Bergnaum'`, which the flow types before it takes the option
(`FillAsync(Configuration.CustomerSearch)`), as a site may find nothing by the option's whole text. A combobox that
completes what you type in place, the rest selected, is recorded with the letters you typed, never the completion. A
search the option's text is all of needs no setting. In a loop, each round is a record of its `Search` and the option's
`Name`.

### Testing the flow

**Test** in the panel's top bar, `Ctrl+Enter` or `F5` there, runs the flow recorded so far and shows, step by step,
whether it works and where it fails, before you save it.

- **It runs the code that will be saved**, with or without auto checks as it will be saved. The code the panel shows is
  built as your test project builds it, in a temporary folder only you can read, against the recorder's own
  QaaS.Playwright and Playwright: nothing is restored, so no package feed is needed. It is compiled by the .NET SDK's own
  C# compiler, with the references, options and implicit usings `dotnet build` gives a project, through the compiler's
  server, without starting MSBuild, which takes most of a build's time. As the panel opens, the recorder compiles the
  flow once in the background, at a low priority, so the server is ready and the first test builds as fast as the next,
  in a fraction of a second. When that compiler or the reference assemblies are not found, or it fails for another
  reason than the flow's code, the flow is built with `dotnet build` instead, which takes a few seconds or more. A test
  of code that has not changed uses the same build again. Each run loads the build afresh, and unloads it after.
- **It runs as the probe runs it**, in a window of its own over the recorded one: a browser context of its own in the
  recorder's Chrome, `BaseUrl` the page you started on, your [login flow](#logging-in-first) first when you record with
  one (as `SetupFlows`, in its probe's order, with its own BaseUrl, then the page you started on), else the
  login the recording started from (`~/.qaas/auth.json`), and the recorded values as
  the flow's settings (its YAML). It has the probe's timeouts (30 s for an action, 5 s for an `Expect`), page setup and
  failure screenshot, and hides the run's secrets, so a flow that passes here passes in the probe with that YAML.
- **It really does what the flow does**, in your app: a flow that creates an order creates another one.
- **Nothing it does is recorded.** The recording is paused while it runs, as with `F8`, and the overlay in the
  recorded window says `Testing the flow`; the recorded window is left as it is. Then the recording goes on as before
  the test, or stays paused if it was.

Until the first step runs, the status strip says what is going on, with its time so far and a moving line under it:
`Building the flow…`, `Opening a window of its own…`, then `Logging in…` with your login flow and
`Opening the start page…`, or `Opening the start page…` alone. Then it says which step runs, with the time, and each step on the left gets a spinner, then a ✓ with how long it
took. When a step fails, the strip says which, the step turns red and opens on why, as the
probe reports it: what it waited for, on which page and on which line, the screenshot of the page then (click it to
see it whole), what the recorder listed to check by hand there, and Playwright's call log. The steps after it show
`Not run`. In the code, the statement running is marked as it runs, each step's lines green once it passed, and the
line that failed in red. A [loop](#repeated-actions)'s line runs once for each item: it is marked as each repeat runs
it, each repeat's step on the left gets its own ✓, a failure is told at the repeat it happened in, and the line turns
green once every repeat of it passed. The console prints the result too, and how long each part of the run took, so a
slow one shows:

```text
  › Testing       NewTicket…
  × Failed        NewTicket at step 4 of 6, line 34, after 31.8 s.
    4 ▼ choose  Category → Software           [name='category']
        └ Timeout 30000ms exceeded (waiting for Locator("[name='category']")) on https://helpdesk.example/helpdesk
                  build 0.2 s · window 0.6 s · start page 1.1 s · run 31.2 s
```

The result stays until you test again or dismiss it; once you change the flow, it is marked `changed since`.

- **Run to here**, on a step (point at it) or in the inspector, or `Ctrl+Shift+Enter`, runs the flow from the start up
  to the selected step, that step included, and stops there: the steps after it do not run. It checks the start of a
  flow without its end, e.g. without the final save.
- **Slow motion**, the gauge beside **Test**, has each step wait 500 ms first, to watch it. The panel remembers it.
- **Stop**, `Esc` or `Shift+F5` closes the test's window and ends the run; closing that window does too.
- **It needs the .NET SDK** of the recorder's .NET (10), which a test project needs anyway; the recorder itself runs
  on the runtime. Without it, **Test** says so, and the recording goes on.

### Recording a part again

When a part of the flow is not what you want, e.g. you went through a dialog the long way, record that part again
rather than the whole flow. Point at a step's name, or at an action, and click **Re-record**; the panel says what that
does in the app, and does it once you confirm:

1. **The recorded window goes back to the start of the flow and replays it up to the part**, as
   [Test](#testing-the-flow) runs it: built as saved, through the probe's runner, with its timeouts and the flow's
   values, but in the recorded window itself, as [continuing](#continuing-a-flow) does. Its cookies and the start
   page's storage are cleared first; then your [login flow](#logging-in-first) logs in on a blank page and the start
   page opens, or the login the recording started from is put back and the start page opens. Nothing is recorded meanwhile, and the overlay says
   `Testing the flow · replaying it here`. Like a test, it really does what the flow does in your app.
2. **Then do the part again.** The status strip says `Re-recording FillNewTicketAsync: do it again, then press Done`,
   with **Done** (`F2`) and **Keep it as it was** (`Esc`), and the pill at the top of the page
   `Re-recording FillNewTicketAsync · do it again, then F2`. What you do goes in place of
   the part's actions, its cards marked `new`, and the steps after it stay. Checks (`F6`), pausing (`F8`) and trying
   (`F7`) work as ever.
3. **Done**, `F2` in the app or the panel, keeps it, and the code is written with it: **Test** the flow to see it run as
   it will be saved. **Keep it as it was**, `Esc` in the panel, keeps the part as it was, and takes the app back to the
   page it was on before the replay, nothing recorded meanwhile, and the recording to what it did then: recording, or
   paused. Done pauses the recording, as the app is where the part left it, not at the end of the flow, unless the part
   was the flow's last; the console and the panel say it is paused, and why. Undo (`Ctrl+Z`) brings back the part as
   it was; before anything is recorded in it, Undo keeps it as it was.

**Record after this**, on a step's name or an action, replays the flow through it instead, and what you record goes
after it, before the steps after it, which stay; nothing is replaced. It ends in the same way.

- **A step**, by its name, is all its actions; **an action** is that one, with what only led to it (opening a select)
  and its checks. In a [loop](#repeated-actions), re-recording one repeat re-records the whole loop, as the
  confirmation says, and recording after one records after the loop.
- **When the replay does not get to the part**, the panel shows where it failed and why, as a test run does, and so
  does the console. The app is then where the replay stopped: **Re-record from step 3** records again from the step that
  failed to the end of the part, and **Cancel** keeps the flow as it is and takes the app back, as `Esc` does.
- **It is in the recording**, so autosave, the raw recording kept and [continuing](#continuing-a-flow) keep the flow as you
  recorded it again, and a recording continued can be recorded again in part too. It needs the panel and the .NET SDK,
  as **Test** does.
- A part that opens a pop-up or a tab that the steps after it act in: they act in the page the part opened when first
  recorded, which the flow no longer opens. Record them again with it.

### Trying things

To see what the recorder would make of something without recording it, press `F7` in the app, or **Try** in the panel.
As when paused, nothing you do is recorded, and the pill at the top of the page says `Trying · nothing is recorded ·
record again F7`; but the box and its label follow the pointer as when recording, in teal, the label's tag saying
`Try · Click`, and each action shows under the steps as a `tried` card, scrolled into view, written as the recorder
would write it where the next action goes:

- the locator it would use, and why the ones before it lost, as the overlay's label says it;
- the other locators it found, under **other locators**, each with what it found (`✓ 1 match`, `✗ found 3`,
  `✗ looks generated`);
- the auto checks it would add after it: the dialog it opened or closed, the message, the new address;
- for what you typed or chose, the setting it would be, e.g. `setting Title = 'Printer jammed'`;
- or, for what the flow would leave out (a click on no control that changed nothing, opening a select), why.

**Add to flow**, on a card, adds that action where the next action goes: at the end, or in the part being recorded
again. It is then an action of the flow, as one added in the [Elements view](#the-elements-view) is, and Undo takes it
back. `F7` again, or **Stop trying**, goes back to recording as resuming after a [pause](#pausing) does: if the app is
on another page then, the flow goes there by its address. Leaving Try clears the cards. Without the panel, Try is a
pause.

### The Elements view

For what a hover cannot reach or single out (a menu item in a portal, a nested dropdown, an element under another),
**Elements** shows the recorded page's elements as a tree, as a browser's developer tools do. Each row is the tag, its
id and classes (those a framework generates left out), then, dimmed: its role (a `tr` is a `row`), its accessible name,
its own text, its test id, and for an element with no text of its own, the start of the text inside it, so four
`div.field` read `Name`, `Category`, `Stock` and `Notes`. A level loads as you open it, and only the rows in view are
drawn, so a page of thousands of elements stays fast. Shadow roots show as `#shadow-root`, and a same-origin frame's
elements inside its `iframe`.

Pick an element the way you would in the developer tools:

- **In the app**: `Ctrl+Shift+C` (in the app or the panel), or the arrow at the top left of the view. The violet box
  follows the pointer in the app, its label naming the element (`Inspect  option “Cables”`, then `li  192 × 32`), and
  the tree opens to the element and scrolls to it. The status strip says `Inspect`. A click picks it: the app never
  sees that click, nothing is recorded, and the keys go on in the panel. `Esc` there, or `Ctrl+Shift+C` again, leaves
  without picking. A menu that closes when the focus leaves the app stays open, as the focus stays there.
- **In the tree**: point at a row to see its element in the app, scrolled into view if it is out of it; click a row to
  pick it. The path to the element picked shows under the tree; click a part of it to pick that one.
- **By search**: type in the tree or the search field. It finds by text in an element's id, test id, name, label or
  own text, by CSS (`#filter`, `.menu li`) or by role (`role=button[name="Save"]`), says how many (`2 of 11`), and
  picks the first as it finds it; Enter and Shift+Enter go to the next and the one before. Every match is boxed in the
  app, the one you are at stronger, and the app scrolls to it. When nothing matches, a line under the search says so,
  and what it can find by.

When a dialog, a menu or a list opens on the page, **Open on the page** names it above the tree (`listbox “Category”`):
click it to go to it, opened. The tree follows the page as it changes, once you leave it alone for a moment, keeping
what you opened and picked; **Refresh** reads it at once.

**Interactive only**, the funnel beside the search, leaves out what you do not act on: what is inside a wrapper shows in
its place, and `⋯` before an element says wrappers were left out around it. Rows, dialogs, menus, lists, tables, forms
and frames stay, so a table row holds its button directly. The panel remembers it.

| Key, in the tree | Does |
|---|---|
| ↑ / ↓, Page Up / Page Down, Home / End | Picks the element before or after |
| → / ← | Opens the element, or goes to its first child; closes it, or goes to the element around it |
| Enter | Makes the step marked `↵` in the inspector |
| A letter | Starts a search with it |
| Esc | Clears the search, or leaves inspect mode |

The inspector shows the steps the element can become, each with the lines it writes, `−` for those it takes out and `+`
for those it adds, and how many elements its locator finds (`its locator: 1 match`). Each changes the code at once, and
Undo takes it back:

| In the inspector | Does |
|---|---|
| **Use for “…”** | Has the step selected on the left act on this element instead. Marked `↵` when the element is around, inside or in the same row as that step's element: a click recorded on the wrong one |
| **Add click** | Adds a click on it where the next action goes: last, or in a part [recorded again](#recording-a-part-again). Marked `↵` otherwise |
| **Add fill…** | For a text field: adds typing a text you give into it, as a setting of the flow like a recorded one. Enter opens it, the text is typed at once, Enter adds it. Marked `↵` for a text field |
| **Add check** | Adds a [check](#checking-what-the-page-shows) of it, of the kind you choose: its text, a field's value, whether it shows, the count of items it is one of… |
| **Copy** | Copies its locator's C# |

Below them, every locator for it, each checked against the page, chosen by the same rules as for a recorded click.

A menu that closes as soon as the focus leaves the app closes before you can click into the panel, unless you pick in
the app. Else open it, press `F4` in the app, and the panel shows the page's elements as they were then, opened at the
menu, with the locators of each element a click acts on found at that moment. Pick an item even after the menu closed:
it is marked "Not on the page now", and a click added on it replays, as the step before it opens the menu again.
**Back to live** leaves the frozen tree. Neither the tree, inspect mode nor `F4` changes the page or is recorded. An
element in a frame can be found and seen, but not made a step: the flow does not act inside frames.

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

    /// <summary>Opens the "Schedule maintenance" dialog.</summary>
    private async Task<ILocator> OpenScheduleMaintenanceAsync(IPage page)
    {
        await page.GetByRole(AriaRole.Button, new() { Name = $"Actions for {Configuration.PlateNumber}", Exact = true })
            .ClickAsync();
        await page.GetByRole(AriaRole.Menuitem, new() { Name = "Schedule maintenance", Exact = true }).ClickAsync();
        var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Schedule maintenance" });
        await Expect(dialog).ToBeVisibleAsync(); // auto check
        return dialog;
    }

    /// <summary>Fills in the maintenance type, due date and priority.</summary>
    private async Task FillScheduleMaintenanceAsync(ILocator dialog)
    {
        var maintenanceType = dialog.Locator("[id='maintenanceTypeId']");
        await maintenanceType.ClickAsync();
        await dialog.Page.GetByRole(AriaRole.Option, new() { Name = Configuration.MaintenanceType, Exact = true })
            .ClickAsync();
        await Expect(maintenanceType).ToHaveTextAsync(Configuration.MaintenanceType); // auto check
        await dialog.Locator("[name='dueDate']").FillAsync(Configuration.DueDate);
        await dialog.GetByRole(AriaRole.Radio, new() { Name = Configuration.Priority, Exact = true }).CheckAsync();
    }

    /// <summary>Clicks "Schedule", which closes the dialog and shows "Maintenance scheduled for ...".</summary>
    private async Task ScheduleAsync(ILocator dialog)
    {
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Schedule", Exact = true }).ClickAsync();
        await Expect(dialog).ToBeHiddenAsync(new() { Timeout = 30_000 }); // auto check
        await Expect(dialog.Page.GetByRole(AriaRole.Alert)
            .Filter(new() { HasText = $"Maintenance scheduled for {Configuration.PlateNumber}" })).ToBeVisibleAsync(); // auto check
    }
}

public sealed record ScheduleMaintenanceConfig
{
    public string PlateNumber { get; init; } = "HL-775-ZE";
    public string MaintenanceType { get; init; } = "Brake inspection";
    public string DueDate { get; init; } = "03/11/2026";
    public string Priority { get; init; } = "High";
}
```

```yaml
FlowConfiguration:
  ScheduleMaintenance:
    PlateNumber: 'HL-775-ZE'
    MaintenanceType: 'Brake inspection'
    DueDate: '03/11/2026'
    Priority: 'High'
```

- **Steps.** `RunAsync` calls one method per step, each with a line that says what it does. A step ends where a dialog
  opens or closes, the address changes, a pop-up opens, or the page answers a click with a message or a busy
  indicator. What you filled in and the click that sends it are two steps (`FillScheduleMaintenanceAsync`,
  `ScheduleAsync`), unless it is one or two values, as a search box or a login is (`SearchAsync`). The step that opens
  a dialog checks that it shows and returns it; the steps in it work inside it. A step is named after what you clicked
  (`AssignTicketAsync` for the "Assign" button of a ticket's row, `OpenOrdersAsync` for a link "Orders"), else after the
  dialog or the page, else after the button's id or test id. A step that only chooses, maybe confirmed by a button
  such as "Confirm", "OK" or "Done", is named after the choice: `ChooseShippingAsync` after the setting of a select or a
  list's option, `ChooseStateAndCityAsync` for two, `ChooseExpressAsync` after the label of a card or an item clicked.
  A step ends where the section of the page its actions are in changes, by its heading, and is named after it
  (`FillPersonalDetailsAsync`, `FillCustomFieldsAsync`); a lone verb says what of by its section (an "Add" in
  Attachments is `AddAttachmentAsync`); the page's name leaves out a record's id and what names it (`empNumber/7`) and
  a verb a route starts with (`viewPersonalDetails` is `PersonalDetails`). A name taken is the step done again
  (`OpenMyInfoAgainAsync`), numbered only the third time; a second form step of a page is named after what it fills in
  (`FillGenderAndEmailAsync`). Each summary says what a person would: "Fills in the gender, email, first name, last name
  and mobile number, pressing Enter after the email." A dialog is named after its title, without the Close button its
  title bar holds ("New Event", never "New Event Close") and up to its first word with a number,
  or the heading it shows, else after the button that opened it, each dialog after its own; a dialog another button
  opened before is named after what its own button does (`AddAsync` for "Add" opening the "Edit" window), never
  `OpenEditAgainAsync`. A lone verb takes a section of one or two words only, never a page's long title; "Sign in" is
  `SignInAsync`; a page of an app that routes by the address's fragment (`#/products`) is named after its route. The click that only opened
  a select's list is left out: the flow opens it as it chooses.
- **Settings.** Every value you type, choose, check or pick becomes a property of `<Name>Config`, with the recorded
  value as its default (a date or a time input's with the format it takes, `yyyy-MM-dd`, in its doc comment; a
  multi-select's, the option chosen, never what the field then shows with the record's other choices), and the same values go in `<Name>.flow.yaml`: paste that block under the probe's
  `ProbeConfiguration` and change it per case. A setting is named after the field's form name
  (`employee.firstName` gives `EmployeeFirstName`), else its own stable id (by its label when that says the same in
  fewer words: `userEmail` and "Email" give `Email`; `subjectsInput` gives `Subjects`), test id, the stable id of the
  widget it is part of (react-select's input in `#state` gives `State`, never a container of the page such as `#root`,
  `#main-content` or `#demo-runner`), its label, the label beside it in its form
  group, row or column though nothing ties it to it (`User Role`, `Blood Type`), what its prompt says (`Select State`),
  for an option of a list without a field the list's label, else the dialog's title; never a placeholder's example of a
  value (`name@example.com`), nor the value a select shows. A checkbox whose id only numbers it is named after its label
  (`hobbies-checkbox-3` is `Music`), a switch or a checkbox of no label after the words of its list's or its tree's item
  (`FixedHeader`, `FebruaryPdf`), a row's checkbox after its row (`UsernameSelected`), the header's `SelectAll`; a field
  of a step form after its name without the step's index (`0_receiverAccount` is `ReceiverAccount`), a name of a letter
  or two (`q`) after its label or placeholder (`Search`); a choice of no name after the section it is in (a sport chosen
  under "Hobbies" is `Hobby`); a link to a person without their avatar's initial ("L Leonie Abernathy"). A
  choice drops the `Id` its field's name ends with, as it holds the option's text (`maintenanceTypeId` gives
  `MaintenanceType`). The same words typed again into another field, as a user's name searched for after it was given,
  read the first one's setting. One file uploaded is a setting of its path, named after its field's label
  (`AttachmentFile: 'report.pdf'`): set it per case, as the browser told only the file's name.
- **Secrets.** A password, or a setting whose name says it is a secret (password, secret, token, API key, pwd), is in no
  file of the flow: its YAML reads it from an environment variable named after it (`Password: '${PASSWORD}'`), its
  record has no default (`= ""`), and the console never shows it and says once where to set it. Only the panel's
  **Test** is given the value you typed. A value typed in a login is no part of a later locator: a user name "Admin" is no
  menu's link "Admin". Two settings of
  one name are told apart by what they are for (`TicketToAssign`, `TicketToClose`), else by a number. A checkbox is a
  `bool`, a number field an `int` or a `decimal`; the rest are text. A record of more than five settings is grouped by
  the step that reads them. Text on the page that shows a recorded value, like a row's key, a message or a check,
  reads the setting too, so each case finds its own row; so does a later locator with a value you typed in it, like
  the link to the ticket you just named.
- **Locals.** A locator a step uses twice is a local named after its field or its text (`maintenanceType`,
  `addLineButton`); a row two elements are in, after its key's column (`orderRow`).
- **The first navigation is left out**: the probe opens `BaseUrl` itself, so the same flow runs in every environment.
  A page you open by its address later (typed in the address bar, or a bookmark) is opened from `BaseUrl`
  (`$"{BaseUrl}/orders"`), and so is a tab you open with an address. Addresses the app goes to by itself are left out,
  as it goes there again when the flow runs: a redirect when the page loads, a sign-in round trip, the app's router,
  or a save that takes longer than 5 seconds to move on.

### Repeated actions

What you do three times or more in a row, the same kinds of actions on the same elements with only the values you type
or choose changing, becomes a step that loops over a list setting with the values you recorded:

```csharp
    /// <summary>For each of the new tasks, fills in the new task, then presses Enter.</summary>
    private async Task EnterNewTasksAsync(IPage page)
    {
        var newTask = page.Locator("[id='new-task']");
        foreach (var task in Configuration.NewTasks)
        {
            await newTask.FillAsync(task);
            await newTask.PressAsync("Enter");
        }
    }
```

```yaml
    NewTasks:
      - 'Buy milk'
      - 'Pay the electricity bill'
      - 'Book a dentist appointment'
```

- A repeat that types several values loops over records: `foreach (var line in Configuration.Lines)` with
  `line.Product` and `line.Quantity`, from `Lines: [{ Product: 'Desk lamp', Quantity: 2 }, ...]`. A repeat that types
  nothing, such as clicking "Add empty line" three times, is a `for` loop over a count setting (`EmptyLineCount: 3`).
- A locator with the repeat's own value in it, like the row of the item just added, is built from the loop's item.
- In the loop, a check stays only when it reads the item, such as a message naming it; a message every repeat showed
  alike is checked once after the loop. The recorder adds no other check after a loop: it did not see where the values
  went, and a check of the last value's text would fail on a page that still shows it from an earlier run.
- A round may act on a row of a table found by its key, which the loop then goes over: ticking a row, its trash and
  "Yes, Delete" in the dialog that asks, five times, is `foreach (var username in Configuration.UsernamesToDelete)`, the
  dialog found once before the loop and checked as each round opens and closes it. A round acts on one row: the
  recorder takes the rounds from the first action on each row.
- Checkboxes of one group ticked one after the other (under one label, or of one name, or ids numbered alike) are a
  list of their labels: `foreach (var hobby in Configuration.Hobbies)` ticks each by its label. A tick every round did
  alike is no value of the list: the loop ticks (`CheckAsync()`) or unticks.
- The choices of a multi-select (one whose choices each show with a button that removes it) are a list from two:
  `Subjects: ['Maths', 'Arts']`.
- A repeat that types nothing is a loop only when each round did something the page showed: a row added, a message, a
  change. Clicking a Close that closes nothing four times is no loop, and the repeats are [suggested](#suggestions) to
  leave out.
- Repeats that differ in anything else, another element or an action more, stay as recorded, each value a setting of
  its own; so do two repeats.

### Which element a click records

The element you meant, which is not always the one under the pointer:

1. The control you clicked in: the button, link, field or widget (`role=button`, `tab`, `menuitem`, ...), e.g. the
   button rather than the icon in it; for the label of a checkbox, radio or switch, the control itself. Also out of a
   web component's shadow root: an option of a select made of custom elements.
2. Else the nearest element that handles the click itself, with one control in it at most (so never a form or a
   dialog's content): a handler the app's framework keeps on it, which leaves no attribute in the page, as React's
   `onClick` or `onMouseDown`, Vue's `@click` or one bound with jQuery; or an `onclick`.
3. Else the outermost element that looks clickable: the pointer is a hand over it and not over its parent, it has an
   `onclick` attribute, or the Tab key stops on it (`tabindex` 0 or more, and it does not scroll). In a table row or a
   grid cell, that element rather than the whole row.
4. Never a wrapper of several options, such as a grid of cards or a carousel's track: the click is on the option in
   it, also when only the list handles the clicks on its items.
5. Else the row or cell, else the element under the pointer.

A click on a part of a widget hidden from assistive technology, as react-select's arrow is, records the widget's one
control; one on the value a custom select shows records the select. A click in a date picker's calendar records
nothing, in its view of a month, of a year or of a decade alike, as its "Navigate to parent view: 2026" names today's
year: the date the field then shows is recorded as typed into it, as MUI's, antd's and Kendo's pickers all take it.
When the picker kept it only once you pressed its OK, as antd's does, the flow presses Enter in the field after typing
it. A click that opens a file chooser through what
stands for it ("Browse") records nothing; the file chosen is the upload. A click on a header's cell records the cell,
as it sorts its column, never the whole header; a click on a row of data records the row, found by its key, as an app
opens its record (`OpenTicketAsync`). A drag drops on what was under the pointer before the drop,
such as a scheduler's slot, never on the item dragged, nor on the hint a library moves with the pointer, which names
where it would drop.

So a click on the title of a card, a tile or a carousel's slide records the card, and keeps it in the flow, though
nothing else tells it is clickable. When only its label's text or Playwright's own locator would find that element, or
nothing finds it alone, an element the same click hits that has a stable id or test id is recorded instead: one
between it and the element under the pointer, or the only child of a wrapper with nearly the same box, like the `div`s
a carousel puts around each slide (`.slick-track > div > div`); else the one control in it when a click on its middle
reaches it, like the input in a select's box. `Alt+↑` and `Alt+↓` override all of this for one click.

### Locators

For each element the recorder takes the first of these that finds it, and only it, on the page as you saw it:

1. The interactive element itself (the input, not the label or icon you clicked), by its form `name` (Formik's path,
   `[name='employee.firstName']`) or its own stable `id`. Generated ids, like React's `:r1:`, MUI's `mui-12`,
   `-option-3`, `-listbox`, the ids select libraries number by instance (`react-select-2-input`, antd's
   `rc_select_0`, `rc-tabs-0-tab-rule`), Headless UI's and Select2's, long numbers, GUIDs and runs of hex anywhere in
   an id, also as CSS escapes them (`\36 d9a47e9-…_k-dialog-content`), are never used. A click on no control that only
   such an id finds is left out, and listed to check by hand.
2. An MUI Select by its `id`, else as the box around its hidden input (`div:has(> input[name='x'])`).
3. Its test id (`GetByTestId`, i.e. `data-testid`).
4. Its role and exact name, without direction marks and a required field's ` *`. A tab whose name counts something
   (`Open (3)`) by the start of its name. A field that opens a list by its label, without the value it shows
   (`NameRegex = new Regex("^Shipping")`), or by its role alone when only that value names it. A name that holds what
   changes from load to load, or what the flow may edit, a price, a date, a time or a percentage, by the words before
   it when they find the element alone: a poster's link `NameRegex = new Regex("^Black & White 14\\.4x17\\.88")`,
   without its `US$15.57`; else it is listed to check by hand. A name an icon's label starts, as antd's "plus New", or
   "loading Next" while a spinner turns in it, or that a flag's label says twice ("EnglishEnglish"), is found with it
   or without it (`NameRegex = new Regex("^(?:loading )?Next$")`), and the step is named after what it says. A title or
   text no person wrote (`[object Object]`, `undefined`) never finds anything.
5. In a table: the row by its key cell's text, then the element in it; an icon button there by its icon
   (`row.Locator("button:has(i.bi-trash)")`). A column's name is its header's own text, not a sort menu hidden in it.
   The key is a reference of letters and digits (`50SKLO`, `TCK-1040`), else a number in a column of ids, else a name;
   never a price, a date, a time, a count or a status, unless nothing else tells the row apart. A row itself is never
   found by all it says.
6. In its form group, by the group's label, though nothing ties the label to the field:
   `page.Locator("div.oxd-input-group").Filter(new() { Has = page.GetByText("User Role", new() { Exact = true }) }).Locator("div.oxd-select-text")`.
   A field named only by a placeholder's example of a value (`yyyy-dd-mm`), or by a prompt when it has a group, is
   found by its group.
7. A button or link that others share the name of, in its section, by the section's heading: the "Save" of
   "Personal Details", the "Add" of "Attachments".
8. An icon button with no words by its title (`GetByTitle("Help")`), else by its icon's class (`bi-*`, `fa-*`, `mdi-*`,
   a ligature font's word).
9. In a card among cards (a post of a feed) by the first words no other card has, then the element in it; an id every
   card repeats is no id but the element's in its card (`Filter(new() { HasText = "Joao Santos" }).Locator("[id='heart-svg']")`).
10. Within the nearest stable id around it, by its role: react-select's input of the select `#state` is
    `Locator("[id='state']").GetByRole(AriaRole.Combobox)`, never `#react-select-3-input`.
11. An element without a role, such as a card, a tile or an option of a hand-made list, by its label: the element of
    exactly its first words in it, which a click there reaches it through, a badge or a price after them left out
    (`GetByText("Express", new() { Exact = true })`). When copies of it hidden from assistive technology have it too, as
    a carousel's clones do, within the item that is not hidden (`.slick-slide:not([aria-hidden='true'])`).
12. A control of no name by its place: the checkbox of a table's header (`GetByRole(AriaRole.Columnheader).GetByRole(AriaRole.Checkbox)`).
13. Last, Playwright's own locator, listed under "Check by hand". It is never used while another finds the element when
    it picks by a class a style library hashed (`css-1x2y3z-control`, `sc-…`, `jss12`, `makeStyles-root-3`), a
    position, an empty name, the glued text of a wrapper (`"Employee Full NameEmployee"`), a number alone, or a row's
    whole name.

A value typed before is read from its setting in a later locator only where the text shows it as a word of its own:
the link "TradeCode 99" after "TradeCode 9" was typed stays as it is, never `$"{Configuration.Name}9"`.

An element in a dialog is located inside it (`dialog.GetByRole(...)`). A dialog that has no name, as a hand-made
modal often has, takes the heading it shows as its title, and is found by it:
`page.GetByRole(AriaRole.Dialog).Filter(new() { Has = page.GetByText("Shipping", new() { Exact = true }) })`. The
options of a select or autocomplete open outside the dialog, so they come from `dialog.Page`; one that another list on
the page has too, from its own list (`GetByRole(AriaRole.Listbox, new() { Name = "Delivery window" })`). An option
with no role, as antd's list draws them beside a hidden one for assistive technology, is a choice of the one combobox
that is open, a setting as any choice, found by its text among the options of its kind that show, as closed lists stay
in the page hidden: `page.Locator("div.ant-select-item:visible").GetByText(Configuration.Status, new() { Exact = true })`,
never by a title no person wrote (`[object Object]`). An item of a menu that opened as the pointer rested on something,
as an avatar's menu does, is clicked after the flow hovers that (`page.Locator("[id='avatar']").HoverAsync()`).

### What each action expects

Each check the recorder adds is an `Expect` right after the action, marked `// auto check`, and listed on the action's
card in the panel, where its ✕ drops it; a check dropped stays dropped, as the recording keeps it. **Auto checks** off, on
the start screen or above the steps, writes none. Checks you [choose](#checking-what-the-page-shows)
yourself follow them.

- A choice: the field shows it (`ToHaveTextAsync` for a select, `ToHaveValueAsync` for an autocomplete, which is typed
  into first, or a field that is an input). None when the field shows nothing of it itself, as an input under the text
  that shows the choice.
- A tab: `ToHaveAttributeAsync("aria-selected", "true")`.
- A click that opened a dialog: the dialog is visible. One that closed it: `ToBeHiddenAsync`, with 30 seconds for the
  save behind it.
- A message that appeared (`role="alert"`): visible, with its text, without the words of a button it holds ("Customer
  updated", never "Customer updated UNDO"). A number in it that is no recorded value, such as a new record's, is left
  out: the message is found by the words around it, those that name a recorded value first (`created:
  {Configuration.Title}` for "Ticket TCK-1043 created: Printer offline"). Not a message that only says the page is
  busy and goes by itself ("Loading...", "Please wait"), which a check would race.
- A new address: `ToHaveURLAsync`, with ids in the path matched as any value. In an app that routes by the fragment
  (`#/orders/5`), the route is checked the same way. Not when the action also opened a dialog or selected a tab, or
  closed its dialog and showed a message: those say it better; nor when the address before the action matched it
  already, as a sort that changes the query does.

A busy indicator that came up within 5 seconds of an action and went away again is waited for after it, for up to 30
seconds: a progress bar (`role="progressbar"`, as MUI's `CircularProgress` and `LinearProgress` are, also inside a
loading backdrop), or an element with `aria-busy="true"`. One that stayed on the page, such as an upload's progress, is
not. This is a wait, not a check, so Auto checks off keeps it:

```csharp
await Expect(page.GetByRole(AriaRole.Progressbar)).ToHaveCountAsync(0, new() { Timeout = 30_000 });
```

The flow never clicks through JavaScript, sleeps, checks styles, asks `IsVisibleAsync`, picks the `Nth` or `First`
match, or catches exceptions: what the page should do is expected, and a run fails where it does not.

## What to check by hand

When it saves a flow, the recorder lists the lines to look at, with their numbers and why:

- **A locator that did not find the element alone**, or Playwright's own locator when it relies on position, a style
  class or a generated id.
- **Uploads.** The browser tells only the name of a file you upload. One file is a setting of its path, whose default
  is that name: set the full path per case. For several files the flow asks its `FileToUpload` method for each, which
  throws until you make it return the file's full path.
- **Browser dialogs** (alert, confirm): the flow accepts them. Dismiss where you dismissed. One that showed before
  your first action on a page is listed without a line: the flow cannot answer it.
- **Menus that opened on hover**: add `HoverAsync()` on what opens them.
- **A list that renders only the options in sight** (a virtualised list): the flow scrolls it as far as you did
  (`Mouse.WheelAsync`) before it chooses; check that this reaches the option, as it does while its items keep their
  height.
- **Drag and drop**: check where it drops. A drag the recorder could not follow is saved as a hover.
- **Pop-ups and tabs you opened yourself**: check what the flow does in them.
- **An address the app was on when you resumed a pause**: the flow goes there by the address; see [Pausing](#pausing).
- **A dialog that closed with no action the recorder saw** (a key it does not record, a click beside it): the flow
  presses Escape after the action that opened it, so the next step is not behind it; check that this closes it.

When an element is located badly, hover it while recording: the overlay tells why each locator before the chosen one
lost. To share the details, record with Keep the raw recording on: the raw recording is saved beside the flow as
`LoginFlow.recording.json` (`LoginFlow.recorded.recording.json` for a re-recording), with every locator tried for
each element and what it found. It holds the values you typed, a password too, which the flow's YAML does not.

Not recorded at all: what you do inside an iframe. Picking a date from a calendar is recorded as typing the date the
field then shows. Only Enter and Escape are recorded as key presses; typing is recorded as the field's value.

## How it works

The recorder runs its own Chrome session through Playwright, not Playwright's codegen. A script added to every page
listens, without changing anything, to what you click, type and choose, and reports each action with the candidates
for its locator, checked against the page with Playwright's own selector engine. That engine is only available from a
driver that runs with `PWDEBUG=console`, which also turns off every timeout, so the recorder drives Chrome with a
normal driver, where a login flow times out as in the probe, and attaches a second driver in that mode over CDP
(a DevTools port on this machine only, which Chrome picks) only to install the engine in the pages. The overlay asks
the recorder which locator it would write, so it shows what the flow gets. The recorder then plans the steps, names
the settings and writes the flow.

## Tips

- One flow per task: log in in one flow, create an order in another, so they combine in `SetupFlows` and `Flows`.
- Give the fields your app's forms a `name` or a stable `id`, and buttons a visible name; the recorder needs nothing
  else.
- If the recorded page looks different in the test run (e.g. a date picker is a read-only field), the test's Chrome
  reports no mouse: see [EmulateDesktopPointer](CONFIGURATION.md#mobile-layout-in-a-headless-chrome-emulatedesktoppointer).
