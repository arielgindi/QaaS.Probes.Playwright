import { useEffect, useMemo, useRef, useState } from 'preact/hooks';
import { flash, highlight, send, subscribe } from './bridge';
import { CodeView } from './components/CodeView';
import { Icon } from './components/icons';
import { Inspector } from './components/Inspector';
import { Timeline } from './components/Timeline';
import { TopBar, type View } from './components/TopBar';
import { plural } from './text';
import { useTheme } from './theme';
import type { Action, PanelState } from './types';

const waiting: PanelState = { flow: '', target: '', status: 'recording', code: '', yaml: '', steps: [], checks: [] };

export function App() {
  const [state, setState] = useState<PanelState>(waiting);
  const [selected, setSelected] = useState<string | null>(null);
  const [follow, setFollow] = useState(true);
  const [hovered, setHovered] = useState<string | null>(null);
  const [view, setView] = useState<View>('code');
  const [dismissed, setDismissed] = useState<string | null>(null);
  const [theme, toggleTheme] = useTheme();

  useEffect(() => {
    const stop = subscribe(setState);
    send({ type: 'ready' });
    return stop;
  }, []);

  const actions = useMemo(() => state.steps.flatMap((step) => step.actions), [state]);
  const owners = useMemo(() => {
    const byLine = new Map<number, string>();
    for (const action of actions) if (action.lines) for (let line = action.lines.first; line <= action.lines.last; line++) byLine.set(line, action.id);
    return byLine;
  }, [actions]);
  const saving = state.status === 'saving';
  const last = actions.at(-1)?.id ?? null;

  // The newest action is selected as it comes, unless the person is looking at an older one.
  useEffect(() => {
    if (follow || !actions.some((action) => action.id === selected)) {
      setSelected(last);
      setFollow(true);
    }
  }, [actions]);

  useEffect(() => {
    document.title = state.flow ? `${state.flow} · QaaS Recorder` : 'QaaS Recorder';
  }, [state.flow]);

  const select = (id: string) => {
    setSelected(id);
    setFollow(id === last);
  };
  const hover = (id: string | null) => {
    setHovered(id);
    highlight(id);
  };
  const undo = () => {
    if (actions.length > 0 && !saving) send({ type: 'undo' });
  };
  const save = () => {
    if (actions.length > 0 && !saving) send({ type: 'save' });
  };
  const choose = (action: Action, candidate: number | null) => send({ type: 'choose', action: action.id, candidate });

  // Keys: ↑ and ↓ move through the steps, Ctrl+Z undoes the last, Ctrl+S saves.
  const keys = useRef({ actions, selected, select, undo, save });
  keys.current = { actions, selected, select, undo, save };
  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      const current = keys.current;
      const command = event.ctrlKey || event.metaKey;
      const key = event.key.toLowerCase();
      if (command && key === 's') {
        event.preventDefault();
        current.save();
        return;
      }
      if (event.target instanceof Element && event.target.closest('input, textarea')) return;
      if (command && key === 'z' && !event.shiftKey) {
        event.preventDefault();
        current.undo();
      } else if ((event.key === 'ArrowDown' || event.key === 'ArrowUp') && !command && current.actions.length > 0) {
        event.preventDefault();
        const at = current.actions.findIndex((action) => action.id === current.selected);
        const to = at < 0 ? current.actions.length - 1 : at + (event.key === 'ArrowDown' ? 1 : -1);
        const next = current.actions[Math.max(0, Math.min(current.actions.length - 1, to))];
        current.select(next.id);
        flash(next.id);
      }
    };
    addEventListener('keydown', onKey);
    return () => removeEventListener('keydown', onKey);
  }, []);

  const selectedAction = actions.find((action) => action.id === selected);
  const hoveredAction = actions.find((action) => action.id === hovered);
  const notes = state.checks.filter((check) => !check.line).map((check) => check.message);

  return (
    <div class="flex h-full flex-col">
      <TopBar
        flow={state.flow}
        stamp={state}
        saving={saving}
        canSave={actions.length > 0}
        canUndo={actions.length > 0}
        view={view}
        theme={theme}
        onRename={(name) => send({ type: 'rename', name })}
        onUndo={undo}
        onSave={save}
        onView={setView}
        onTheme={toggleTheme}
      />
      {state.error && state.error !== dismissed && (
        <div role="alert" class="flex shrink-0 items-start gap-2 border-b border-bad/30 bg-bad/10 px-4 py-2 text-[12.5px] text-bad">
          <Icon name="warn" size={14} class="mt-0.5 shrink-0" />
          <span class="min-w-0 flex-1">{state.error}</span>
          <button type="button" aria-label="Dismiss" onClick={() => setDismissed(state.error ?? null)} class="rounded p-0.5 transition hover:bg-bad/15">
            <Icon name="cross" size={13} />
          </button>
        </div>
      )}
      <main class="layout min-h-0 flex-1">
        <Timeline steps={state.steps} count={actions.length} notes={notes} selected={selected} onSelect={select} onHover={hover} />
        <CodeView
          view={view}
          code={state.code}
          yaml={state.yaml}
          target={state.target}
          checks={state.checks}
          selected={selectedAction}
          hovered={hoveredAction}
          owners={owners}
          onSelect={select}
        />
        <Inspector action={selectedAction} disabled={saving} onChoose={choose} onHighlight={highlight} />
      </main>
      <StatusBar state={state} actions={actions} />
    </div>
  );
}

function StatusBar({ state, actions }: { state: PanelState; actions: Action[] }) {
  const settings = new Set(actions.flatMap((action) => action.settings.map((setting) => setting.name))).size;
  const toCheck = state.checks.length;
  return (
    <footer class="flex h-7 shrink-0 items-center gap-3 border-t border-line bg-panel px-4 text-[11.5px] whitespace-nowrap text-faint">
      <span>
        {plural(state.steps.length, 'step')} · {plural(actions.length, 'action')} · {plural(settings, 'setting')}
      </span>
      {toCheck > 0 && (
        <span class="flex items-center gap-1 text-warn">
          <Icon name="warn" size={12} />
          {toCheck} to check
        </span>
      )}
      <span class="ml-auto hidden items-center gap-3 md:flex">
        <Hint keys="↑ ↓">steps</Hint>
        <Hint keys="Ctrl Z">undo</Hint>
        <Hint keys="Ctrl S">save</Hint>
      </span>
      {state.target && (
        <span class="min-w-0 truncate max-md:ml-auto" title={state.kept ? 'The flow is there already: it is kept, and the recording is saved beside it.' : undefined}>
          {state.kept ? 'Saves beside ' : 'Saves to '}
          <span dir="ltr" class="font-mono text-muted">{state.target}</span>
        </span>
      )}
    </footer>
  );
}

function Hint({ keys, children }: { keys: string; children: string }) {
  return (
    <span class="flex items-center gap-1.5">
      <kbd class="rounded border border-line bg-canvas px-1 font-sans text-[10.5px] leading-4 text-muted">{keys}</kbd>
      {children}
    </span>
  );
}
