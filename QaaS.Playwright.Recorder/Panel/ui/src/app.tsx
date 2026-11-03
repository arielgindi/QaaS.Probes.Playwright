import { useEffect, useMemo, useRef, useState } from 'preact/hooks';
import { flash, highlight, send, subscribe } from './bridge';
import { CodeView } from './components/CodeView';
import { ElementsView } from './components/Elements';
import { Icon } from './components/icons';
import { ElementInspector, Inspector } from './components/Inspector';
import { Lightbox, TestBar } from './components/TestRun';
import { Timeline } from './components/Timeline';
import { TopBar, type View } from './components/TopBar';
import { useElements } from './elements';
import { isGoing, numberOf, useSlowMotion } from './test';
import { cls, plural, subjectOf, verbOf } from './text';
import { useTheme } from './theme';
import type { Action, PanelState, Rerecord } from './types';

const waiting: PanelState = { flow: '', target: '', status: 'recording', code: '', yaml: '', steps: [], checks: [] };

export function App() {
  const [state, setState] = useState<PanelState>(waiting);
  const [selected, setSelected] = useState<string | null>(null);
  const [follow, setFollow] = useState(true);
  const [hovered, setHovered] = useState<string | null>(null);
  const [view, setView] = useState<View>('code');
  const [dismissed, setDismissed] = useState<string | null>(null);
  const [theme, toggleTheme] = useTheme();
  const [slow, toggleSlow] = useSlowMotion();
  const [enlarged, setEnlarged] = useState<{ src: string; caption: string } | null>(null);
  const [toast, setToast] = useState<string | null>(null);
  // Inspect mode in the app shows the Elements view; a click there picks its element, and the keys go on here.
  const [elements, elementActions] = useElements(view === 'elements', ({ picked, on }) => {
    if (picked !== undefined) pick(picked, false, true);
    else if (on) setView('elements');
  });
  const [checkKind, setCheckKind] = useState<string | undefined>(undefined);
  const primary = useRef<(() => void) | null>(null);
  const pickTimer = useRef(0);

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
    setToast(null);
  };
  // A deleted action's card goes, and the one after it is selected; Undo brings it back.
  const remove = (id: string) => {
    if (saving) return;
    const at = actions.findIndex((action) => action.id === id);
    const action = actions[at];
    send({ type: 'delete', action: id });
    if (!action) return;
    setToast(action.kind === 'expect' ? 'Deleted the check' : `Deleted ${verbOf(action).toLowerCase()} “${subjectOf(action)}”`);
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

  // Keys: ↑ and ↓ move through the steps, Delete deletes the selected one, Ctrl+Z undoes the last change, Ctrl+S saves,
  // F8 pauses or resumes, F6 checks something in the app, F7 tries things there; Ctrl+Enter or F5 tests the flow,
  // Ctrl+Shift+Enter runs it to the selected step, Esc or Shift+F5 stops the test. While a part is recorded again, F2 is
  // done with it, and Esc keeps it as it was.
  const keys = useRef({ actions, selected, select, undo, save, pause, runTest, stopTest, testing, remove, check, toggleTry, partDone, partCancel, part, inspect });
  keys.current = { actions, selected, select, undo, save, pause, runTest, stopTest, testing, remove, check, toggleTry, partDone, partCancel, part, inspect };
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
      if (event.target instanceof Element && event.target.closest('input, textarea, [data-own-keys]')) return;
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
      if (event.key === 'Escape' && (current.part?.phase === 'recording' || current.part?.phase === 'failed')) {
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
  useEffect(() => {
    if (!toast) return;
    const timer = window.setTimeout(() => setToast(null), 6000);
    return () => window.clearTimeout(timer);
  }, [toast]);

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
      />
      <Banner state={state} canTest={canTest} onDone={partDone} onCancel={partCancel} onTest={() => runTest()} />
      {test && (
        <TestBar
          test={test}
          actions={actions}
          onAgain={() => runTest(test.upTo && actions.some((action) => action.id === test.upTo) ? test.upTo : undefined)}
          onStop={stopTest}
          onClear={() => send({ type: 'clearTest' })}
          onShow={(id) => {
            setSelected(id);
            setFollow(false);
          }}
        />
      )}
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
      </main>
      <StatusBar state={state} actions={actions} />
      {toast && (
        <div role="status" class="pointer-events-none fixed inset-x-0 bottom-10 z-40 flex justify-center px-4">
          <div class="pointer-events-auto flex animate-step-in items-center gap-3 rounded-xl border border-line-strong bg-raised py-1.5 pr-1.5 pl-3.5 text-[12.5px] text-fg shadow-[0_10px_30px_rgb(0_0_0/0.28)]">
            <Icon name="trash" size={13} class="shrink-0 text-muted" />
            <span dir="auto" class="max-w-[48ch] truncate">{toast}</span>
            <button type="button" onClick={undo} class="inline-flex h-7 items-center gap-1.5 rounded-lg bg-accent/14 px-2.5 font-medium text-accent-fg transition hover:bg-accent/22">
              <Icon name="undo" size={13} />
              Undo
            </button>
          </div>
        </div>
      )}
      {enlarged && <Lightbox src={enlarged.src} caption={enlarged.caption} onClose={() => setEnlarged(null)} />}
    </div>
  );
}

