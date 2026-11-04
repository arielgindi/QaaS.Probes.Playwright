import type { ComponentChildren } from 'preact';
import { useEffect, useRef, useState } from 'preact/hooks';
import { sendSetup, subscribeSetup } from '../bridge';
import { Icon, type IconName } from '../components/icons';
import { type Command, KeysSheet, Modal, Palette, ToastView } from '../components/Overlays';
import { Button, focusOnMount, Kbd, ModeMark, Spinner, type Tone, toneText } from '../components/ui';
import { cls } from '../text';
import { useTheme } from '../theme';
import { toast } from '../toast';
import type { BrowseView, LoginView, SetupView, Tab } from './types';

// The recorder's start screen: the project, what to record, the login to run first and two switches, then one Start;
// and once a flow is saved, what was saved. What is checked, and why, is the recorder's (Setup/SetupRules.cs): the
// screen sends each field as it changes, and shows what comes back.

const set = (field: string, value: string | boolean) => sendSetup({ type: 'setup', field, value });

// A field's problem shows once the person left it, or pressed Start: not while the screen is new.
const touched = new Set<string>();
let retouch: () => void = () => {};
const touch = (field: string) => {
  if (touched.has(field)) return;
  touched.add(field);
  retouch();
};
const start = () => {
  ['name', 'url'].forEach(touch);
  sendSetup({ type: 'start' });
};

export function SetupScreen() {
  const [view, setView] = useState<SetupView | null>(null);
  const [overlay, setOverlay] = useState<'palette' | 'keys' | null>(null);
  const [theme, toggleTheme] = useTheme();
  const shownNotice = useRef<string | undefined>(undefined);
  const [, rerender] = useState(0);
  retouch = () => rerender((count) => count + 1);
  useEffect(() => {
    // A new recording's fields are untouched again.
    let screen = '';
    const stop = subscribeSetup((next) => {
      if (next.screen !== screen) touched.clear();
      screen = next.screen;
      setView(next);
    });
    sendSetup({ type: 'ready' });
    return stop;
  }, []);
  useEffect(() => {
    document.title = view?.screen === 'done' ? `Saved ${view.done?.flow ?? ''} · QaaS Recorder` : 'QaaS Recorder';
    if (view?.notice && view.notice !== shownNotice.current) toast({ text: view.notice, alert: true });
    shownNotice.current = view?.notice;
  }, [view]);

  const keys = useRef<() => void>(() => {});
  keys.current = () => {
    if (!view) return;
    if (view.screen === 'done') sendSetup({ type: 'again' });
    else start();
  };
  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      const command = event.ctrlKey || event.metaKey;
      if (command && event.key === 'Enter') {
        event.preventDefault();
        keys.current();
      } else if (command && event.key.toLowerCase() === 'k') {
        event.preventDefault();
        setOverlay('palette');
      } else if (event.key === '?' && !(event.target instanceof HTMLInputElement)) setOverlay('keys');
    };
    addEventListener('keydown', onKey);
    return () => removeEventListener('keydown', onKey);
  }, []);

  if (!view) return <div class="h-full bg-canvas" />;
  const commands: Command[] = [
    { id: 'start', group: 'Record', icon: 'play', label: view.startLabel, keys: 'Ctrl ↵', off: !view.canStart && 'Fix what is marked first', run: start },
    { id: 'browse', group: 'Project', icon: 'file', label: 'Choose the project’s folder', run: () => sendSetup({ type: 'browse', mode: 'folder' }) },
    { id: 'login', group: 'Login', icon: 'key', label: view.draft.logIn ? 'Record without logging in' : 'Log in first', run: () => set('logIn', !view.draft.logIn) },
    { id: 'why', group: 'Login', icon: 'search', label: 'Why this login flow, or none', run: () => sendSetup({ type: 'why', on: true }) },
    { id: 'build', group: 'Login', icon: 'refresh', label: 'Build the test project', off: !view.login.buildCommand && 'No test project found', run: () => sendSetup({ type: 'build' }) },
    { id: 'theme', group: 'Show', icon: theme === 'dark' ? 'sun' : 'moon', label: theme === 'dark' ? 'Light theme' : 'Dark theme', run: toggleTheme },
    { id: 'keys', group: 'Help', icon: 'keyboard', label: 'Keyboard shortcuts', keys: '?', run: () => setOverlay('keys') },
  ];

  return (
    <div class="flex h-full flex-col bg-canvas">
      <header class="flex h-12 shrink-0 items-center gap-2 border-b border-line bg-panel pr-2 pl-4">
        <span class="grid size-6 place-items-center rounded-md bg-accent text-tag font-bold text-on-accent">Q</span>
        <h1 class="text-title font-semibold text-fg">QaaS Recorder</h1>
        <span class="text-meta text-faint">v{view.version}</span>
        <div class="ml-auto flex items-center gap-0.5">
          <Button icon="command" keys="Ctrl K" keysWide onClick={() => setOverlay('palette')} title="Every command (Ctrl+K)">Commands</Button>
          <Button iconOnly icon="keyboard" label="Keyboard shortcuts (?)" onClick={() => setOverlay('keys')} />
          <Button iconOnly icon={theme === 'dark' ? 'sun' : 'moon'} label={theme === 'dark' ? 'Light theme' : 'Dark theme'} onClick={toggleTheme} />
        </div>
      </header>
      <Strip view={view} />
      {view.screen === 'done' && view.done ? <Done view={view} /> : <Start view={view} />}
      {view.why && <WhySheet lines={view.why} />}
      {view.browse && <Browser browse={view.browse} />}
      {overlay === 'palette' && <Palette commands={commands} onClose={() => setOverlay(null)} />}
      {overlay === 'keys' && <KeysSheet onClose={() => setOverlay(null)} />}
      <ToastView />
    </div>
  );
}

