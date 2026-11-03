import type { ComponentChildren } from 'preact';
import { useEffect, useMemo, useRef, useState } from 'preact/hooks';
import { flash, highlight, send, subscribe } from './bridge';
import { CodeView } from './components/CodeView';
import { ElementsView } from './components/Elements';
import { Icon } from './components/icons';
import { ElementInspector, Inspector } from './components/Inspector';
import { type Command, KeysSheet, Palette, Splitter, ToastView } from './components/Overlays';
import { StatusStrip } from './components/StatusStrip';
import { Lightbox } from './components/TestRun';
import { Timeline } from './components/Timeline';
import { TopBar, type View } from './components/TopBar';
import { Kbd } from './components/ui';
import { useElements } from './elements';
import { usePref } from './prefs';
import { isGoing, useSlowMotion } from './test';
import { plural, subjectOf, verbOf } from './text';
import { useTheme } from './theme';
import { toast } from './toast';
import type { Action, PanelState } from './types';

const waiting: PanelState = { flow: '', target: '', status: 'recording', code: '', yaml: '', steps: [], checks: [] };

// The panes' sizes the person can drag, with their defaults and limits.
const STEPS = { fallback: 300, min: 220, max: 520 };
const INSPECTOR = { fallback: 360, min: 280, max: 600 };
const CODE_SHARE = { fallback: 56, min: 20, max: 80 };