// While paused, logging in or replaying the recording continued, nothing is recorded: the panel says so, and why.
// Resume is in the top bar. In check mode, it says what a click in the app does. While a part is recorded again, it
// says how far that got, and how to end it; while trying, that nothing is recorded.
function Banner({ state, canTest, onDone, onCancel, onTest }: { state: PanelState; canTest: boolean; onDone(): void; onCancel(): void; onTest(): void }) {
  if (state.rerecord && !(state.rerecord.phase === 'done' && state.status === 'trying'))
    return <PartBanner part={state.rerecord} actions={state.steps.flatMap((step) => step.actions)} canTest={canTest} onDone={onDone} onCancel={onCancel} onTest={onTest} />;
  if (state.status === 'trying')
    return (
      <div role="status" class="flex shrink-0 items-center gap-2.5 border-b border-info/25 bg-info/8 px-4 py-2 text-[12.5px] leading-snug text-info">
        <Icon name="flask" size={14} class="shrink-0" />
        <span class="min-w-0 flex-1">
          <span class="font-medium">Trying: nothing is recorded.</span>
          <span class="text-info/85"> Each action shows under the steps as the recorder would write it; Add to flow keeps one. Try (F7) again goes back to recording.</span>
        </span>
        <button type="button" onClick={() => send({ type: 'try', on: false })} class="inline-flex h-7 shrink-0 items-center gap-1.5 rounded-md border border-info/35 px-2 text-[11.5px] font-medium text-info transition hover:bg-info/10">
          Stop trying
          <kbd class="rounded border border-info/30 px-1 font-sans text-[10.5px] leading-4">F7</kbd>
        </button>
      </div>
    );
  if (state.continued?.banner)
    return (
      <div role="status" class="flex shrink-0 items-center gap-2.5 border-b border-info/25 bg-info/8 px-4 py-2 text-[12.5px] leading-snug text-info">
        <Icon name="play" size={14} class="shrink-0" />
        <span class="min-w-0 flex-1 font-medium">{state.continued.banner}</span>
      </div>
    );
  if (state.checking && state.status === 'recording')
    return (
      <div role="status" class="flex shrink-0 items-center gap-2.5 border-b border-ok/25 bg-ok/8 px-4 py-2 text-[12.5px] leading-snug text-ok">
        <Icon name="checkCircle" size={14} class="shrink-0" />
        <span class="min-w-0 flex-1">
          <span class="font-medium">Check mode.</span>
          <span class="text-ok/85"> Click what the flow should check in the app, then choose the check; Enter adds it. Nothing is clicked there. Esc leaves.</span>
        </span>
      </div>
    );
  if (state.status === 'login') {
    const progress = state.loginProgress;
    return (
      <div role="status" class="flex shrink-0 items-center gap-2.5 border-b border-info/25 bg-info/8 px-4 py-2 text-[12.5px] leading-snug text-info">
        <span class="size-3.5 shrink-0 animate-spin-slow rounded-full border-[1.5px] border-info border-t-transparent" />
        <span class="min-w-0 flex-1">
          <span class="font-medium">{progress?.text ?? `Logging in with ${state.login}…`}</span>
          {progress?.last && <span class="text-info/80"> Last: {progress.last}.</span>}
          <span class="text-info/80">
            {progress?.slow ? ' Taking long? Log in by hand.' : ' Nothing it does is recorded; the recording starts on the page it leaves.'}
          </span>
        </span>
        <button
          type="button"
          onClick={() => send({ type: 'loginByHand' })}
          title="Stop the login flow, and log in in the app yourself (Esc there)"
          class="inline-flex h-7 shrink-0 items-center rounded-md border border-info/35 px-2 text-[11.5px] font-medium text-info transition hover:bg-info/10"
        >
          Log in by hand
        </button>
      </div>
    );
  }
  if (state.status !== 'paused') return null;
  return (
    <div role="status" class="flex shrink-0 items-start gap-2.5 border-b border-warn/25 bg-warn/9 px-4 py-2 text-[12.5px] leading-snug text-warn">
      <Icon name={state.notice ? 'warn' : 'pause'} size={14} class="mt-[2px] shrink-0" />
      <span class="min-w-0 flex-1">
        {state.notice ? (
          <>
            <span class="font-medium">{state.notice}</span>
            <span class="text-warn/85"> Log in by hand in the app, then Resume (F8) to start recording.</span>
          </>
        ) : (
          <>
            <span class="font-medium">Recording paused.</span>
            <span class="text-warn/85"> What you do in the app is not recorded. Resume (F8) goes on from the page the app is on.</span>
          </>
        )}
      </span>
    </div>
  );
}

