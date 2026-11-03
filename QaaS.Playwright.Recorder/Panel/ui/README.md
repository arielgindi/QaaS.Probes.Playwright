# The recorder's panel

The page the recorder shows beside the recorded window (see [Recording flows](../../../docs/RECORDING.md#the-panel)).
It is built with Vite, Preact, TypeScript and Tailwind into one self-contained HTML file, every script and style inline,
because the recorder shows it in a browser that may have no network. The built file is committed as
`../panel.html` and embedded in the recorder, so building the recorder needs no Node.

## Rebuilding it

After changing anything here, rebuild the page and commit `../panel.html` with your change:

```bash
cd QaaS.Playwright.Recorder/Panel/ui
npm ci          # once; Node 20.19 or later
npm run build   # type-checks, then writes dist/panel.html and copies it to ../panel.html
```

`npm run dev` serves the panel with a sample recording (`src/sample.ts`) and reloads as you edit; the commands it
would send the recorder are logged in the browser's console, and the Elements view shows a sample page
(`/panel.html?frozen` as frozen with F4). **Test** runs a simulated test run (`?fail` makes it fail at a step), and
`?test=building`, `window`, `start`, `running`, `passed`, `failed` or `error` opens on one. `npm run check` only
type-checks.

## How it talks to the recorder

There is no server: the recorder (`../RecorderPanel.cs`) opens the page in its Chrome, in a browser context of its own,
and the two talk through two functions on the window (`src/bridge.ts`):

- `window.__qaasPanelShow(json)`: the recorder calls it with each state, the flow exactly as it will be saved
  (`../PanelView.cs`, typed in `src/types.ts`).
- `window.__qaasPanelElements(json)`: the recorder calls it with the recorded page's elements for the Elements view: a
  level of the live tree, a search's matches, the tree frozen with F4, what the app in inspect mode points at or picked,
  or that the page changed.
- `window.__qaasPanelSend(json)`: the recorder exposes it for the panel's commands: `ready`, `choose`, `highlight`,
  `unhighlight`, `undo`, `rename`, `save`, `pause`, `loginByHand` (stops the login flow, to log in by hand), for the
  Elements view `elements` (`tree`, `search`, `watch`, `inspect`, `mark` and `preview`), `pick`, `choosePicked`,
  `useForStep`, `addClick`, `addFill` and `addCheck`, for testing the flow
  `test`, `stopTest` and `clearTest`, for checks and edits `checkMode`, `autoChecks`, `dropCheck`, `delete`,
  `renameStep` and `editValue`, for recording a part again `rerecord`, `rerecordDone`, `rerecordCancel`, `rerecordFrom`
  and `rerecordDismiss`, and for trying `try` and `addTried`.

## Rules

- Nothing from the network at runtime: no CDN, no web fonts. The fonts are system stacks: Inter or Segoe UI for the UI,
  JetBrains Mono, Cascadia or Consolas for code.
- Versions are pinned exactly, and `package-lock.json` is committed, so `npm ci` builds the same page.
- Text from the app (element names, values, dialog titles) may be right-to-left: show it with `dir="auto"`. Code stays
  left-to-right.
- C# and YAML are highlighted by the small tokenizers in `src/syntax`, not by a library.
- Colours are tokens in `src/styles.css`, one set per theme.
