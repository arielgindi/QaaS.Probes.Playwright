import type { ComponentChildren } from 'preact';
import { useEffect, useState } from 'preact/hooks';
import { send } from '../bridge';
import { duration, isGoing, isPreparing, numberOf, useNow } from '../test';
import { cls, plural, subjectOf, verbOf } from '../text';
import type { Action, PanelState, TestRun } from '../types';
import { Icon, type IconName } from './icons';
import { Button, ModeMark, type Tone, toneText } from './ui';

// The one place that says what the recorder is doing and how to leave it: recording, paused, logging in, testing,
// trying, re-recording, checking or inspecting, with the keys that end it. A test's result shows here too, or, while
// another mode is on, as a small chip at its end. It never grows or moves: a mode changes its words and colour only.

interface Props {
  state: PanelState;
  actions: Action[];
  /** The app is in inspect mode, for the Elements view. */
  inspecting: boolean;
  canTest: boolean;
  onPause(): void;
  onTry(): void;
  onCheck(): void;
  onInspect(): void;
  onDone(): void;
  onCancelPart(): void;
  onTest(): void;
  onAgain(): void;
  onStopTest(): void;
  onClearTest(): void;
  onShowStep(action: string): void;
}

interface Shown {
  /** Its accessible name, which says what it is about: "Test run", "Re-recording". */
  name: string;
  tone: Tone;
  label: string;
  message?: ComponentChildren;
  /** The whole message, for its tooltip, when it is cut. */
  full?: string;
  busy?: boolean;
  pulse?: boolean;
  icon?: IconName;
  time?: string;
  alert?: boolean;
  actions?: ComponentChildren;
  /** Tinted, for a mode the person should not forget they are in. */
  tinted?: boolean;
}

const tints: Partial<Record<Tone, string>> = {
  rec: 'bg-rec/[0.06]',
  warn: 'bg-warn/[0.07]',
  info: 'bg-info/[0.06]',
  accent: 'bg-accent/[0.07]',
  try: 'bg-try/[0.06]',
  ok: 'bg-ok/[0.06]',
  inspect: 'bg-inspect/[0.06]',
  bad: 'bg-bad/[0.07]',
};

const fills: Partial<Record<Tone, string>> = {
  accent: 'bg-accent-fg',
  info: 'bg-info',
  ok: 'bg-ok',
  bad: 'bg-bad',
  rec: 'bg-rec',
};

export function StatusStrip(props: Props) {
  const { state, actions } = props;
  const test = state.test;
  const going = isGoing(test);
  const now = useNow(going || state.rerecord?.phase === 'replaying', 100);
  const [details, setDetails] = useState(false);
  useEffect(() => setDetails(false), [test?.run]);

  const result = test && !going ? testResult(test, actions, props) : null;
  const mode = modeOf(props, now) ?? (test ? (going ? testProgress(test, actions, now, props) : result) : null) ?? recording(props);
  // A result the strip does not say itself goes at its end, as a chip.
  const chip = result && mode !== result ? result : null;
  const progress = test && (going || test.state === 'passed' || test.state === 'failed') && (mode.name === 'Test run' || going) ? progressOf(test, actions) : null;

  return (
    <>
      <div class={cls('relative flex h-10 shrink-0 items-center gap-3 border-b border-line pr-2 pl-4 transition-colors', mode.tinted ? tints[mode.tone] : 'bg-panel')}>
        <div role={mode.alert ? 'alert' : 'status'} aria-label={mode.name} aria-live="polite" class="flex min-w-0 flex-1 items-center gap-2.5">
          <span class={cls('flex shrink-0 items-center gap-2 text-body font-semibold', toneText[mode.tone])}>
            {mode.icon ? <ResultMark tone={mode.tone} icon={mode.icon} /> : <ModeMark tone={mode.tone} busy={mode.busy} pulse={mode.pulse} />}
            {mode.label}
          </span>
          {mode.message && (
            <span title={mode.full} class="min-w-0 truncate text-body text-muted">
              {mode.message}
            </span>
          )}
          {mode.time && <span class="shrink-0 font-mono text-meta text-muted tabular-nums">{mode.time}</span>}
        </div>
        <div class="flex shrink-0 items-center gap-1">
          {mode.actions}
          {mode.name === 'Test run' && test?.problemDetail && (
            <Button size="sm" icon="down" pressed={details} onClick={() => setDetails(!details)} title="What the build said">
              Details
            </Button>
          )}
          {chip && <ResultChip shown={chip} />}
        </div>
        {progress && (
          <span class="absolute inset-x-0 bottom-[-1px] h-0.5 overflow-hidden">
            {progress.indeterminate ? (
              <span class={cls('absolute inset-y-0 w-1/3 animate-indeterminate', fills[progress.tone])} />
            ) : (
              <span class={cls('block h-full transition-[width] duration-500 ease-out', fills[progress.tone])} style={{ width: `${progress.share * 100}%` }} />
            )}
          </span>
        )}
      </div>
      {details && test?.problemDetail && (
        <pre class="max-h-48 shrink-0 overflow-auto border-b border-line bg-canvas px-4 py-3 font-mono text-meta whitespace-pre-wrap text-muted">{test.problemDetail}</pre>
      )}
    </>
  );
}