export function App() {
  const [state, setState] = useState<PanelState>(waiting);
  const [selected, setSelected] = useState<string | null>(null);
  const [follow, setFollow] = useState(true);
  const [hovered, setHovered] = useState<string | null>(null);
  const [view, setView] = useState<View>('code');
  const [theme, toggleTheme] = useTheme();
  const [slow, toggleSlow] = useSlowMotion();
  const [enlarged, setEnlarged] = useState<{ src: string; caption: string } | null>(null);
  const [overlay, setOverlay] = useState<'palette' | 'keys' | null>(null);
  const [seenPalette, setSeenPalette] = usePref('seenPalette', '');
  // Inspect mode in the app shows the Elements view; a click there picks its element, and the keys go on here.
  const [elements, elementActions] = useElements(view === 'elements', ({ picked, on }) => {
    if (picked !== undefined) pick(picked, false, true);
    else if (on) setView('elements');
  });
  const [checkKind, setCheckKind] = useState<string | undefined>(undefined);
  const primary = useRef<(() => void) | null>(null);
  const pickTimer = useRef(0);
  const shownError = useRef<string | null>(null);

  useEffect(() => {
    const stop = subscribe(setState);
    send({ type: 'ready' });
    return stop;
  }, []);

  // Why a command did not work, once, as a toast that says it out loud.
  useEffect(() => {
    if (state.error && state.error !== shownError.current) toast({ text: state.error, alert: true });
    shownError.current = state.error ?? null;
  }, [state]);

  const actions = useMemo(() => state.steps.flatMap((step) => step.actions), [state]);
  const owners = useMemo(() => {
    const byLine = new Map<number, string>();
    for (const action of actions) if (action.lines) for (let line = action.lines.first; line <= action.lines.last; line++) byLine.set(line, action.id);
    return byLine;
  }, [actions]);
  const saving = state.status === 'saving';
  // The newest action: the last of the flow, or the last recorded in the part being recorded again.
  const fresh = state.rerecord?.phase === 'recording' ? state.rerecord.fresh : [];
  const last = fresh.at(-1) ?? actions.at(-1)?.id ?? null;
  const test = state.test;
  const testing = isGoing(test);
  const canTest = actions.length > 0 && !saving && !testing && state.status !== 'login' && state.status !== 'testing';
  const part = state.rerecord;
  // A step is recorded again from the flow's start: not while anything runs, nor while another part is.
  const canRerecord = canTest && (!part || part.phase === 'done');

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

  // A run that failed at a step selects it, so its lines show in the code and its element in the inspector.
  const failedAction = test?.state === 'failed' && !test.stale ? test.failure?.action : undefined;
  useEffect(() => {
    if (!failedAction) return;
    setSelected(failedAction);
    setFollow(false);
  }, [test?.run, failedAction]);

  // F4 in the app shows its frozen elements here.
  const frozenAt = elements.source === 'frozen' ? elements.at : undefined;
  useEffect(() => {
    if (frozenAt) setView('elements');
  }, [frozenAt]);

  const select = (id: string) => {
    setSelected(id);
    setFollow(id === last);
  };
  const hover = (id: string | null) => {
    setHovered(id);
    highlight(id === null ? null : { action: id });
  };
  const undo = () => {
    if (state.undo && !saving) send({ type: 'undo' });
  };
  // A deleted action's card goes, and the one after it is selected; Undo brings it back.
  const remove = (id: string) => {
    if (saving) return;
    const at = actions.findIndex((action) => action.id === id);
    const action = actions[at];
    send({ type: 'delete', action: id });
    if (!action) {
      toast({ text: 'Deleted the check', icon: 'trash', action: { label: 'Undo', icon: 'undo', run: () => send({ type: 'undo' }) } });
      return;
    }
    toast({ text: `Deleted ${verbOf(action).toLowerCase()} “${subjectOf(action)}”`, icon: 'trash', action: { label: 'Undo', icon: 'undo', run: () => send({ type: 'undo' }) } });
    if (id === selected) {
      const next = actions[at + 1] ?? actions[at - 1];
      setSelected(next?.id ?? null);
      setFollow(!actions[at + 1]);
    }
  };
  const check = () => {
    if (!saving && (state.status === 'recording' || state.checking)) send({ type: 'checkMode', on: !state.checking });
  };
  const save = () => {
    if (actions.length > 0 && !saving) send({ type: 'save' });
  };
  const pause = () => {
    if (state.status === 'recording' || state.status === 'paused') send({ type: 'pause' });
  };
  const runTest = (upTo?: string) => {
    if (canTest) send({ type: 'test', action: upTo, slow });
  };
  const stopTest = () => {
    if (testing) send({ type: 'stopTest' });
  };
  const toggleTry = () => {
    if (!saving && ['recording', 'paused', 'trying'].includes(state.status)) send({ type: 'try' });
  };
  const partDone = () => {
    if (part?.phase === 'recording') send({ type: 'rerecordDone' });
  };
  const partCancel = () => {
    if (part && part.phase !== 'done') send({ type: 'rerecordCancel' });
  };
  const choose = (action: Action, candidate: number | null) => send({ type: 'choose', action: action.id, candidate });
  // The element picked in the Elements view, with the step selected, which "Use for" changes; the keys moving through
  // the tree pick once they rest.
  const pick = (node: number, soon = false, focus = false) => {
    window.clearTimeout(pickTimer.current);
    const frozen = elements.source === 'frozen';
    const go = () => send({ type: 'pick', node, frozen, action: selected ?? undefined, focus });
    if (soon) pickTimer.current = window.setTimeout(go, 140);
    else go();
  };
  const pickedNode = state.element?.node;
  useEffect(() => setCheckKind(undefined), [pickedNode]);
  useEffect(() => {
    if (view === 'elements' && pickedNode !== undefined) send({ type: 'elements', op: 'preview', action: selected ?? undefined, check: checkKind });
  }, [selected, checkKind]);
  const inspect = () => {
    setView('elements');
    elementActions.inspect();
  };
  const openPalette = () => {
    setOverlay('palette');
    if (!seenPalette) setSeenPalette('1');
  };

  // Every command, for the palette: what it is called, its key, and why it cannot run now.
  const recordingOrPaused = state.status === 'recording' || state.status === 'paused';
  const commands: Command[] = [
    { id: 'test', group: 'Test', icon: 'play', label: 'Test the flow', keys: 'Ctrl ↵', off: !canTest && 'Nothing to test now', run: () => runTest() },
    { id: 'runTo', group: 'Test', icon: 'runTo', label: 'Run to the selected step', keys: 'Ctrl Shift ↵', off: (!canTest || !selected) && 'Select a step, with nothing running', run: () => runTest(selected ?? undefined) },
    { id: 'stop', group: 'Test', icon: 'stop', label: 'Stop the test', keys: 'Esc', off: !testing && 'No test runs', run: stopTest },
    { id: 'slow', group: 'Test', icon: 'gauge', label: slow ? 'Test at full speed' : 'Test in slow motion', run: toggleSlow },
    { id: 'pause', group: 'Record', icon: state.status === 'paused' ? 'play' : 'pause', label: state.status === 'paused' ? 'Resume recording' : 'Pause recording', keys: 'F8', off: !recordingOrPaused && 'Not while it is busy', run: pause },
    { id: 'check', group: 'Record', icon: 'checkCircle', label: state.checking ? 'Leave check mode' : 'Check something in the app', keys: 'F6', off: (saving || (state.status !== 'recording' && !state.checking)) && 'Only while recording', run: check },
    { id: 'try', group: 'Record', icon: 'flask', label: state.status === 'trying' ? 'Stop trying' : 'Try things without recording them', keys: 'F7', off: !(recordingOrPaused || state.status === 'trying') && 'Not while it is busy', run: toggleTry },
    { id: 'inspect', group: 'Record', icon: 'inspect', label: elements.inspecting ? 'Stop picking in the app' : 'Pick an element in the app', keys: 'Ctrl Shift C', run: inspect },
    { id: 'done', group: 'Record', icon: 'tick', label: 'Done re-recording', keys: 'F2', off: part?.phase !== 'recording' && 'No part is being re-recorded', run: partDone },
    { id: 'keep', group: 'Record', icon: 'restore', label: 'Keep the part as it was', keys: 'Esc', off: !(part && part.phase !== 'done') && 'No part is being re-recorded', run: partCancel },
    { id: 'undo', group: 'Edit', icon: 'undo', label: state.undo ? `Undo: ${state.undo}` : 'Undo', keys: 'Ctrl Z', off: (!state.undo || saving) && 'Nothing to undo', run: undo },
    { id: 'delete', group: 'Edit', icon: 'trash', label: 'Delete the selected step', keys: 'Delete', off: (!selected || saving) && 'Select a step first', run: () => selected && remove(selected) },
    { id: 'autoChecks', group: 'Edit', icon: 'checkCircle', label: state.autoChecks === false ? 'Turn auto checks on' : 'Turn auto checks off', off: saving && 'Saving', run: () => send({ type: 'autoChecks', on: state.autoChecks === false }) },
    { id: 'code', group: 'Show', icon: 'file', label: 'Show the code', run: () => setView('code') },
    { id: 'yaml', group: 'Show', icon: 'list', label: 'Show the YAML', run: () => setView('yaml') },
    { id: 'elements', group: 'Show', icon: 'elements', label: 'Show the page’s elements', run: () => setView('elements') },
    { id: 'theme', group: 'Show', icon: theme === 'dark' ? 'sun' : 'moon', label: theme === 'dark' ? 'Light theme' : 'Dark theme', run: toggleTheme },
    { id: 'keys', group: 'Help', icon: 'keyboard', label: 'Keyboard shortcuts', keys: '?', run: () => setOverlay('keys') },
    { id: 'save', group: 'Flow', icon: 'file', label: 'Save the flow and end the recording', keys: 'Ctrl S', off: (actions.length === 0 || saving) && 'Nothing recorded yet', run: save },
  ];

  // Keys: ↑ and ↓ move through the steps, Delete deletes the selected one, Ctrl+Z undoes the last change, Ctrl+S saves,
  // F8 pauses or resumes, F6 checks something in the app, F7 tries things there; Ctrl+Enter or F5 tests the flow,
  // Ctrl+Shift+Enter runs it to the selected step, Esc or Shift+F5 stops the test. While a part is recorded again, F2 is
  // done with it, and Esc keeps it as it was. Ctrl+K lists every command, ? every key.
  const keys = useRef({ actions, selected, select, undo, save, pause, runTest, stopTest, testing, remove, check, toggleTry, partDone, partCancel, part, inspect, openPalette });
  keys.current = { actions, selected, select, undo, save, pause, runTest, stopTest, testing, remove, check, toggleTry, partDone, partCancel, part, inspect, openPalette };
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
      if (command && key === 'k' && !event.shiftKey) {
        event.preventDefault();
        current.openPalette();
        return;
      }
      if (event.key === 'F8' && !event.repeat) {
        event.preventDefault();
        current.pause();
        return;
      }
      // Ctrl+Shift+C picks an element in the app, as in a browser's developer tools, which it would open.
      if (command && event.shiftKey && event.code === 'KeyC') {
        event.preventDefault();
        if (!event.repeat) current.inspect();
        return;
      }
      // F7 would turn on caret browsing.
      if (event.key === 'F7') {
        event.preventDefault();
        if (!event.repeat) current.toggleTry();
        return;
      }
      if (event.key === 'F2' && current.part?.phase === 'recording') {
        event.preventDefault();
        if (!event.repeat) current.partDone();
        return;
      }
      // F6 would move the focus to the address bar.
      if (event.key === 'F6') {
        event.preventDefault();
        if (!event.repeat) current.check();
        return;
      }
      // F5 would reload the panel.
      if (event.key === 'F5') {
        event.preventDefault();
        if (event.repeat) return;
        if (event.shiftKey) current.stopTest();
        else current.runTest();
        return;
      }
      if (event.target instanceof Element && event.target.closest('input, textarea, select, [data-own-keys]')) return;
      if (event.key === '?' && !command) {
        event.preventDefault();
        setOverlay('keys');
        return;
      }
      if (command && event.key === 'Enter' && !event.repeat) {
        event.preventDefault();
        current.runTest(event.shiftKey ? (current.selected ?? undefined) : undefined);
        return;
      }
      if (event.key === 'Escape' && current.testing) {
        event.preventDefault();
        current.stopTest();
        return;
      }
      if (event.key === 'Escape' && (current.part?.phase === 'recording' || current.part?.phase === 'failed' || current.part?.phase === 'replaying')) {
        event.preventDefault();
        current.partCancel();
        return;
      }
      if (command && key === 'z' && !event.shiftKey) {
        event.preventDefault();
        current.undo();
      } else if ((event.key === 'Delete' || event.key === 'Backspace') && !command && current.selected) {
        event.preventDefault();
        current.remove(current.selected);
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
  const file = state.target.slice(Math.max(state.target.lastIndexOf('/'), state.target.lastIndexOf('\\')) + 1);
  const enlarge = (src: string, caption: string) => setEnlarged({ src, caption });

  return (
    <div class="flex h-full flex-col">
      <TopBar
        flow={state.flow}
        stamp={state}
        status={state.status}
        canSave={actions.length > 0}
        undo={state.undo}
        checking={!!state.checking}
        onTry={toggleTry}
        view={view}
        theme={theme}
        testing={testing}
        canTest={canTest}
        slow={slow}
        onTest={() => runTest()}
        onStopTest={stopTest}
        onSlow={toggleSlow}
        onRename={(name) => send({ type: 'rename', name })}
        onUndo={undo}
        onCheck={check}
        onSave={save}
        onPause={pause}
        onView={setView}
        onTheme={toggleTheme}
        onPalette={openPalette}
        onKeys={() => setOverlay('keys')}
      />
      <StatusStrip
        state={state}
        actions={actions}
        inspecting={elements.inspecting}
        canTest={canTest}
        onPause={pause}
        onTry={toggleTry}
        onCheck={check}
        onInspect={() => elementActions.inspect(false)}
        onDone={partDone}
        onCancelPart={partCancel}
        onTest={() => runTest()}
        onAgain={() => runTest(test?.upTo && actions.some((action) => action.id === test.upTo) ? test.upTo : undefined)}
        onStopTest={stopTest}
        onClearTest={() => send({ type: 'clearTest' })}
        onShowStep={(id) => {
          setSelected(id);
          setFollow(false);
        }}
      />
      <Panes>
        <Timeline
          steps={state.steps}
          count={actions.length}
          notes={notes}
          selected={selected}
          status={state.status}
          test={test}
          continued={state.continued}
          file={file}
          canRun={canTest}
          canEdit={!saving}
          autoChecks={state.autoChecks !== false}
          onSelect={select}
          onHover={hover}
          onRunTo={runTest}
          onEnlarge={enlarge}
          onDelete={remove}
          onRenameStep={(action, name) => send({ type: 'renameStep', action, name })}
          onDropCheck={(action, key, drop) => send({ type: 'dropCheck', action, key, drop })}
          onApplySuggestion={(key) => {
            if (saving) return;
            send({ type: 'applySuggestion', key });
            toast({ text: 'Left out what the suggestion said', icon: 'trash', action: { label: 'Undo', icon: 'undo', run: () => send({ type: 'undo' }) } });
          }}
          onAutoChecks={(on) => send({ type: 'autoChecks', on })}
          rerecord={part}
          tried={state.tried}
          login={state.login}
          canRerecord={canRerecord}
          onRerecord={(action, step, after, label) => send({ type: 'rerecord', action, step, after, label })}
          onAddTried={(action) => send({ type: 'addTried', action })}
        />
        {view === 'elements' ? (
          <ElementsView model={elements} actions={elementActions} picked={state.element?.node} onPick={pick} onPrimary={() => primary.current?.()} />
        ) : (
          <CodeView
            view={view}
            code={state.code}
            yaml={state.yaml}
            target={state.target}
            checks={state.checks}
            selected={selectedAction}
            hovered={hoveredAction}
            owners={owners}
            actions={actions}
            test={test}
            onSelect={select}
          />
        )}
        {view === 'elements' ? (
          <ElementInspector
            element={state.element}
            step={selectedAction}
            where={part?.phase === 'recording' ? `${part.after ? 'after' : 'into'} ${part.part}` : 'at the end'}
            disabled={saving}
            undo={state.undo}
            primary={primary}
            onChoose={(candidate) => send({ type: 'choosePicked', candidate })}
            onHighlight={(candidate) => highlight(candidate === null ? null : { candidate })}
            onUse={(action) => send({ type: 'useForStep', action: action.id })}
            onAdd={(kind, value) =>
              send(kind === 'click' ? { type: 'addClick' } : kind === 'fill' ? { type: 'addFill', value } : { type: 'addCheck', check: value })
            }
            onCheckKind={setCheckKind}
            onUndo={undo}
          />
        ) : (
          <Inspector
            action={selectedAction}
            disabled={saving}
            test={selectedAction && test?.steps[selectedAction.id]}
            canRun={canTest}
            onChoose={choose}
            onHighlight={(action, candidate) => highlight(action === null ? null : { action, candidate })}
            onRunTo={runTest}
            onEdit={(action, setting, change) => send({ type: 'editValue', action: action.id, role: setting.role, ...change })}
          />
        )}
      </Panes>
      <StatusBar state={state} actions={actions} hint={!seenPalette} onPalette={openPalette} />
      <ToastView />
      {overlay === 'palette' && <Palette commands={commands} onClose={() => setOverlay(null)} />}
      {overlay === 'keys' && <KeysSheet onClose={() => setOverlay(null)} />}
      {enlarged && <Lightbox src={enlarged.src} caption={enlarged.caption} onClose={() => setEnlarged(null)} />}
    </div>
  );
}

// Which layout the panel's width gives: three columns, the code over the inspector beside the steps, or all stacked.
function useLayout(): 'wide' | 'medium' | 'narrow' {
  const of = (): 'wide' | 'medium' | 'narrow' => (matchMedia('(min-width: 1180px)').matches ? 'wide' : matchMedia('(min-width: 720px)').matches ? 'medium' : 'narrow');
  const [layout, setLayout] = useState(of);
  useEffect(() => {
    const follow = () => setLayout(of());
    addEventListener('resize', follow);
    return () => removeEventListener('resize', follow);
  }, []);
  return layout;
}

// The three panes, with handles between them that the person drags; the sizes are remembered.
function Panes({ children }: { children: ComponentChildren }) {
  const layout = useLayout();
  const main = useRef<HTMLElement>(null);
  const [steps, setSteps] = usePref('stepsWidth', '');
  const [inspector, setInspector] = usePref('inspectorWidth', '');
  const [share, setShare] = usePref('codeShare', '');
  const [dragging, setDragging] = useState<{ steps?: number; inspector?: number; share?: number }>({});
  const number = (text: string, fallback: number) => (Number.isFinite(Number.parseFloat(text)) ? Number.parseFloat(text) : fallback);
  const stepsWidth = dragging.steps ?? number(steps, STEPS.fallback);
  const inspectorWidth = dragging.inspector ?? number(inspector, INSPECTOR.fallback);
  const codeShare = dragging.share ?? number(share, CODE_SHARE.fallback);
  const height = () => main.current?.clientHeight ?? 600;
  const style = {
    '--steps-width': `${layout === 'wide' ? stepsWidth : Math.min(stepsWidth, 340)}px`,
    '--inspector-width': `${inspectorWidth}px`,
    '--code-share': `${codeShare}fr`,
    '--inspector-share': `${100 - codeShare}fr`,
  } as Record<string, string>;
  // A double-click (-1) goes back to the default.
  const done = (value: number, store: (text: string) => void) => {
    setDragging({});
    store(value < 0 ? '' : String(value));
  };
  const [stepsPane, codePane, inspectorPane] = children as ComponentChildren[];
  return (
    <main ref={main} class="layout min-h-0 flex-1" style={style}>
      {stepsPane}
      {layout !== 'narrow' && (
        <Splitter
          class="area-split-left"
          label="Resize the steps"
          orientation="vertical"
          value={stepsWidth}
          min={STEPS.min}
          max={layout === 'wide' ? STEPS.max : 340}
          grows={1}
          onChange={(value) => setDragging({ steps: value })}
          onDone={(value) => done(value, setSteps)}
        />
      )}
      {codePane}
      {layout === 'wide' && (
        <Splitter
          class="area-split-right"
          label="Resize the inspector"
          orientation="vertical"
          value={inspectorWidth}
          min={INSPECTOR.min}
          max={INSPECTOR.max}
          grows={-1}
          onChange={(value) => setDragging({ inspector: value })}
          onDone={(value) => done(value, setInspector)}
        />
      )}
      {layout === 'medium' && (
        <Splitter
          class="area-split-right"
          label="Resize the code and the inspector"
          orientation="horizontal"
          value={(codeShare / 100) * height()}
          min={(CODE_SHARE.min / 100) * height()}
          max={(CODE_SHARE.max / 100) * height()}
          grows={1}
          onChange={(value) => setDragging({ share: Math.round((value / height()) * 100) })}
          onDone={(value) => done(value < 0 ? -1 : Math.round((value / height()) * 100), setShare)}
        />
      )}
      {inspectorPane}
    </main>
  );
}

function StatusBar({ state, actions, hint, onPalette }: { state: PanelState; actions: Action[]; hint: boolean; onPalette(): void }) {
  const settings = new Set(actions.flatMap((action) => action.settings.map((setting) => setting.name))).size;
  const toCheck = state.checks.length;
  return (
    <footer class="flex h-7 shrink-0 items-center gap-3 overflow-hidden border-t border-line bg-panel px-3 text-meta whitespace-nowrap text-muted">
      <span class="shrink-0 tabular-nums">
        {plural(state.steps.length, 'step')} · {plural(actions.length, 'action')} · {plural(settings, 'setting')}
      </span>
      {toCheck > 0 && (
        <span class="flex shrink-0 items-center gap-1 text-warn">
          <Icon name="warn" size={12} />
          {toCheck} to check by hand
        </span>
      )}
      {state.login && (
        <span class="flex min-w-0 items-center gap-1 max-[860px]:hidden" title={`Recorded after ${state.login}: run it with SetupFlows: [${state.login}]`}>
          <Icon name="key" size={12} class="shrink-0" />
          <span class="truncate">
            after <span class="font-mono text-fg/85">{state.login}</span>
          </span>
        </span>
      )}
      {state.kept && (
        <span class="flex min-w-0 items-center gap-1 text-warn" title="The flow is there already: it is kept, and the recording is saved beside it, to merge by hand.">
          <Icon name="file" size={12} class="shrink-0" />
          <span class="truncate">saves beside the flow</span>
        </span>
      )}
      <button type="button" onClick={onPalette} class="ml-auto flex shrink-0 items-center gap-1.5 rounded-sm px-1 transition hover:text-fg">
        {hint && <span class="text-accent-fg max-[640px]:hidden">Every command and its key:</span>}
        <Kbd>Ctrl K</Kbd>
        <span class="max-[640px]:hidden">commands</span>
      </button>
    </footer>
  );
}