// The one place that says what the recorder is doing: setting up, building, Chrome opening, or saved.
function Strip({ view }: { view: SetupView }) {
  const mode = view.mode;
  const tone = (view.starting ? 'accent' : mode.tone) as Tone;
  const busy = view.starting || mode.name === 'Building' || mode.name === 'Saving';
  return (
    <div role="status" aria-label={mode.name} aria-live="polite" class="flex h-10 shrink-0 items-center gap-2.5 border-b border-line bg-panel pr-2 pl-4">
      <span class={cls('flex shrink-0 items-center gap-2 text-body font-semibold', toneText[tone])}>
        <ModeMark tone={tone} busy={busy} />
        {view.starting ? 'Starting' : mode.label}
      </span>
      <span class="min-w-0 truncate text-body text-muted">{view.starting ? 'Chrome opens the app…' : mode.message}</span>
      <span class="ml-auto flex shrink-0 items-center gap-1">
        {mode.name === 'Building' && (
          <Button size="sm" variant="secondary" keys="Esc" onClick={() => sendSetup({ type: 'stopBuild' })}>Stop</Button>
        )}
        {mode.key && mode.name !== 'Building' && <Kbd>{mode.key}</Kbd>}
      </span>
    </div>
  );
}

function Start({ view }: { view: SetupView }) {
  const off = !view.project || view.project.gone;
  return (
    <>
      <main class="min-h-0 flex-1 overflow-y-auto">
        <div class="mx-auto flex max-w-[760px] flex-col gap-5 px-5 py-6">
          <Project view={view} />
          <Section title="What to record" off={off}>
            <WhatToRecord view={view} />
          </Section>
          <Section
            title="Log in first"
            off={off}
            aside={<Switch label="Log in first" on={view.draft.logIn} onChange={(on) => set('logIn', on)} />}
          >
            <Login login={view.login} on={view.draft.logIn} problem={view.problems.login} />
          </Section>
          <Section title="Options" off={off}>
            <div class="flex flex-col gap-2 px-4 py-3">
              <Check label="Auto checks: check what each action did" on={view.draft.autoChecks} onChange={(on) => set('autoChecks', on)}
                disabled={view.draft.tab !== 'new'} hint={view.draft.tab !== 'new' ? 'as the flow was recorded' : undefined} />
              <Check label="Keep the raw recording beside the flow" on={view.draft.keepRecording} onChange={(on) => set('keepRecording', on)}
                hint="with every locator tried, to continue it anywhere" />
            </div>
          </Section>
        </div>
      </main>
      <footer class="shrink-0 border-t border-line bg-panel">
        <div class="mx-auto flex max-w-[760px] items-center gap-4 px-5 py-3">
          <p class="min-w-0 flex-1 text-body text-muted" title={Object.values(view.problems).join('\n')}>
            {view.canStart ? view.willDo : Object.values(view.problems)[0] ?? view.willDo}
          </p>
          <Button variant="primary" icon={view.starting ? undefined : 'play'} keys="Ctrl ↵" disabled={!view.canStart || view.starting}
            class="h-10 px-5 text-title" onClick={start}>
            {view.starting ? 'Starting…' : view.startLabel}
          </Button>
        </div>
      </footer>
    </>
  );
}

function Section({ title, aside, off, children }: { title: string; aside?: ComponentChildren; off?: boolean; children: ComponentChildren }) {
  return (
    <section aria-label={title} class={cls(off && 'pointer-events-none opacity-45')}>
      <div class="mb-1.5 flex items-center gap-2 px-1">
        <h2 class="text-meta font-semibold tracking-wide text-muted uppercase">{title}</h2>
        {off ? <span class="ml-auto text-meta text-faint">Choose a project first</span> : aside && <span class="ml-auto">{aside}</span>}
      </div>
      <div class="rounded-lg border border-line bg-panel shadow-e1">{children}</div>
    </section>
  );
}