// The mode the recorder is in, the most pressing first; null when it only records, or tests.
function modeOf(props: Props, now: number): Shown | null {
  const { state, actions } = props;
  const part = state.rerecord;
  // The mode is the recorder's state machine's, most pressing first; here it gets its buttons.
  const mode = state.mode;
  const going = isGoing(state.test);
  const leave = (label: string, keys: string, onClick: () => void, title?: string) => (
    <Button size="sm" variant="secondary" keys={keys} onClick={onClick} title={title}>
      {label}
    </Button>
  );
  switch (mode?.name) {
    case 'Saving':
      return { name: 'Saving', tone: 'accent', label: 'Saving', busy: true, message: `${state.flow}.cs and its settings…` };
    case 'Logging in': {
      const progress = state.loginProgress;
      const text = progress?.text ?? `Logging in with ${state.login ?? 'the login flow'}…`;
      const message = [text.replace(/^Logging in /, ''), progress?.last && `last: ${progress.last}`, progress?.slow && 'taking long?'].filter(Boolean).join(' · ');
      return {
        name: 'Logging in', tone: 'info', label: 'Logging in', busy: true, tinted: true, message, full: `${text} Nothing it does is recorded.`,
        actions: (
          <Button size="sm" variant={progress?.slow ? 'secondary' : 'ghost'} onClick={() => sendLoginByHand()} title="Stop the login flow, and log in in the app yourself (Esc there)">
            Log in by hand
          </Button>
        ),
      };
    }
    case 'Replaying':
      return { name: 'Replaying', tone: mode.tone as Tone, label: mode.label, busy: true, tinted: true, message: state.continued?.banner ?? mode.message };
    case 'Re-recording':
      if (part?.phase === 'replaying')
        return {
          name: 'Re-recording', tone: 'rec', label: part.after ? 'Record after' : 'Re-record', busy: true, tinted: true,
          message: `Replaying the flow up to ${part.after ? 'the end of ' : ''}${part.part}… nothing is recorded`,
          time: state.test && going ? `${(Math.max(0, now - state.test.started) / 1000).toFixed(1)} s` : undefined,
          actions: leave('Cancel', 'Esc', props.onCancelPart),
        };
      if (part?.phase === 'failed') {
        const at = numberOf(actions, part.failedAt);
        return {
          name: 'Re-recording', tone: 'bad', label: 'Replay failed', icon: 'cross', tinted: true, alert: true,
          message: part.problem ?? (at ? `It did not get to ${part.part}: step ${at} failed, see why on its card` : `It did not get to ${part.part}`),
          full: part.problem,
          actions: (
            <>
              {at > 0 && (
                <Button size="sm" variant="secondary" icon="rerecord" onClick={() => sendRerecordFrom()} title="The app is where the replay stopped: record again from there to the end of the part">
                  Re-record from step {at}
                </Button>
              )}
              <Button size="sm" onClick={props.onCancelPart}>
                Cancel
              </Button>
            </>
          ),
        };
      }
      if (part?.phase === 'done') {
        const paused = mode.label === 'Paused';
        return {
          name: 'Re-recording', tone: paused ? 'warn' : 'ok', label: paused ? 'Paused' : 'Re-recorded', icon: paused ? undefined : 'tick', tinted: paused,
          message: mode.message,
          actions: (
            <>
              <Button size="sm" variant="secondary" icon="play" disabled={!props.canTest} onClick={props.onTest} title="Run the flow as it will be saved">
                Test
              </Button>
              {paused && leave('Resume', 'F8', props.onPause, 'Record again from the page the app is on')}
              <Button size="sm" iconOnly icon="cross" label="Dismiss" onClick={() => sendDismissPart()} />
            </>
          ),
        };
      }
      if (going || !part) return null;
      return {
        name: 'Re-recording', tone: 'rec', label: part.after ? 'Recording after' : 'Re-recording', pulse: true, tinted: true,
        message: `${part.part}: ${part.after ? 'record what goes there' : 'do it again'}, then press Done${part.loop && !part.after ? ' · the whole loop' : ''}${part.fresh.length > 0 ? ` · ${plural(part.fresh.length, 'action')} so far` : ''}`,
        actions: (
          <>
            <Button size="sm" variant="primary" icon="tick" keys="F2" onClick={props.onDone}>
              Done
            </Button>
            <Button size="sm" keys="Esc" onClick={props.onCancelPart} title="Keep the flow as it was">
              Keep it as it was
            </Button>
          </>
        ),
      };
    case 'Testing':
      return going ? null : { name: 'Testing', tone: 'accent', label: 'Testing', busy: true, tinted: true, message: mode.message };
    case 'Check mode':
      return {
        name: 'Check mode', tone: 'ok', label: 'Check', pulse: false, tinted: true,
        message: 'Click what the flow should check in the app, then choose the check; nothing is clicked there',
        actions: leave('Leave', 'Esc', props.onCheck, 'Leave check mode (F6, or Esc in the app)'),
      };
    case 'Inspect':
      return {
        name: 'Inspect', tone: 'inspect', label: 'Inspect', tinted: true,
        message: 'Point at an element in the app; a click there picks it here, and nothing else',
        actions: leave('Stop', 'Esc', props.onInspect, 'Stop picking in the app (Ctrl+Shift+C)'),
      };
    case 'Trying':
      return {
        name: 'Trying', tone: 'try', label: 'Trying', tinted: true, message: mode.message,
        actions: leave('Stop trying', 'F7', props.onTry, 'Go back to recording (F7)'),
      };
    case 'Paused':
      return {
        name: 'Paused', tone: 'warn', label: 'Paused', tinted: true, message: mode.message, full: state.notice,
        actions: leave('Resume', 'F8', props.onPause, 'Record again, from the page the app is on'),
      };
    default:
      return null;
  }
}

