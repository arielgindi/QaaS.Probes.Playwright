# Changelog

Notable changes, newest first. Versions follow [Semantic Versioning](https://semver.org/).

## 2.1.0

- **The recorder's modes hold in every order you press their keys**, as a test that drives a recording through random
  sequences of them showed:
  - **In check or inspect mode, the pill at the top says so again** after a key that tells something for a moment, as
    `F4` does (`Elements frozen`): it said that for good, in place of `Check · Esc` or `Inspect · Esc`.
  - **The panel's status strip says when a part recorded again is paused, tried or checked**, with the key that
    leaves it: it went on saying `Re-recording … do it again, then press Done` while nothing was recorded.
  - **Done, while trying or while a test runs, pauses the recording** once back from them, as Done always does when
    the part is not the end of the flow: it recorded on at the end of the flow from where the part left the app.
    **Keep it as it was**, while trying, ends trying too, and pausing or resuming while a test runs takes effect once
    it ended: the tried actions stayed in the panel while recording, and the recording records while the test ran.
  - **Saving, or closing Chrome, while the steps of a flow continued replay saves the flow as it was.** It asked why
    its steps did not replay, as if the app had failed them, then saved nothing.
  - **A tab you open yourself is a tab of its own, however soon after a click**: within 5 seconds, it was that click's
    pop-up, so the flow waited for a pop-up the click never opens, and did not build when the click opened a dialog.
    A page is a pop-up when a page opened it, `noopener` links included.
  - **A recording you deleted every action of is not offered again** when the recorder starts next, after a crash or
    Ctrl+C: its copy in `~/.qaas/recordings/` kept the actions deleted. Undo writes it again.

## 2.0.3

- **The recorder finds your login flow as QaaS finds it when your tests run**, and never fails to say why it did not:
  - **From a folder above your test project**, the repository's root or a solution's folder: it looks up to 3 folders
    down (skipping `bin`, `obj`, `node_modules` and hidden folders) and in the `.sln` and `.slnx` files there, and takes
    the projects that use QaaS (a YAML that runs `SetupFlows`, else a QaaS package in `obj/project.assets.json`). When
    several do, it asks which, once, and remembers the answer for the folder.
  - **Wherever your project builds to**: a `Directory.Build.props` with `OutputPath` or `BaseOutputPath`, a
    repository-level `bin/`, or `UseArtifactsOutput`. When the bin folder has no build, or an old one, it asks MSBuild
    for the project's `TargetPath` (Debug, then Release), and keeps the answer in `~/.qaas/recorder-builds.json` until
    the project, or a `Directory.Build.props` or `.targets` above it, changes.
  - **In a referenced package**: your project's DLL loads with its whole dependency closure, from its `.deps.json`
    (a package a library's build does not copy is found in the NuGet package folder) and its output folder, and the
    flow is matched as `FlowDiscovery` matches it, by class name, case-insensitively, a name two flows have said as
    QaaS refuses it.
  - **Built against another QaaS.Playwright**: a flow of a shared package built against an older version, that uses
    what this one no longer has, is said plainly before Chrome opens, e.g. `LoginFlow (in Acme.Shared.dll) was built
    against QaaS.Playwright 1.15.1, and the recorder runs 2.0.3: it can't load it (BrowserDefaults.RemoteUrl is gone).
    Update that package to QaaS.Playwright 2.0.x, ...`; so is such a failure while it runs.
  - **Never silent**: with no login flow found, one dim line says why and where it looked, until you answered once in
    that folder; after a no, `Not logging in here (you said no on 2026-11-03). --login LoginFlow to log in, --no-login
    to ask again.` `--login LoginFlow` alone finds it the same way, and says why when it does not.
  - **`qaas-playwright --login-why`** prints the whole search, and records nothing: the projects found, the build used,
    the DLLs looked through, the flows found, and why each was chosen or not.
- **The login flow runs first, with the BaseUrl your tests give it.** It runs on a page that has loaded nothing yet,
  so a login that plants a token with `AddInitScriptAsync` before any page loads, as a proxy may need, works; its
  scripts go on running on every page while you record. Its `BaseUrl` is that of the probe that runs it in your test
  YAML (`ProbeConfiguration: BaseUrl:`, `${NAME ?? default}` resolved), else the address of the URL you record on,
  never the whole URL, which sent a login building `{BaseUrl}/login` to a page deep in the app. Then the page goes to
  the URL you record on; started on the app's root, the recording starts where the login left the app, and says so.
  The panel's **Test**, `--continue` and recording a part again run it in the same order. The `Logging in with …`
  line says the BaseUrl, and a login that fails or times out says the BaseUrl it was given and the page it was on.
- **The questions cannot be misread.** `qaas-playwright` without arguments asks `Do your tests log in first with a
  flow, e.g. LoginFlow? (y/N)` first, then its class name, `(the login flow, not the one you're recording)`, among the
  flows found, refusing the flow being recorded and a name QaaS would not find; the DLL only when none was found, and
  its values only when no test gives them and the flow has any.
- **Every question says what it asks, why, an example and what Enter does** (`(Enter = Flows)`, `(y/N, Enter = no)`),
  and every warning and error what happened, the likely cause, and what to do next, without jargon.
- **An app whose fields and buttons are named in a non-Latin script is recorded and saved.** A click on a button
  whose id has no Latin letter, or a fill of a field so named (or of an id of underscores alone), made the flow unwritable: saving said
  `Could not write the flow …: ArgumentNullException: Value cannot be null`, the panel stopped showing what you did,
  and the recorder's own copy stopped being kept at such a field. A name with no letter or digit a C# name can
  have is now passed over, for the next thing that names the setting.
- **Pausing, and stopping with Ctrl+C**:
  - **What you do after a pause is recorded on an app that tests with a date of its own.** An app that replaces
    `Date.now` (a fake date, the server's time) had every action after `F8` dropped, as done before you resumed: the
    recorder now tells the time by the browser's own clock.
  - **What you typed just before pausing from the panel is kept**, as with `F8`: the page learns of a pause the
    panel makes (or Try, or Test) a moment later, and the text being typed then was dropped.
  - **A pause cannot be missed.** The page's pill, the panel and the console say `Paused: nothing you do is recorded;
    F8 resumes`; a click, typing or a choice while paused shows a toast beside it, `Paused: that click was not
    recorded · F8 resumes`, and the first one in a pause prints that line in the console.
  - **Resuming after a login flow that failed says the recording records**, in the console too: it said nothing, as
    it had not seen the pause the failed login made.
  - **`F8` pauses and resumes with the focus in a frame too**, as in an editor or a payment form: the frame never
    told the recorder, so a pause could not be ended there, and nothing more was recorded. So do `F7` and `F2`.
  - **Ctrl+C in the console saves what you recorded, as Enter does**, paused or not, and Chrome stays open meanwhile,
    so your login is saved too. It stopped the recorder at once, saying the recording was not saved. Pressed again
    before the flow is written, it stops at once, says so, and keeps the recording, to save next time.
  - **What you were typing as the recording ended is kept**: Enter, Ctrl+C or **Save** within a moment of the last
    key lost the field's value, which the page told only once you stopped typing.
  - **A flow the recorder cannot write says where to find why**: `Could not write the flow …` now writes where it
    happened, with its stack, to `~/.qaas/recorder-error.log`, and says where the recording is kept, rather than
    printing the stack alone.
  - **A recording with a change of address it does not give is written**, without checking that address, as one
    edited by hand may have: saving it failed with `ArgumentNullException: Value cannot be null`.

## 2.0.2

- **What three more real sites showed, recorded as a messy person does.** react-admin's demo (React and MUI), Ant
  Design Pro (React and antd) and the Kendo UI for jQuery demos gave flows that could not replay, or did the opposite
  of what was done. Now:
  - **A password is never typed from another setting.** react-admin's demo logs in as `demo` with the password `demo`;
    the flow typed the user name as the password, with no setting for it. A password, or what a secret's field holds,
    is never read from an earlier setting of the same value, nor read into another.
  - **A switch or a checkbox of ARIA is told as the page shows it once the app rendered the click**, not before: antd's
    and Kendo's switches turned on were recorded as turned off, so a flow did the opposite.
  - **Browser dialogs are accepted by one handler a page has once** (`page.Dialog -= AcceptBrowserDialog;` then `+=`),
    however often a confirm shows: two deletes no longer add two handlers that accept the same dialog twice.
  - **A row is found by its key**: a reference of letters and digits (`50SKLO`), a number in a column of ids, or a name;
    never a price, a date, a time, a count or a status, which a load or the flow itself changes. A grid's headers are
    found in their own table, as Kendo's are, and a row is never found by all it says.
  - **A name that holds a price, a date or a time** is found by the words before it, when they find the element alone:
    a poster's link by its title and size, never by its `US$15.57`. A name an icon's label starts, as antd's
    "plus New", or "loading Next" while it spins, or that a flag's label doubles ("EnglishEnglish"), is found with it or
    without it; a person's link without their avatar's initial ("L Leonie Abernathy"). A title no person wrote
    (`[object Object]`) never finds anything.
  - **An autocomplete types what the person typed**: the letters typed to find an option are its search, a setting of
    their own (`CustomerSearch: 'Hipol'` beside `Customer: 'Hipolito Bergnaum'`), as react-admin's search finds nobody
    by the whole name. A combobox that completes what is typed in place is recorded with the letters typed, never the
    completion. This changes 2.0.1, which typed the option's whole text.
  - **Generated ids are never used**: a GUID or a run of hex anywhere in an id, one CSS escapes
    (`\36 d9a47e9-…_k-dialog-content`), one a library numbers by instance (`rc-tabs-0-tab-rule`). A click on no control
    that only such an id finds is left out, and listed to check by hand.
  - **A drag drops on what was under the pointer before the drop**, a scheduler's slot, never on the item dragged nor
    the hint a library moves with the pointer; a click on a table's cell is the cell, not its row. A drag onto what it
    dragged is no drag.
  - **A menu that opened on hover is hovered open** (`HoverAsync()` on what the pointer rested on) before its item is
    clicked. **A date picked in a picker that keeps it only once its OK is pressed**, as antd's, is typed and committed
    with Enter; a calendar is one in its month's, year's and decade's views too, so Kendo's "Navigate to parent view:
    2026" is never a step.
  - **An option of no role**, as antd's virtual list draws them, is a choice of the one open combobox, a setting found
    by its text among the options that show (`page.Locator("div.ant-select-item:visible").GetByText(Configuration.Status,
    new() { Exact = true })`).
  - A value typed before is read into a later locator only where it is a word of its own: "TradeCode 99" is never
    `$"{Configuration.Name}9"`.
  - A select or a combobox opened and closed with Escape is left out, both; one opened and filtered in its list's own
    field (Kendo's) is chosen in the list they left open, never opened again, so it no longer closes. Values filled in
    stay one step with the click after them only when it sends them. A multi-select has no setting of what it shows
    with the record's other choices; a date input's setting says the format it takes. A click on a header's cell is the
    cell, never the whole header row, and a click on a row of data is the row by its key (`OpenTicketAsync`).
- **`--continue` never loses a flow.** Kept recording without the steps that did not replay, it saves the flow
  continued beside the flow, as `<Name>.recorded.txt`, says so, and leaves the flow and its recording as they were. A
  flow that logs in itself, its first actions typing a password, is replayed, tested and recorded again signed out, as
  it was recorded, never from the recorder's saved login, which skipped its login page.
- **The recorder never ends unsaid.** An error nothing caught, Chrome or its driver going away, or a signal from
  outside (SIGTERM, SIGHUP, SIGQUIT) writes the recording as it is, prints that it was stopped and that the recording
  is kept, and writes what happened, with its stack, to `~/.qaas/recorder-error.log`. A recording recovered keeps its
  raw recording beside the flow when it was recorded with `--keep-recording`.
- **Recording a part again**: cancelled, or after its replay failed, the app goes back to the page it was on and the
  recording to recording or paused, as it was, never silently paused; a part kept in the middle of the flow says it
  leaves the recording paused, and why.
- **Names**: a switch or a checkbox of no label after its list's or tree's item (`FixedHeader`, `FebruaryPdf`), never a
  page's container (`#root`, `#demo-runner`); a field of a step form without the step's index (`ReceiverAccount`); a
  name of a letter (`q`) after its label (`Search`); a choice of no name after its section (`Hobby`); a dialog without
  the Close button its title bar holds ("New Event"), cut before its first number; a dialog another button opened
  before after what its own button does (`AddAsync`, never `OpenEditAgainAsync`); "Sign in" as `SignInAsync`; a lone
  verb with a section of one or two words only; a page of an app routed by the fragment after its route. The namespace
  is the nearest project's that references QaaS.Playwright, never a project's of something else above a scratch folder.
- **Suggestions** for what was done and undone before anything used it: a checkbox or switch ticked and unticked, a
  toggle clicked twice, a menu's choice made back (English, Français, English). A checkbox ticked again and again is
  the state it ends in, radios of one group the last picked; a column's header clicked twice is never suggested away.
  No address is gone to after a try right before a menu's item that goes elsewhere.
- **Checks**: no message that only says the page is busy ("Loading..."); a message without the words of a button it
  holds ("Customer updated", never "Customer updated UNDO"); no address the page matched already before the action; an
  element found by the text it is checked to have is checked to show; a check you chose reads the settings where it
  looks as in what it expects; a row's cells are taken as its filter matches them, those that change left out at first.
- **One step numbering**: the console numbers each action as the flow, the panel, a test and the summary do, and shows
  a click on no control only once the flow keeps it. "Enter may send the form" is said in a form only.
- **The panel and the app**: `Ctrl+K` in the recorded app opens the panel's commands, as Chrome's search took it; a
  test's failure at a step the flow has no more is cleared; an element picked in the Elements view on a page the app
  left is forgotten; the hover card steps aside once an element is clicked, as what the click opens shows there; the
  pill of a test or a replay is at the bottom of the page, off its header.
- A line sent to a console whose input is a pipe or a FIFO finishes the recording, as Enter does; the input's end
  finishes nothing.

## 2.0.1

- **A flow a tester keeps, from a messy recording of a real site.** Recorded on demoqa's practice form and OrangeHRM's
  demo, as people click around, the flow came out with hashed classes, positions and empty names, settings named
  `Value2` or after the value a select showed, the letters typed to filter a list as settings, a loop of clicks that did
  nothing, and one step of thirty statements. Now:
  - **Locators** never use a class a style library hashed (`css-1x2y3z-control`, `sc-…`, `jss12`), a position, an empty
    name, a wrapper's glued text or a row's whole name. A field is found by its form group's label
    (`Locator("div.oxd-input-group").Filter(new() { Has = page.GetByText("User Role", …) })`), a repeated button by its
    section's heading, a widget by the stable id around it (`Locator("[id='state']").GetByRole(AriaRole.Combobox)`), an
    icon button by its title or its icon (`button:has(i.bi-trash)`), a post by its words, a checkbox numbered by id by
    its label, a header's checkbox by its place. A click on a widget's arrow hidden from assistive technology, or on the
    value a select shows, is a click on the widget.
  - **Names** come from what a person reads: the label beside a field in its row or group, the select's prompt
    (`Select State`), the wrapper's id, a checkbox's label; never a placeholder's example (`name@example.com`). Icon
    buttons are named after their icon ("Delete", "Like"), a row's checkbox after its row, steps after the page's section
    (`FillPersonalDetailsAsync`, `AddAttachmentAsync`), a step done again `…AgainAsync` before any number.
  - **Noise** is left out: a click that only put the focus in a field, the click that opened a choice's list, a click on
    no control that changed nothing (the recorder now watches what each click changes), a calendar's and a file
    chooser's clicks (the date is typed as the field then shows it), a link dragged a few pixels. The letters typed to
    filter a list are part of the choice: the flow types the option's own text, and no setting holds them.
  - **Suggestions**, one click each in the panel, never a silent drop: a value typed or chosen again before anything
    used it, a click repeated that showed nothing, a multi-select's choice removed again, clicks on no control in an
    older recording. The console lists those left when saving.
  - **Loops** need what each round showed: four clicks on a Close that does nothing are one click. A round may go
    through a dialog (tick the row, its trash, "Yes, Delete") and over rows by their key (`UsernamesToDelete`), a
    multi-select's choices are a list from two (`Subjects`), and a checkbox group's ticks a list of labels (`Hobbies`).
  - **Steps** end where the section changes, and their summaries read as a person writes them ("Fills in the gender,
    email, first name, last name and mobile number, pressing Enter after the email.").
  - A dialog that closed with no action seen is closed by Escape, listed to check by hand. A checkbox under the box a
    library draws over it is ticked through that box (`Force = true`). One file uploaded is a setting of its path.
- **A password is in no file of the flow, nor in the console.** The YAML reads it from an environment variable
  (`Password: '${PASSWORD}'`), the record has no default for it, the console says once where to set it, and only the
  panel's Test is given the value you typed. A user name typed to log in is no part of a later locator.
- The console says that Enter in a form's field may send the form.
- Fixed: the recorder stopped with a NullReferenceException writing a flow whose first step is in a dialog no recorded
  step opened, such as one open when the recording started, or after deleting the click that opened it; and then at
  every start, offering that recording.
- A recording the recorder cannot write no longer stops it: it is still offered when the recorder starts, and saving it
  says why and keeps it.
- **Choices in dialogs are recorded as you meant them.** A click on the title or the badge of a card, a tile or a
  carousel's slide is recorded on the card, the element the app's own click handler is on (React's, Vue's or jQuery's,
  which leave no attribute), never on the text in it or on the list of all the cards, and is kept in the flow though
  nothing else says the card is clickable. It is found by its label (`GetByText("Express", new() { Exact = true })`), a
  carousel's hidden clones left out, rather than by Playwright's own locator and a position. An option of a web
  component's shadow root is the option; one another list has too is found in its own list; the box of a select by the
  one control in it, or by its label rather than the value it shows. Ids select libraries number by instance
  (react-select's, antd's, Headless UI's, Select2's) are never used.
- A dialog without a name, as a hand-made modal has, is titled by the heading it shows and found by it, so a dialog
  opened over another one is found, and a step that opens one is named after it, or its own button: no longer after the
  first dialog's button with a `2`. A step that only chooses is named after the choice (`ChooseShippingAsync`,
  `ChooseExpressAsync`), also when a button such as "Confirm" ends it; the click that only opened the select's list is
  left out, as the choice opens it.
- A list that renders only the options in sight is scrolled as far as you scrolled it before the choice.
- `Alt+↑` and `Alt+↓` show the way as a breadcrumb, go out of shadow roots, stop at the element's dialog, never into a
  portal's root or the page's containers, and the pick holds while the pointer moves within the element picked.
- The panel's **Elements** view works as a browser's developer tools do. `Ctrl+Shift+C`, in the app or the panel,
  picks an element in the app: a box names what is under the pointer, the tree opens to it, and a click picks it
  without clicking it or recording anything, a menu that closes on blur still open. Pointing at a row boxes its element
  in the app and scrolls to it. Rows read as the element does: its role (`tr` is a `row`), accessible name and text,
  dimmed, and for a wrapper the start of the text inside it. Search finds by text, id, CSS or `role=button[name="Save"]`,
  picks the first match, steps with Enter and Shift+Enter, and boxes every match in the app. **Interactive only** leaves
  out the wrappers a person does not act on, a `⋯` where they were. A dialog, menu or list that opens is named above the
  tree, to go to it. The keys move through the tree, Enter makes the step marked in the inspector, and typing searches.
  Each step the element can become, **Use for**, **Add click**, **Add fill** and the new **Add check**, shows the lines
  it writes and how many elements its locator finds before you make it. A step added goes where the next action goes,
  so also into a part recorded again. The tree draws only the rows in view, and shows shadow roots and same-origin
  frames.
- **Re-record** a step or an action in the panel, or **Record after this**: the recorded window goes back to the start
  of the flow and replays it up to there, as **Test** runs it, nothing recorded meanwhile; then what you do goes in
  place of the part, or after it, and the steps after it stay. `F2` (**Done**) keeps it, and `Esc` in the panel keeps
  it as it was; Undo brings the old part back. A replay that does not get there says where, as a test does, and offers
  to record again from the step that failed. A loop is recorded again whole. It works while recording and with
  `--continue`, and is kept in the recording.
- **Try**, `F7` in the app or **Try** in the panel: nothing is recorded, as when paused, but each action shows in the
  panel as a dimmed card of what the recorder would write: its locator and why the others lost, every locator with what
  it found, the auto checks after it, and the setting a value would be. **Add to flow** adds one where the next action
  goes. `F7` no longer turns on Chrome's caret browsing in the recorded window.
- The recorder finds your tests' login flow by itself, so `--login LoginFlow --login-assembly <dll>` is no longer
  needed: in your test project (the nearest `.csproj`), the flow its YAML runs in `SetupFlows` most, else one named like
  a login, in the project's build or a DLL beside it, as a shared package's flow is, with its values from the
  `FlowConfiguration` of a test that runs it. It says where the flow is (`LoginFlow, from
  bin/Debug/net10.0/YourShared.dll`), whether it needs values, and for those set nowhere, the YAML to add and where. It
  asks once per folder and remembers the answer; later runs only say what it logs in with. A project not built yet, or
  changed since, is offered a `dotnet build`. `--login <Flow>` alone finds the flow's DLL and values; `--no-login` forgets
  the answer.
- A login flow is hard to miss while it runs: the console, the page and the panel show how long it has run, the page it
  is on, and what it did last. Esc, in the console or the page, or **Log in by hand** in the panel, stops it, for you
  to log in by hand, as when it fails. It stops by itself after 2 minutes of nothing, and when the app asks the browser
  to sign in (with Windows, or HTTP authentication), which a flow cannot answer.
- `--login-assembly` with the test project's own DLL finds a login flow of a package it references, and a YAML of no
  `FlowConfiguration` (`{}`) gives a flow of no settings its defaults instead of being refused.
- **Test** starts far sooner: the flow is compiled by the .NET SDK's own C# compiler, with the references, options and
  implicit usings `dotnet build` gives it, through the compiler's server, without starting MSBuild, which took up to
  15 s where an antivirus scans each DLL it loads. The recorder compiles once in the background, at a low priority, as
  the panel opens, so the first test is as fast as the next. Where that compiler or the reference assemblies are not
  found, or it fails for another reason than the flow's code, it builds with `dotnet build` as before.
- Until the first step runs, the panel's test bar says what is going on, with its time so far: building the flow,
  opening the browser window, opening the start page, logging in. The console prints how long each part took after
  each test, e.g. `build 0.2 s · window 0.6 s · start page 1.1 s · run 4.3 s`.
- **The recorder looks and reads as one calm tool.** The panel, the overlay in the recorded page and the console share
  one design system: a type scale of five sizes, one set of radii and shadows, one button hierarchy (primary, secondary,
  ghost, danger), one icon style, and light and dark palettes whose text meets WCAG AA contrast. Each mode has one colour
  everywhere: recording and re-recording red, paused amber, logging in blue, testing indigo, trying teal, checking green,
  inspecting violet.
- **One status strip says what the recorder is doing and how to leave it**, under the panel's top bar, in place of the
  banners and the test bar that stacked there: the mode, a short line, and the button and the key that end it. A test's
  result shows there, or as a chip at its end while another mode is on; it never moves the panes.
- **Every command is a key away.** `Ctrl+K` lists every command with its key, to find one by typing; `?` shows the keys
  in the app and in the panel; the footer points at `Ctrl+K` until it was used once.
- **Calmer steps.** A card says what was done, the value, the locator and its checks; what the page did shows only when
  no check says it already. The buttons of a card and of a step are icons that no longer cover its text. A loading tree
  or a login shows placeholders, and a search of the elements that finds nothing says so.
- **One toast at a time**, at the bottom, with Undo where a change can be undone, also for a step added in the Elements
  view; a command the panel refused says why in it, once.
- **The panes are resizable**, by dragging the edge between them or with the arrow keys, and the panel keeps them, with
  the theme, slow motion and **Interactive only**, in `~/.qaas/recorder-panel.json`. It fits from 520 px wide.
- **The hover box in the recorded page.** One box and label in every mode, its colour and tag saying which. The label
  says what a click does there (`Click`, `Fill`, `Choose`, `Tick`, `Pick`) and on what, the locator with how many it
  found and a green or amber dot, and once the pointer rests, why the others lost, without those the element has none
  of. It glides from element to element, drawn once a frame, keeps its last words dimmed until the next ones come, never
  covers the pointer or leaves the screen, and steps aside while dragging, selecting text or on a scrollbar. What the
  panel points at is boxed in the same style, every element a locator finds. The pill at the top of the page names the
  mode in its colour with the key that leaves it, and fades as the pointer comes near it.
- **The console** prints each mode as a line of its own, its word in a column (`● Recording`, `║ Paused`, `◇ Trying`,
  `› Testing`, `✓ Passed`, `× Failed`), with the panel's words: a checkbox is ticked and a radio picked, as `check` is a
  check of what the page shows. A click that only opens a select is no longer printed, so the numbers are the panel's
  step numbers; a part's replay no longer prints what the page did again; a check of an element's own text says it once.
- The raw recording `--keep-recording` keeps beside the flow leaves out passwords and secrets, as autosave does.

## 2.0.0

- Checks you choose: `F6` in the recorded page, or **Check** in the panel, turns on check mode, where a click picks an
  element instead of reaching the app, and a chooser beside it offers the checks that fit, the likeliest chosen: its
  text, a field's value, a checkbox's state, how many items there are, a table row by its cells, visible or gone,
  enabled or disabled, or the page's address. Enter adds it after the last action, as an `Expect` marked `// check`,
  which `--no-auto-checks` keeps. A value typed before reads its setting; a new record's number is matched as a
  message's is.
- The auto checks under control: a switch in the panel turns them all off or on, live, as `--no-auto-checks`, and each
  one's ✕ on its action's card drops it; a dropped check stays dropped, kept in the recording.
- The steps can be edited in the panel: delete an action (Delete), rename a step's method, change a setting's name or
  default, have a value typed unique per run, or type a date as days from today (`DueInDays: 3`), which the panel
  suggests for a date typed near the day of the recording. Undo takes back each change in turn. The edits and checks
  are part of the recording, so `--keep-recording`, autosave and `--continue` keep them.
- A later locator that shows a value typed before, such as a link to the ticket just named, reads its setting, as text
  and checks do.
- `BasePlaywrightFlow` has a protected `Unique(value)`: the value with the run's stamp appended (`Printer offline
  1103-142501-k7q`), the same for the whole run. Flows that mark a value unique need this version of QaaS.Playwright.
- The recorder never loses a recording: it writes it to `~/.qaas/recordings/` after every change, atomically and for
  the user's eyes only, one file per flow and folder, without the passwords and secrets it hides elsewhere. A recording
  that did not end in a save is offered when the recorder starts next: save it now without a browser, continue
  recording, or discard it. A file it cannot read is skipped with a warning.
- `qaas-playwright --continue <flow or .cs>` goes on with a saved flow: Chrome opens where it started, the login flow
  runs, the flow's steps replay in the recorded window as a test runs them, nothing recorded, and what is recorded
  then comes after them. When a step fails, keep recording from there or stop. Saving regenerates the flow and replaces
  it, unless it was changed by hand since. Asked for a name that is there already, the recorder offers to continue that
  flow, overwrite it, or take another name.
- The recorder writes cleaner flows. Each step method has a one-line summary of what it does, and is named after what
  was clicked (`AssignTicketAsync` for a row's "Assign" button); a step ends where the page answered a click with a
  message or a busy indicator, and a search or a login of one or two values stays one step with its click. A locator
  used twice, or a row two elements are in, is a local; long statements break between calls at 120 columns; the class
  comment says where it was recorded and how to run it.
- Actions repeated three times or more, differing only in the values typed or chosen, become a loop over a list setting
  (`foreach (var task in Configuration.NewTasks)`), a list of records for repeats of several values, or a count for
  repeats of none. Anything else that differs leaves them as recorded.
- A busy indicator that came up after an action and went away (`role="progressbar"`, as MUI's progress components are,
  or `aria-busy="true"`) is waited for after it, without a sleep.
- Checks are more precise: each is marked `// auto check`; an address is not checked when a dialog, a tab or a closed
  dialog with a message says the same; a message with a new record's number in it is found by the words around it.
  `--no-auto-checks` writes a flow with none, keeping the waits.
- Settings read better: a choice drops its field's `Id` (`MaintenanceType`), settings of one name are told apart by
  what they are for (`TicketToAssign`, `TicketToClose`), number fields are `int` or `decimal`, and a long record is
  grouped by step.
- The recorder's panel tests the flow recorded so far: **Test** (`Ctrl+Enter` or `F5` in the panel) builds the code
  exactly as it will be saved, with the .NET SDK and without restoring anything, and runs it as the probe does, in a
  browser context and window of its own in the recorder's Chrome: from the page the recording started on, after the
  recording's login flow, with the recorded values as its settings, the probe's timeouts, failure screenshot and
  hidden secrets. Each step shows as it runs, passes with its time, or fails, opened on what it waited for, on which
  page and line, the screenshot, and what to check by hand; the code marks each step's lines, the failed one in red,
  and the console prints the result. **Run to here** (`Ctrl+Shift+Enter`) runs the flow up to the selected step,
  **Stop** (`Esc`, `Shift+F5`) ends a run, and slow motion waits 500 ms before each action. A loop's line is followed
  repeat by repeat, and a failure is told at its repeat. Nothing a test does is recorded: the recording pauses while it
  runs.
- `PlaywrightPlatform` set in a `Directory.Packages.Local.props`, e.g. `win-x64`, packs the recorder with Playwright's
  driver for those platforms only.

## 1.3.0

- The recorder can log in first with a login flow of your tests: `--login LoginFlow --login-assembly
  bin/Debug/net10.0/MyTests.dll [--login-settings Flows/LoginFlow.flow.yaml]`. The flow runs as the probe runs a setup
  flow, nothing it does is recorded, the recording starts on the page it leaves, and the flow recorded says it runs
  after it. When it fails, the recorder says why, secrets hidden, and waits for you to log in by hand. The options are
  remembered per project folder; `--no-login` forgets them.
- `F8` in the recorded window, or **Pause** in the panel, pauses the recording, and resumes it. While paused nothing is
  recorded, and the overlay, the panel and the console say so. A page the app went to meanwhile is opened by its
  address after the pause, and listed to check by hand.
- The panel's **Elements** view shows the recorded page's elements as a tree, searchable by text, id or CSS selector, to
  pick one a hover cannot reach: it lists its locators, and re-targets the selected step to it, or adds a click or a
  fill on it. `F4` freezes the page's elements, so the items of a menu that closes when the focus leaves can be picked
  after it closed.
- The panel shows XML doc comments readably (their tags dimmed), wraps long lines between calls rather than inside
  names, and got a polish pass: states for paused, logging in and saving, and a layout that holds at narrow widths.
- Actions of a login flow in the recorder time out as in the probe: the recorder's own driver no longer runs in
  Playwright's debug mode, which waits forever.
- `ENV=local` or `Headless: false` runs in the Chrome on this machine again, as in earlier versions: the run uses
  `http://localhost:9222`, starting a Chrome with a window there if none answers, instead of a remote `BrowserUrl`.
- The recorder's console has a new look: a compact header with the keys, one line per step with the element's name,
  the value and its locator, cut to the console's width and with right-to-left names in place, what to check by hand
  in amber as you record, and a summary of the files, the counts, each line to check and the next steps. The questions
  say what is wrong with an answer and ask again, Ctrl+C stops politely, `--help` has sections and examples, and errors
  go to the standard error. Colours turn off when the output is redirected or `NO_COLOR` is set, glyphs fall back to
  ASCII where the console cannot show them, and `FORCE_COLOR` turns colours on.

## 1.2.1

- A `Directory.Packages.Local.props` beside `Directory.Packages.props`, if there is one, changes package versions, e.g.
  to those of your own feed, without editing the repository's files.
- Builds with several package sources configured (NU1507 no longer fails the build).

## 1.2.0

- While recording, a box follows the pointer over the element a click records, labelled with the locator the flow
  will use and why each locator tried before it lost (`id: found 2 (1 hidden)`, `id: looks generated`,
  `testid: none`). F9 hides or shows it.
- `Alt+↑` and `Alt+↓` pick the element the next click records: the one around the box, or back toward the pointer.
- A click on a plain element that looks clickable (a hand pointer, `onclick`, `tabindex`) records that element, like a
  button. When only Playwright's own locator finds the element, as for a carousel's wrapper, the element the click
  hits inside it with a stable id or test id is recorded instead.
- `--keep-recording` saves the raw recording beside the flow, with every locator tried, to share when an element was
  located badly.
- The recorder opens a panel beside the recorded window that shows the flow exactly as it will be saved, step by step,
  with its code and YAML. For each step it lists every locator found for the element and what each found, highlights
  it in the app, and takes another; it also undoes the last step, renames the flow and saves.
  `qaas-playwright record ... --no-panel` records without it.
- The recorder saves a flow into a folder that does not exist yet, instead of failing after the recording.

## 1.1.0

First public release.

- `PlaywrightFlowProbe` runs C# flows (`BasePlaywrightFlow<TConfig>`) in Chrome over CDP, each with typed settings
  from `FlowConfiguration:<FlowName>`; `PlaywrightFlowAssertion` reports each flow's result with a failure
  screenshot, every pop-up the flow left open, and the run's warnings.
- Settings are checked strictly before the browser opens: unknown keys, values that do not convert, unresolved
  `${...}`, and each flow's own settings, all reported at once.
- Each run gets a browser context of its own (`IsolateContext`); a login can be saved once and reused
  (`SaveStorageStatePath`, `LoadStorageStatePath`); `ForEach` runs a flow once per generated item, with
  `Parallelism` workers.
- `BrowserUrl` defaults to `http://localhost:9222`, where the probe starts Chrome when none answers.
- `EmulateDesktopPointer` makes a headless Chrome render the desktop layout.
- Secrets are hidden in failures, warnings, the report and log properties.
- The recorder, the .NET tool `qaas-playwright`, records in its own Chrome session and writes clean flows: stable
  locators, one method per step, an Expect after each action, and every typed or chosen value as a setting.