// A part recorded again: the replay up to it, the replay failed, the part recording, or done.
function PartBanner({ part, actions, canTest, onDone, onCancel, onTest }: { part: Rerecord; actions: Action[]; canTest: boolean; onDone(): void; onCancel(): void; onTest(): void }) {
  const what = part.after ? `Recording after ${part.part}` : `Re-recording ${part.part}`;
  const button = 'inline-flex h-7 shrink-0 items-center gap-1.5 rounded-md border px-2 text-[11.5px] font-medium transition';
  if (part.phase === 'replaying')
    return (
      <div role="status" aria-label="Re-recording" class="flex shrink-0 items-center gap-2.5 border-b border-info/25 bg-info/8 px-4 py-2 text-[12.5px] leading-snug text-info">
        <span class="size-3.5 shrink-0 animate-spin-slow rounded-full border-[1.5px] border-info border-t-transparent" />
        <span class="min-w-0 flex-1">
          <span class="font-medium">Replaying the flow up to {part.after ? 'the end of ' : ''}{part.part}…</span>
          <span class="text-info/85"> In the recorded window, from its start, as a test runs it; nothing is recorded.</span>
        </span>
        <button type="button" onClick={onCancel} class={cls(button, 'border-info/35 text-info hover:bg-info/10')}>
          Cancel
          <kbd class="rounded border border-info/30 px-1 font-sans text-[10.5px] leading-4">Esc</kbd>
        </button>
      </div>
    );
  if (part.phase === 'failed') {
    const at = numberOf(actions, part.failedAt);
    return (
      <div role="alert" aria-label="Re-recording" class="flex shrink-0 items-start gap-2.5 border-b border-bad/30 bg-bad/[0.07] px-4 py-2 text-[12.5px] leading-snug text-bad">
        <Icon name="warn" size={14} class="mt-[2px] shrink-0" />
        <span class="min-w-0 flex-1">
          <span class="font-medium">The replay did not get to {part.part}.</span>
          <span class="text-bad/85">
            {part.problem ? ` ${part.problem}` : at ? ` It failed at step ${at}: see why on its card. The app is where it stopped, so you can record again from there.` : ' It failed before the first step.'}
          </span>
        </span>
        {at > 0 && (
          <button type="button" onClick={() => send({ type: 'rerecordFrom' })} class={cls(button, 'border-bad/40 bg-bad/10 text-bad hover:bg-bad/16')}>
            <Icon name="rerecord" size={12} />
            Re-record from step {at}
          </button>
        )}
        <button type="button" onClick={onCancel} class={cls(button, 'border-transparent text-bad/85 hover:bg-bad/10')}>
          Cancel
        </button>
      </div>
    );
  }
  if (part.phase === 'recording')
    return (
      <div role="status" aria-label="Re-recording" class="flex shrink-0 items-start gap-2.5 border-b border-rec/30 bg-rec/[0.07] px-4 py-2 text-[12.5px] leading-snug text-fg">
        <span class="mt-[5px] size-2 shrink-0 animate-rec rounded-full bg-rec" />
        <span class="min-w-0 flex-1">
          <span class="font-medium">
            {what}: {part.after ? 'record what goes there' : 'do it again'}, then press Done (or Esc to keep {part.after ? 'the flow' : 'it'} as it was).
          </span>
          <span class="text-muted">
            {part.loop && !part.after ? ' It is a loop: its repeats are recorded again together.' : ''}
            {part.fresh.length > 0 ? ` ${plural(part.fresh.length, 'action')} so far; the steps after it stay.` : ' The steps after it stay.'}
          </span>
        </span>
        <button type="button" onClick={onDone} class={cls(button, 'border-rec/40 bg-rec text-white hover:brightness-110')}>
          <Icon name="tick" size={12} class="stroke-[2.4]" />
          Done
          <kbd class="rounded bg-white/20 px-1 font-sans text-[10.5px] leading-4">F2</kbd>
        </button>
        <button type="button" onClick={onCancel} class={cls(button, 'border-line-strong text-muted hover:bg-raised hover:text-fg')}>
          Keep it as it was
          <kbd class="rounded border border-line px-1 font-sans text-[10.5px] leading-4 text-faint">Esc</kbd>
        </button>
      </div>
    );
  return (
    <div role="status" aria-label="Re-recording" class="flex shrink-0 items-center gap-2.5 border-b border-ok/25 bg-ok/8 px-4 py-2 text-[12.5px] leading-snug text-ok">
      <Icon name="checkCircle" size={14} class="shrink-0" />
      <span class="min-w-0 flex-1">
        <span class="font-medium">{part.after ? `Recorded after ${part.part}.` : `Re-recorded ${part.part}.`}</span>
        <span class="text-ok/85">
          {' '}Test the flow to see it run as it will be saved; Ctrl+Z brings it back as it was.
          {part.paused ? ' Paused: the app is where that part left it, not at the end of the flow.' : ''}
        </span>
      </span>
      <button type="button" disabled={!canTest} onClick={onTest} class={cls(button, 'border-ok/40 bg-ok/10 text-ok hover:bg-ok/16 disabled:opacity-40')}>
        <Icon name="play" size={11} fill="currentColor" />
        Test
      </button>
      <button type="button" aria-label="Dismiss" onClick={() => send({ type: 'rerecordDismiss' })} class="rounded p-0.5 transition hover:bg-ok/15">
        <Icon name="cross" size={13} />
      </button>
    </div>
  );
}