// Only recording: what to do, and the modes a key away.
function recording(props: Props): Shown {
  const empty = props.actions.length === 0;
  return {
    name: 'Recording', tone: 'rec', label: 'Recording', pulse: true,
    message: empty ? 'Work in the app: each action shows here, with the code it writes' : 'Each action you do in the app is added to the flow',
  };
}

function testProgress(test: TestRun, actions: Action[], now: number, props: Props): Shown {
  const running = actions.find((action) => test.steps[action.id]?.state === 'running');
  const passed = Object.values(test.steps).filter((step) => step.state === 'passed').length;
  const phases: Record<string, string> = {
    building: 'Building the flow, as it will be saved…',
    window: 'Opening a window of its own…',
    start: 'Opening the start page…',
    login: `Logging in with ${test.login}…`,
  };
  const preparing = isPreparing(test);
  const since = preparing ? (test.since ?? test.started) : test.started;
  return {
    name: 'Test run', tone: test.state === 'login' ? 'info' : 'accent', label: 'Testing', busy: true, tinted: true,
    message: preparing ? phases[test.state] : (
      <>
        Step {numberOf(actions, running?.id) || passed + 1} of {test.total}
        {running && (
          <span class="text-fg/80">
            {' · '}
            {verbOf(running)} <span dir="auto">{subjectOf(running)}</span>
          </span>
        )}
      </>
    ),
    time: `${(Math.max(0, now - since) / 1000).toFixed(1)} s`,
    actions: (
      <Button size="sm" variant="secondary" keys="Esc" onClick={props.onStopTest} title="Stop the test (Esc or Shift+F5)">
        Stop
      </Button>
    ),
  };
}