function Project({ view }: { view: SetupView }) {
  const project = view.project;
  return (
    <Section title="Project">
      <div class="flex flex-col gap-3 px-4 py-3">
        {project ? (
          <div class="flex items-start gap-3">
            <Icon name="file" size={18} class={cls('mt-0.5 shrink-0', project.gone ? 'text-bad' : 'text-faint')} />
            <div class="min-w-0 flex-1">
              <p class="text-title font-semibold text-fg">{project.name}</p>
              <p class="truncate text-meta text-muted" title={project.folder}>{project.folder} · {project.found}</p>
            </div>
            <Button size="sm" variant="secondary" icon="file" onClick={() => sendSetup({ type: 'browse', mode: 'folder' })}>Browse…</Button>
          </div>
        ) : (
          <div class="flex items-start gap-3">
            <Icon name="file" size={18} class="mt-0.5 shrink-0 text-faint" />
            <p class="min-w-0 flex-1 text-body text-muted">
              No project yet: the recorder was started from a shortcut, or from a folder that holds no project. Choose your test project’s folder; flows are saved there.
            </p>
            <Button size="sm" variant="primary" icon="file" onClick={() => sendSetup({ type: 'browse', mode: 'folder' })} autoFocus>Browse…</Button>
          </div>
        )}
        {project && view.problems.project && <Problem text={view.problems.project} />}
        {project && !project.gone && (
          <Field label="Save flows in" hint={project.namespace && `namespace ${project.namespace}`}>
            <TextInput value={view.draft.outputDir} field="outputDir" mono class="w-56" />
          </Field>
        )}
        {view.recent.filter((recent) => recent.folder !== project?.folder).length > 0 && (
          <div class="flex flex-wrap items-center gap-1.5">
            <span class="text-meta text-faint">Recent</span>
            {view.recent.filter((recent) => recent.folder !== project?.folder).map((recent) => (
              <span key={recent.folder} class="inline-flex items-center rounded-md border border-line bg-raised">
                <button type="button" disabled={recent.gone} title={`${recent.folder} · ${recent.when}`} onClick={() => set('project', recent.folder)}
                  class={cls('h-6 px-2 text-meta text-fg hover:text-accent-fg', recent.gone && 'line-through opacity-60')}>
                  {recent.name}
                </button>
                {recent.gone && <Button size="sm" iconOnly icon="cross" label={`Remove ${recent.name}`} onClick={() => sendSetup({ type: 'forget', folder: recent.folder })} />}
              </span>
            ))}
          </div>
        )}
      </div>
    </Section>
  );
}

const tabs: [Tab, string, IconName][] = [['new', 'New flow', 'plus'], ['continue', 'Continue a saved flow', 'recordAfter'], ['recovered', 'Recovered', 'restore']];

function WhatToRecord({ view }: { view: SetupView }) {
  const draft = view.draft;
  const shown = tabs.filter(([tab]) => tab !== 'recovered' || view.recovered.length > 0);
  return (
    <div class="flex flex-col">
      <div role="tablist" aria-label="What to record" class="flex gap-1 border-b border-line px-3 pt-2">
        {shown.map(([tab, label, icon]) => (
          <button key={tab} type="button" role="tab" aria-selected={draft.tab === tab} onClick={() => set('tab', tab)}
            class={cls('-mb-px inline-flex h-8 items-center gap-1.5 border-b-2 px-2.5 text-body font-medium transition',
              draft.tab === tab ? 'border-accent-fg text-fg' : 'border-transparent text-muted hover:text-fg')}>
            <Icon name={icon} size={13} />
            {label}
            {tab === 'recovered' && <span class="rounded-sm bg-warn/15 px-1 text-tag text-warn">{view.recovered.length}</span>}
          </button>
        ))}
      </div>
      <div class="flex flex-col gap-3 px-4 py-3">
        {draft.tab === 'new' && <NewFlow view={view} />}
        {draft.tab === 'continue' && <Continue view={view} />}
        {draft.tab === 'recovered' && <Recovered view={view} />}
      </div>
    </div>
  );
}