function StatusBar({ state, actions }: { state: PanelState; actions: Action[] }) {
  const settings = new Set(actions.flatMap((action) => action.settings.map((setting) => setting.name))).size;
  const toCheck = state.checks.length;
  return (
    <footer class="flex h-7 shrink-0 items-center gap-3 border-t border-line bg-panel px-4 text-[11.5px] whitespace-nowrap text-faint">
      <span class="tabular-nums">
        {plural(state.steps.length, 'step')} · {plural(actions.length, 'action')} · {plural(settings, 'setting')}
      </span>
      {toCheck > 0 && (
        <span class="flex items-center gap-1 text-warn">
          <Icon name="warn" size={12} />
          {toCheck} to check
        </span>
      )}
      {state.login && (
        <span class="flex min-w-0 items-center gap-1 max-[860px]:hidden" title={`Recorded after ${state.login}: run it with SetupFlows: [${state.login}]`}>
          <Icon name="key" size={12} />
          after <span class="font-mono text-muted">{state.login}</span>
        </span>
      )}
      <span class="ml-auto hidden items-center gap-3 lg:flex">
        <Hint keys="↑ ↓">steps</Hint>
        <Hint keys="Ctrl ↵">test</Hint>
        <Hint keys="F6">check</Hint>
        <Hint keys="F8">pause</Hint>
        <Hint keys="F7">try</Hint>
        <Hint keys="Ctrl Z">undo</Hint>
        <Hint keys="Ctrl S">save</Hint>
      </span>
      {state.target && (
        <span class="min-w-0 truncate max-lg:ml-auto" title={state.kept ? 'The flow is there already: it is kept, and the recording is saved beside it.' : undefined}>
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