function testResult(test: TestRun, actions: Action[], props: Props): Shown {
  const failure = test.failure;
  const failedAt = numberOf(actions, failure?.action);
  const of = test.total < actions.length && !test.stale ? `${test.total} of ${actions.length}` : `${test.total}`;
  const again = (
    <Button size="sm" icon="refresh" onClick={props.onAgain} title={test.upTo ? 'Run to the same step again (Ctrl+Shift+Enter)' : 'Run the test again (Ctrl+Enter)'} collapse="md">
      Run again
    </Button>
  );
  const dismiss = <Button size="sm" iconOnly icon="cross" label="Dismiss the test run" onClick={props.onClearTest} />;
  const stale = test.stale ? ' · changed since' : '';
  const slow = test.slowMotion ? ' · slow motion' : '';
  const warnings = test.warnings.length ? ` · ${plural(test.warnings.length, 'warning')}` : '';
  const time = test.ms !== undefined ? duration(test.ms) : undefined;
  switch (test.state) {
    case 'passed':
      return {
        name: 'Test run', tone: 'ok', label: test.upTo ? `Passed up to step ${test.total}` : 'Passed', icon: 'tick', time,
        message: `${test.upTo ? `${of} steps ran` : `${plural(test.total, 'step')}, all as recorded`}${stale}${slow}${warnings}`,
        full: test.warnings.join('\n') || undefined,
        actions: (
          <>
            {again}
            {dismiss}
          </>
        ),
      };
    case 'failed':
      return {
        name: 'Test run', tone: 'bad',
        label: failure?.inLogin ? `${failure.inLogin} failed` : failedAt ? `Failed at step ${failedAt} of ${test.total}` : failure?.action ? 'Failed at a step deleted since' : 'Failed before the first step',
        icon: 'cross', time, tinted: true, message: `${failure?.message ?? ''}${stale}`, full: failure?.headline,
        actions: (
          <>
            {failedAt > 0 && (
              <Button size="sm" variant="secondary" onClick={() => props.onShowStep(failure!.action!)} title="Select the step that failed">
                Show step
              </Button>
            )}
            {again}
            {dismiss}
          </>
        ),
      };
    case 'stopped': {
      const passed = Object.values(test.steps).filter((step) => step.state === 'passed').length;
      return {
        name: 'Test run', tone: 'muted', label: 'Stopped', icon: 'stop', message: passed > 0 ? `after ${plural(passed, 'step')} passed` : 'before the first step',
        actions: (
          <>
            {again}
            {dismiss}
          </>
        ),
      };
    }
    default:
      return {
        name: 'Test run', tone: 'bad', label: 'Could not test', icon: 'warn', tinted: true, message: test.problem, full: test.problem,
        actions: (
          <>
            {again}
            {dismiss}
          </>
        ),
      };
  }
}

function progressOf(test: TestRun, actions: Action[]): { tone: Tone; share: number; indeterminate: boolean } | null {
  if (test.state === 'error' || test.state === 'stopped') return null;
  const passed = Object.values(test.steps).filter((step) => step.state === 'passed').length;
  const failedAt = numberOf(actions, test.failure?.action);
  const total = Math.max(test.total, 1);
  if (isPreparing(test)) return { tone: test.state === 'login' ? 'info' : 'accent', share: 0, indeterminate: true };
  if (test.state === 'passed') return { tone: 'ok', share: 1, indeterminate: false };
  if (test.state === 'failed') return { tone: 'bad', share: Math.max(failedAt - 1, 0) / total, indeterminate: false };
  return { tone: 'accent', share: passed / total, indeterminate: false };
}

// The last test's result, while the strip says something else: a click shows it, its ✕ dismisses it.
function ResultChip({ shown }: { shown: Shown }) {
  return (
    <span role="status" aria-label="Test run" title={typeof shown.message === 'string' ? `${shown.label}: ${shown.message}` : shown.label} class={cls('ml-1 inline-flex h-6 items-center gap-1.5 rounded-full border border-line-strong bg-panel pr-2 pl-1.5 text-meta font-medium', toneText[shown.tone])}>
      {shown.icon && <Icon name={shown.icon} size={12} class="stroke-[2.6]" />}
      <span class="max-[900px]:hidden">{shown.label}</span>
    </span>
  );
}

function ResultMark({ tone, icon }: { tone: Tone; icon: IconName }) {
  return (
    <span class={cls('grid size-4 shrink-0 place-items-center rounded-full text-canvas', tone === 'muted' ? 'bg-faint' : tone === 'ok' ? 'bg-ok' : tone === 'warn' ? 'bg-warn' : 'bg-bad')}>
      <Icon name={icon} size={icon === 'stop' ? 8 : 10} fill={icon === 'stop' ? 'currentColor' : 'none'} class="stroke-[3]" />
    </span>
  );
}

// Commands the strip sends itself, as nothing else offers them.
const sendLoginByHand = () => send({ type: 'loginByHand' });
const sendRerecordFrom = () => send({ type: 'rerecordFrom' });
const sendDismissPart = () => send({ type: 'rerecordDismiss' });