function NewFlow({ view }: { view: SetupView }) {
  const draft = view.draft;
  const [open, setOpen] = useState(false);
  return (
    <>
      <Field label="Name" problem={touched.has('name') ? view.problems.name : undefined} hint={view.name && `→ ${view.name.className}, ${view.name.target}`}>
        <TextInput value={draft.name} field="name" placeholder="e.g. new-order" focus class="w-64" />
      </Field>
      {view.problems.ifExists && (
        <div class="ml-[112px] flex flex-col gap-1.5 rounded-md border border-warn/40 bg-warn/[0.06] px-3 py-2">
          <p class="flex items-center gap-1.5 text-body text-warn"><Icon name="warn" size={13} />{view.problems.ifExists}</p>
          <div class="flex gap-1.5">
            <Button size="sm" variant="secondary" icon="recordAfter" onClick={() => set('ifExists', 'continue')}>Continue it</Button>
            <Button size="sm" variant="secondary" icon="rerecord" onClick={() => set('ifExists', 'replace')}>Replace it with a new recording</Button>
          </div>
        </div>
      )}
      {draft.ifExists !== 'unset' && !view.problems.ifExists && view.name && (
        <p class="ml-[112px] text-meta text-muted">
          {draft.ifExists === 'continue' ? `Continues ${view.name.className}: its steps replay first.` : `Replaces ${view.name.target} with this recording.`}{' '}
          <button type="button" class="text-accent-fg hover:underline" onClick={() => set('ifExists', 'unset')}>Change</button>
        </p>
      )}
      <Field label="Start page" problem={draft.ifExists === 'continue' || !touched.has('url') ? undefined : view.problems.url}>
        <div class="relative">
          <TextInput value={draft.url} field="url" placeholder="https://" mono class="w-[440px] max-w-full" disabled={draft.ifExists === 'continue'}
            onFocus={() => setOpen(true)} onBlur={() => window.setTimeout(() => setOpen(false), 150)} />
          {open && view.urls.length > 0 && (
            <ul role="listbox" aria-label="Start pages" class="absolute top-9 left-0 z-20 w-[440px] max-w-full overflow-hidden rounded-lg border border-line-strong bg-raised py-1 shadow-e2">
              {view.urls.map((choice) => (
                <li key={choice.url + choice.from} role="option" aria-selected={choice.url === draft.url}>
                  <button type="button" onMouseDown={(event) => event.preventDefault()} onClick={() => { set('url', choice.url); setOpen(false); }}
                    class="flex w-full items-center gap-3 px-3 py-1.5 text-left hover:bg-panel">
                    <span class="w-20 shrink-0 text-meta text-faint">{choice.from === 'recent' ? 'recent' : 'your tests'}</span>
                    <span class="min-w-0 flex-1 truncate font-mono text-meta text-fg">{choice.url}</span>
                    {choice.from !== 'recent' && <span class="shrink-0 truncate text-tag text-faint">{choice.from}</span>}
                  </button>
                </li>
              ))}
            </ul>
          )}
        </div>
      </Field>
      {view.fixUrl && (
        <p class="ml-[112px] text-meta text-muted">
          <button type="button" class="font-medium text-accent-fg hover:underline" onClick={() => set('url', view.fixUrl!)}>Use {view.fixUrl}</button>
        </p>
      )}
    </>
  );
}

function Continue({ view }: { view: SetupView }) {
  if (view.flows.length === 0) return <Empty icon="file" text="No flow in this folder yet: record one under New flow." />;
  return (
    <>
      <div role="radiogroup" aria-label="The flow to continue" class="flex max-h-64 flex-col overflow-y-auto rounded-md border border-line">
        {view.flows.map((flow) => (
          <label key={flow.name} title={flow.why}
            class={cls('flex items-center gap-3 border-b border-line px-3 py-2 last:border-b-0', flow.kept ? 'cursor-pointer hover:bg-raised' : 'opacity-55')}>
            <input type="radio" name="continue" disabled={!flow.kept} checked={view.draft.continueFlow === flow.name} onChange={() => set('continueFlow', flow.name)} class="accent-[var(--accent)]" />
            <span class="w-44 shrink-0 truncate text-body font-medium text-fg">{flow.name}</span>
            <span class="min-w-0 flex-1 truncate text-meta text-muted">{flow.kept ? flow.detail : flow.why}</span>
          </label>
        ))}
      </div>
      {view.problems.continue && <Problem text={view.problems.continue} />}
      <p class="text-meta text-muted">Its steps replay first in the app’s window, as Test runs them; then what you do is added after them.</p>
    </>
  );
}

function Recovered({ view }: { view: SetupView }) {
  const [discard, setDiscard] = useState<string | null>(null);
  return (
    <>
      {view.recovered.map((kept) => (
        <div key={kept.path} class={cls('flex flex-col gap-2 rounded-md border px-3 py-2.5', view.draft.recovered === kept.path ? 'border-accent/60 bg-accent/[0.05]' : 'border-line')}>
          <button type="button" class="flex items-baseline gap-2 text-left" onClick={() => set('recovered', kept.path)}>
            <span class="text-body font-semibold text-fg">{kept.flow}</span>
            <span class="min-w-0 truncate text-meta text-muted">{kept.detail}</span>
            {!kept.here && <span class="ml-auto shrink-0 text-tag text-faint">another project</span>}
          </button>
          <div class="flex flex-wrap items-center gap-1.5">
            <Button size="sm" variant="secondary" icon="file" onClick={() => sendSetup({ type: 'recovered', path: kept.path, act: 'save' })}>Save it now</Button>
            <Button size="sm" variant="secondary" icon="recordAfter" onClick={() => sendSetup({ type: 'recovered', path: kept.path, act: 'continue' })}>Continue recording</Button>
            {discard === kept.path ? (
              <>
                <span class="text-meta text-bad">Discard it for good?</span>
                <Button size="sm" variant="danger" autoFocus onClick={() => { setDiscard(null); sendSetup({ type: 'recovered', path: kept.path, act: 'discard' }); }}>Discard</Button>
                <Button size="sm" onClick={() => setDiscard(null)}>Keep it</Button>
              </>
            ) : (
              <Button size="sm" icon="trash" onClick={() => setDiscard(kept.path)}>Discard…</Button>
            )}
          </div>
        </div>
      ))}
      {view.problems.recovered && <Problem text={view.problems.recovered} />}
    </>
  );
}

function Login({ login, on, problem }: { login: LoginView; on: boolean; problem?: string }) {
  const why = <button type="button" class="text-meta font-medium text-accent-fg hover:underline" onClick={() => sendSetup({ type: 'why', on: true })}>{login.flow ? 'Why this flow?' : 'Why no login?'}</button>;
  if (login.state === 'looking')
    return (
      <div class="flex flex-col gap-2 px-4 py-3" aria-busy="true">
        <p class="flex items-center gap-2 text-body text-muted"><Spinner />{login.detail ?? 'Looking for your login flow…'}</p>
        <span class="skeleton h-3 w-2/3" />
        <span class="skeleton h-3 w-1/2" />
      </div>
    );
  return (
    <div class={cls('flex flex-col gap-2.5 px-4 py-3', !on && 'opacity-55')}>
      {login.state === 'choose' && (
        <>
          <p class="text-body text-muted">{login.none}</p>
          <Field label="Test project">
            <Select value="" field="loginProject"
              options={[['', 'Choose the test project that logs in'], ...login.projects.map((project) => [project.file, project.name] as [string, string])]} />
          </Field>
        </>
      )}
      {login.state === 'found' ? (
        <>
          <div class="flex items-center gap-3">
            <Icon name="key" size={16} class="shrink-0 text-faint" />
            <div class="min-w-0 flex-1">
              <p class="text-body font-semibold text-fg">{login.flow}</p>
              <p class="truncate text-meta text-muted">{login.detail}</p>
            </div>
            {login.flows.length > 1 && (
              <Select value={login.flow ?? ''} field="loginFlow" options={login.flows.map((flow) => [flow.name, `${flow.name} · ${flow.detail}`] as [string, string])} />
            )}
          </div>
          {login.baseUrl && <Row label="BaseUrl"><span class="font-mono text-meta text-fg">{login.baseUrl}</span></Row>}
          {login.settingsFiles.length > 0 && (
            <Row label="Values from">
              <Select value={login.settings ?? ''} field="loginSettings"
                options={[...login.settingsFiles.map((file) => [file.file, file.name] as [string, string]), ['', 'none: its defaults']]} />
              <Button size="sm" onClick={() => sendSetup({ type: 'browse', mode: 'yaml' })}>Choose a file…</Button>
            </Row>
          )}
          {login.values.length > 0 && (
            <div class="flex flex-col gap-1.5">
              {login.values.map((value) => <Value key={value.name} value={value} />)}
            </div>
          )}
          {login.values.some((value) => value.missing) && (
            <p class="text-meta text-faint">Or give them in your test YAML, under FlowConfiguration: {login.flow}:</p>
          )}
        </>
      ) : (
        login.state === 'none' && (
          <>
            <p class="text-body text-muted">{login.none ?? 'No login flow: the recording starts signed out.'}</p>
            {login.flows.length > 0 && (
              <Field label="Log in with">
                <Select value="" field="loginFlow" options={[['', 'Choose one of your tests’ flows'], ...login.flows.map((flow) => [flow.name, `${flow.name} · ${flow.detail}`] as [string, string])]} />
              </Field>
            )}
          </>
        )
      )}
      {login.stale && (
        <div class="flex flex-col gap-1.5 rounded-md border border-warn/40 bg-warn/[0.06] px-3 py-2">
          <p class="flex items-center gap-1.5 text-body text-warn"><Icon name="warn" size={13} />{login.stale}</p>
          <div class="flex flex-wrap items-center gap-1.5">
            <Button size="sm" variant="secondary" icon="refresh" disabled={login.building} onClick={() => sendSetup({ type: 'build' })}>{login.building ? 'Building…' : 'Build now'}</Button>
            <code class="font-mono text-meta text-muted">{login.buildCommand}</code>
          </div>
        </div>
      )}
      {login.buildLog && <pre class="max-h-40 overflow-auto rounded-md border border-bad/40 bg-canvas p-2 font-mono text-tag whitespace-pre-wrap text-muted">{login.buildLog}</pre>}
      {login.problems.map((text) => <Problem key={text} text={text} tone="warn" />)}
      {problem && on && <Problem text={problem} />}
      <div class="flex items-center gap-3">
        {why}
        <button type="button" class="text-meta text-muted hover:text-fg" onClick={() => sendSetup({ type: 'refresh' })}>Look again</button>
      </div>
    </div>
  );
}

function Value({ value }: { value: LoginView['values'][number] }) {
  const [shown, setShown] = useState(false);
  return (
    <Row label={value.name}>
      {value.missing || value.from === 'typed here' ? (
        <>
          <TextInput value={value.value} field={`value:${value.name}`} type={value.secret && !shown ? 'password' : 'text'} mono class="w-56"
            placeholder={value.secret ? (value.from === 'typed here' ? 'typed, kept for this session' : 'type it here') : 'needed'} />
          {value.secret && <Button size="sm" onClick={() => setShown(!shown)}>{shown ? 'Hide' : 'Show'}</Button>}
          <span class="text-meta text-faint">{value.secret ? 'a secret: for this session only, never saved' : 'remembered for this project'}</span>
        </>
      ) : (
        <>
          <span class="w-56 truncate font-mono text-meta text-fg">{value.secret ? '••••••••' : value.value}</span>
          <span class="truncate text-meta text-faint">from {value.from}</span>
        </>
      )}
    </Row>
  );
}

function Done({ view }: { view: SetupView }) {
  const done = view.done!;
  return (
    <main class="min-h-0 flex-1 overflow-y-auto">
      <div class="mx-auto flex max-w-[760px] flex-col gap-4 px-5 py-6">
        <div class="flex items-center gap-3">
          <span class={cls('grid size-10 place-items-center rounded-xl', done.saved ? 'bg-ok/15 text-ok' : 'bg-bad/15 text-bad')}>
            <Icon name={done.saved ? 'tick' : 'warn'} size={20} />
          </span>
          <div>
            <h2 class="text-head font-semibold text-fg">{done.saved ? `Saved ${done.flow}` : `${done.flow} was not saved`}</h2>
            <p class="text-body text-muted">{done.summary}</p>
          </div>
        </div>
        {done.error && <Problem text={done.error} />}
        {done.files.length > 0 && (
          <div class="rounded-lg border border-line bg-panel shadow-e1">
            {done.files.map((file) => (
              <div key={file.path} class="flex items-center gap-3 border-b border-line px-4 py-2 last:border-b-0">
                <Icon name="file" size={14} class="shrink-0 text-faint" />
                <span class="min-w-0 flex-1 truncate font-mono text-meta text-fg" title={file.path}>{file.path}</span>
                <span class="shrink-0 text-meta text-muted">{file.what}</span>
              </div>
            ))}
          </div>
        )}
        {done.notes.map((note) => <Problem key={note} text={note} tone="warn" />)}
        {done.checks.length > 0 && (
          <section aria-label="To check by hand" class="rounded-lg border border-line bg-panel px-4 py-3 shadow-e1">
            <h3 class="mb-1.5 text-meta font-semibold text-muted">To check by hand</h3>
            <ul class="flex flex-col gap-1">
              {done.checks.map((check) => <li key={check} class="flex gap-2 text-body text-fg"><Icon name="warn" size={13} class="mt-0.5 shrink-0 text-warn" />{check}</li>)}
            </ul>
          </section>
        )}
        <div class="flex flex-wrap items-center gap-2 pt-2">
          <Button variant="primary" icon="plus" keys="Ctrl ↵" class="h-10 px-5 text-title" autoFocus onClick={() => sendSetup({ type: 'again' })}>Record another</Button>
          {done.saved && <Button variant="secondary" icon="recordAfter" onClick={() => sendSetup({ type: 'continueSaved' })}>Continue {done.flow}</Button>}
          {done.files.length > 0 && <Button variant="secondary" icon="file" onClick={() => sendSetup({ type: 'openFolder' })}>Open the folder</Button>}
          <Button class="ml-auto" onClick={() => sendSetup({ type: 'quit' })}>Close</Button>
        </div>
      </div>
    </main>
  );
}

function WhySheet({ lines }: { lines: { depth: number; text: string }[] }) {
  const close = () => sendSetup({ type: 'why', on: false });
  const text = lines.map((line) => `${'  '.repeat(line.depth)}${line.text}`).join('\n');
  return (
    <Modal label="Why this login flow" onClose={close} class="max-w-[760px]">
      <div class="flex items-center gap-2 border-b border-line px-4 py-3">
        <h2 class="text-title font-semibold text-fg">Where the recorder looked for your login flow</h2>
        <Button size="sm" icon="copy" class="ml-auto" onClick={() => { void navigator.clipboard?.writeText(text); toast({ text: 'Copied the search' }); }}>Copy</Button>
        <Button size="sm" iconOnly icon="cross" label="Close" autoFocus onClick={close} />
      </div>
      <div class="min-h-0 overflow-y-auto px-4 py-3">
        {lines.map((line, at) => (
          <p key={at} class={cls('text-body', line.text.startsWith('Result') ? 'font-medium text-fg' : 'text-fg/85')} style={{ paddingLeft: `${line.depth * 16}px` }}>{line.text}</p>
        ))}
      </div>
    </Modal>
  );
}

function Browser({ browse }: { browse: BrowseView }) {
  const [path, setPath] = useState(browse.path);
  useEffect(() => setPath(browse.path), [browse.path]);
  const close = () => sendSetup({ type: 'browseClose' });
  const what = browse.mode === 'folder' ? 'the project’s folder' : browse.mode === 'dll' ? 'the DLL that has the login flow' : 'the YAML of the login’s values';
  return (
    <Modal label={`Choose ${what}`} onClose={close} class="max-w-[680px]">
      <div class="flex items-center gap-2 border-b border-line px-4 py-3">
        <h2 class="text-title font-semibold text-fg">Choose {what}</h2>
        <Button size="sm" iconOnly icon="cross" label="Close" class="ml-auto" onClick={close} />
      </div>
      <form class="flex items-center gap-1.5 px-4 pt-3" onSubmit={(event) => { event.preventDefault(); sendSetup({ type: 'browse', path, mode: browse.mode }); }}>
        <input ref={focusOnMount} value={path} spellcheck={false} aria-label="Folder" onInput={(event) => setPath(event.currentTarget.value)}
          class="h-8 min-w-0 flex-1 rounded-md border border-line-strong bg-canvas px-2 font-mono text-meta text-fg outline-none focus:border-accent" />
        <Button size="sm" variant="secondary" icon="up" disabled={!browse.parent} onClick={() => sendSetup({ type: 'browse', path: browse.parent, mode: browse.mode })}>Up</Button>
        <Button size="sm" variant="secondary" type="submit">Go</Button>
      </form>
      <div class="flex flex-wrap gap-1 px-4 pt-2">
        {browse.places.map((place) => (
          <Button key={place.path} size="sm" onClick={() => sendSetup({ type: 'browse', path: place.path, mode: browse.mode })}>{place.name}</Button>
        ))}
      </div>
      {browse.error && <div class="px-4 pt-2"><Problem text={browse.error} /></div>}
      <div class="mx-4 mt-2 min-h-24 overflow-y-auto rounded-md border border-line">
        {browse.entries.length === 0 && <p class="px-3 py-2 text-meta text-faint">Nothing to choose here.</p>}
        {browse.entries.map((entry) => (
          <button key={entry.path} type="button" class="flex w-full items-center gap-2.5 border-b border-line px-3 py-1.5 text-left last:border-b-0 hover:bg-raised"
            onClick={() => (entry.folder ? sendSetup({ type: 'browse', path: entry.path, mode: browse.mode }) : sendSetup({ type: 'use', path: entry.path, mode: browse.mode }))}>
            <Icon name={entry.folder ? 'chevron' : 'file'} size={13} class="shrink-0 text-faint" />
            <span class="min-w-0 flex-1 truncate text-body text-fg">{entry.name}</span>
            {entry.detail && <span class="shrink-0 text-meta text-accent-fg">{entry.detail}</span>}
          </button>
        ))}
      </div>
      <div class="flex items-center gap-2 px-4 py-3">
        <p class="min-w-0 flex-1 truncate text-meta text-muted">{browse.note}</p>
        <Button onClick={close}>Cancel</Button>
        {browse.mode === 'folder' && <Button variant="primary" onClick={() => sendSetup({ type: 'use', path: browse.path, mode: 'folder' })}>Use this folder</Button>}
      </div>
    </Modal>
  );
}

function Field({ label, problem, hint, children }: { label: string; problem?: string; hint?: string | false; children: ComponentChildren }) {
  return (
    <div class="flex flex-col gap-1">
      <div class="flex items-center gap-3">
        <span class="w-[100px] shrink-0 text-body text-muted">{label}</span>
        {children}
        {hint && <span class="min-w-0 truncate text-meta text-faint">{hint}</span>}
      </div>
      {problem && <div class="ml-[112px]"><Problem text={problem} /></div>}
    </div>
  );
}

function Row({ label, children }: { label: string; children: ComponentChildren }) {
  return (
    <div class="flex items-center gap-3">
      <span class="w-[100px] shrink-0 truncate text-meta text-muted">{label}</span>
      {children}
    </div>
  );
}

// A field the recorder checks as it changes: what is typed stays as typed while it has the focus.
function TextInput(props: { value: string; field: string; placeholder?: string; mono?: boolean; class?: string; type?: string; focus?: boolean; disabled?: boolean; onFocus?(): void; onBlur?(): void }) {
  const [text, setText] = useState(props.value);
  const focused = useRef(false);
  const timer = useRef(0);
  useEffect(() => {
    if (!focused.current) setText(props.value);
  }, [props.value]);
  const send = (value: string) => {
    window.clearTimeout(timer.current);
    timer.current = window.setTimeout(() => set(props.field, value), 150);
  };
  return (
    <input
      ref={props.focus ? focusOnMount : undefined}
      type={props.type ?? 'text'}
      value={text}
      disabled={props.disabled}
      spellcheck={false}
      autocomplete="off"
      placeholder={props.placeholder}
      aria-label={props.field}
      dir={props.mono ? 'ltr' : 'auto'}
      onInput={(event) => { setText(event.currentTarget.value); send(event.currentTarget.value); }}
      onFocus={() => { focused.current = true; props.onFocus?.(); }}
      onBlur={() => { focused.current = false; window.clearTimeout(timer.current); set(props.field, text); if (text.length > 0) touch(props.field); props.onBlur?.(); }}
      onKeyDown={(event) => { if (event.key === 'Enter') { window.clearTimeout(timer.current); set(props.field, text); if (props.field === 'url') window.setTimeout(start, 60); } }}
      class={cls('h-8 min-w-0 rounded-md border border-line-strong bg-canvas px-2 text-body text-fg outline-none placeholder:text-faint focus:border-accent disabled:opacity-50', props.mono && 'font-mono text-meta', props.class)}
    />
  );
}

function Select({ value, field, options }: { value: string; field: string; options: [string, string][] }) {
  return (
    <select value={value} aria-label={field} onChange={(event) => set(field, event.currentTarget.value)}
      class="h-8 max-w-[440px] min-w-0 rounded-md border border-line-strong bg-canvas px-1.5 text-body text-fg outline-none focus:border-accent">
      {options.map(([option, label]) => <option key={option} value={option}>{label}</option>)}
    </select>
  );
}

function Switch({ label, on, onChange }: { label: string; on: boolean; onChange(on: boolean): void }) {
  return (
    <button type="button" role="switch" aria-checked={on} aria-label={label} onClick={() => onChange(!on)}
      class="inline-flex items-center gap-2 text-meta font-medium text-muted hover:text-fg">
      {on ? 'On' : 'Off'}
      <span class={cls('relative inline-block h-4 w-7 rounded-full transition', on ? 'bg-accent' : 'bg-line-strong')}>
        <span class={cls('absolute top-0.5 size-3 rounded-full bg-white shadow-e1 transition-[left]', on ? 'left-3.5' : 'left-0.5')} />
      </span>
    </button>
  );
}

function Check({ label, on, onChange, hint, disabled }: { label: string; on: boolean; onChange(on: boolean): void; hint?: string; disabled?: boolean }) {
  return (
    <label class={cls('flex items-center gap-2.5 text-body text-fg', disabled ? 'opacity-55' : 'cursor-pointer')}>
      <input type="checkbox" checked={on} disabled={disabled} onChange={(event) => onChange(event.currentTarget.checked)} class="size-4 accent-[var(--accent)]" />
      {label}
      {hint && <span class="text-meta text-faint">{hint}</span>}
    </label>
  );
}

function Problem({ text, tone = 'bad' }: { text: string; tone?: 'bad' | 'warn' }) {
  return (
    <p role="alert" class={cls('flex items-start gap-1.5 text-meta', tone === 'bad' ? 'text-bad' : 'text-warn')}>
      <Icon name="warn" size={13} class="mt-0.5 shrink-0" />
      <span class="min-w-0">{text}</span>
    </p>
  );
}

function Empty({ icon, text }: { icon: IconName; text: string }) {
  return <p class="flex items-center gap-2 text-body text-muted"><Icon name={icon} size={14} class="text-faint" />{text}</p>;
}
