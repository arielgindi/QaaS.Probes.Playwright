import type { ComponentChildren } from 'preact';
import { useEffect, useState } from 'preact/hooks';
import { duration, isGoing, isPreparing, numberOf, useNow } from '../test';
import { cls, plural, subjectOf, verbOf } from '../text';
import type { Action, TestFailure, TestRun, TestStep } from '../types';
import { Icon } from './icons';

export function Spinner({ size = 14, class: className }: { size?: number; class?: string }) {
  return (
    <span
      aria-hidden="true"
      class={cls('inline-block shrink-0 animate-spin-slow rounded-full border-[1.5px] border-current border-t-transparent', className)}
      style={{ width: size, height: size }}
    />
  );
}

interface BarProps {
  test: TestRun;
  /** The flow's actions, in the order they run. */
  actions: Action[];
  onAgain(): void;
  onStop(): void;
  onClear(): void;
  onShow(action: string): void;
}

type Tone = 'going' | 'login' | 'passed' | 'failed' | 'stopped';

const bars: Record<Tone, string> = {
  going: 'border-accent/25 bg-accent/[0.07]',
  login: 'border-info/25 bg-info/[0.07]',
  passed: 'border-ok/25 bg-ok/[0.07]',
  failed: 'border-bad/30 bg-bad/[0.07]',
  stopped: 'border-line bg-panel',
};

const fills: Record<Tone, string> = {
  going: 'bg-accent',
  login: 'bg-info',
  passed: 'bg-ok',
  failed: 'bg-bad',
  stopped: 'bg-faint',
};

/** The test run's summary, under the top bar: where it is or how it ended, how long it took, and what to do next. */
export function TestBar({ test, actions, onAgain, onStop, onClear, onShow }: BarProps) {
  const going = isGoing(test);
  const preparing = isPreparing(test);
  const now = useNow(going, 100);
  const [open, setOpen] = useState(false);
  useEffect(() => setOpen(false), [test.run]);

  const running = actions.find((action) => test.steps[action.id]?.state === 'running');
  const passed = Object.values(test.steps).filter((step) => step.state === 'passed').length;
  const failure = test.failure;
  const failedAt = numberOf(actions, failure?.action);
  const of = test.total < actions.length && !test.stale ? `${test.total} of ${actions.length}` : `${test.total}`;

  let tone: Tone;
  let icon: ComponentChildren;
  let title: string;
  let detail: ComponentChildren = null;
  switch (test.state) {
    case 'building':
      [tone, icon, title, detail] = ['going', <Spinner class="text-accent-fg" />, 'Building the flow…', 'exactly as it will be saved'];
      break;
    case 'window':
      [tone, icon, title, detail] = ['going', <Spinner class="text-accent-fg" />, 'Opening a browser window…', 'a window of its own, over the recorded one'];
      break;
    case 'start':
      [tone, icon, title, detail] = ['going', <Spinner class="text-accent-fg" />, 'Opening the start page…', 'BaseUrl, as the probe opens it'];
      break;
    case 'login':
      [tone, icon, title, detail] = ['login', <Spinner class="text-info" />, `Logging in with ${test.login}…`, 'first, as the probe runs it'];
      break;
    case 'running':
      tone = 'going';
      icon = <Spinner class="text-accent-fg" />;
      title = `Step ${numberOf(actions, running?.id) || passed + 1} of ${test.total}`;
      detail = running && <Subject action={running} />;
      break;
    case 'passed':
      tone = 'passed';
      icon = <Badge tone="ok" icon="tick" />;
      title = test.upTo ? `Passed up to step ${test.total}` : 'Passed';
      detail = test.upTo ? `${of} steps ran` : `${plural(test.total, 'step')}, all as recorded`;
      break;
    case 'failed':
      tone = 'failed';
      icon = <Badge tone="bad" icon="cross" />;
      title = failure?.inLogin ? `${failure.inLogin} failed` : failedAt ? `Failed at step ${failedAt} of ${test.total}` : 'Failed before the first step';
      detail = <span class="text-bad/90">{failure?.message}</span>;
      break;
    case 'stopped':
      tone = 'stopped';
      icon = <Badge tone="muted" icon="stop" />;
      title = 'Stopped';
      detail = passed > 0 ? `after ${plural(passed, 'step')} passed` : 'before the first step';
      break;
    default:
      tone = 'failed';
      icon = <Badge tone="bad" icon="warn" />;
      title = 'Could not test';
      detail = <span class="text-bad/90">{test.problem}</span>;
  }

  const progress =
    test.state === 'passed' ? 1 : test.state === 'failed' ? Math.max(failedAt - 1, 0) / Math.max(test.total, 1) : passed / Math.max(test.total, 1);
  const time = going ? Math.max(0, now - test.started) : test.ms;
  const wraps = test.state === 'error' || test.state === 'failed';

  return (
    <div class="shrink-0">
      <div
        role="status"
        aria-label="Test run"
        aria-live="polite"
        class={cls('relative flex min-h-10 shrink-0 gap-3 border-b py-2 pr-2.5 pl-4 text-[12.5px] leading-snug', bars[tone], wraps ? 'items-start' : 'items-center')}
      >
        <span class={cls('grid size-5 shrink-0 place-items-center', wraps && 'mt-px')}>{icon}</span>
        <div class={cls('flex min-w-0 flex-1 gap-x-2 gap-y-0.5', wraps ? 'flex-wrap items-baseline pt-px' : 'items-baseline')}>
          <span class="shrink-0 font-semibold text-fg">{title}</span>
          {preparing && (
            <span class="shrink-0 font-mono text-[11.5px] text-fg/80 tabular-nums" title="This part so far">
              {(Math.max(0, now - (test.since ?? test.started)) / 1000).toFixed(1)} s
            </span>
          )}
          {detail && <span class={cls('min-w-0 text-muted', !wraps && 'truncate')}>{detail}</span>}
        </div>
        <div class="flex shrink-0 items-center gap-1.5">
          {test.slowMotion && <Tag icon="gauge" title="Each action waited 500 ms, to watch it">Slow</Tag>}
          {test.stale && !going && <Tag icon="pencil" title="The flow changed since this run: test it again for the steps as they are now">Changed since</Tag>}
          {test.warnings.length > 0 && (
            <Tag icon="warn" tone="warn" title={test.warnings.join('\n')}>
              {plural(test.warnings.length, 'warning')}
            </Tag>
          )}
          {time !== undefined && !preparing && test.state !== 'error' && (
            <span class="min-w-[44px] text-right font-mono text-[11.5px] text-muted tabular-nums" title={going ? 'Running for' : 'Took'}>
              {going ? `${(time / 1000).toFixed(1)} s` : duration(time)}
            </span>
          )}
          <span class="mx-1 h-4 w-px bg-line-strong/70" />
          {going ? (
            <button type="button" onClick={onStop} title="Stop the test (Esc or Shift+F5)" class="inline-flex h-7 items-center gap-1.5 rounded-md px-1.5 text-[11.5px] text-faint transition hover:bg-raised hover:text-fg">
              <kbd class="rounded border border-line-strong bg-panel px-1 font-sans text-[10.5px] leading-4 font-medium">Esc</kbd>
              <span class="max-[620px]:hidden">to stop</span>
            </button>
          ) : (
            <>
              {failure?.action && (
                <BarButton onClick={() => onShow(failure.action!)} title="Select the step that failed">
                  Show step
                </BarButton>
              )}
              {test.problemDetail && (
                <BarButton onClick={() => setOpen(!open)} title="What the build said" pressed={open}>
                  Details
                  <Icon name="down" size={12} class={cls('transition', open && 'rotate-180')} />
                </BarButton>
              )}
              <BarButton onClick={onAgain} title={test.upTo ? 'Run to the same step again (Ctrl+Shift+Enter)' : 'Test the flow again (Ctrl+Enter)'}>
                <Icon name="refresh" size={13} />
                <span class="max-[620px]:hidden">Run again</span>
              </BarButton>
              <button type="button" onClick={onClear} title="Dismiss" aria-label="Dismiss the test run" class="grid size-7 place-items-center rounded-md text-faint transition hover:bg-raised hover:text-fg">
                <Icon name="cross" size={13} />
              </button>
            </>
          )}
        </div>
        {test.state !== 'error' && test.state !== 'stopped' && (
          <span class="absolute inset-x-0 bottom-[-1px] h-[2px] overflow-hidden">
            {preparing ? (
              <span class={cls('absolute inset-y-0 w-1/3 animate-indeterminate', fills[tone])} />
            ) : (
              <span class={cls('block h-full transition-[width] duration-500 ease-out', fills[tone])} style={{ width: `${progress * 100}%` }} />
            )}
          </span>
        )}
      </div>
      {open && test.problemDetail && (
        <pre class="max-h-48 shrink-0 overflow-auto border-b border-line bg-canvas px-4 py-3 font-mono text-[11.5px] leading-5 whitespace-pre-wrap text-muted">
          {test.problemDetail}
        </pre>
      )}
    </div>
  );
}

function Subject({ action }: { action: Action }) {
  return (
    <span class="inline-flex min-w-0 items-baseline gap-1.5">
      <span class="shrink-0">{verbOf(action)}</span>
      <span dir="auto" class="truncate font-medium text-fg/85">
        {subjectOf(action)}
      </span>
    </span>
  );
}

function Badge({ tone, icon }: { tone: 'ok' | 'bad' | 'muted'; icon: 'tick' | 'cross' | 'stop' | 'warn' }) {
  return (
    <span
      class={cls(
        'grid size-5 place-items-center rounded-full',
        tone === 'ok' ? 'bg-ok text-canvas' : tone === 'bad' ? 'bg-bad text-canvas' : 'bg-line-strong text-muted',
      )}
    >
      <Icon name={icon} size={icon === 'stop' ? 10 : 12} fill={icon === 'stop' ? 'currentColor' : 'none'} class="stroke-[2.8]" />
    </span>
  );
}

function Tag({ icon, tone, title, children }: { icon: 'gauge' | 'pencil' | 'warn'; tone?: 'warn'; title: string; children: ComponentChildren }) {
  return (
    <span
      title={title}
      class={cls(
        'inline-flex h-6 shrink-0 items-center gap-1 rounded-md border px-1.5 text-[11px] font-medium max-[760px]:hidden',
        tone === 'warn' ? 'border-warn/35 text-warn' : 'border-line-strong text-muted',
      )}
    >
      <Icon name={icon} size={12} />
      {children}
    </span>
  );
}

function BarButton({ onClick, title, pressed, children }: { onClick(): void; title: string; pressed?: boolean; children: ComponentChildren }) {
  return (
    <button
      type="button"
      onClick={onClick}
      title={title}
      aria-pressed={pressed}
      class={cls(
        'inline-flex h-7 shrink-0 items-center gap-1.5 rounded-md border px-2 text-[12px] font-medium transition',
        pressed ? 'border-line-strong bg-raised text-fg' : 'border-line-strong/80 bg-panel/60 text-fg hover:border-faint/60 hover:bg-raised',
      )}
    >
      {children}
    </button>
  );
}

/** An action's state in the run, at the right of its card. */
export function StepStatus({ step, now }: { step: TestStep; now: number }) {
  switch (step.state) {
    case 'running':
      return (
        <span class="inline-flex shrink-0 items-center gap-1 text-[11px] font-medium text-accent-fg tabular-nums">
          <Spinner size={10} />
          {step.since !== undefined && `${(Math.max(0, now - step.since) / 1000).toFixed(1)} s`}
        </span>
      );
    case 'passed':
      return (
        <span title="Passed" class="inline-flex shrink-0 items-center gap-0.5 text-[11px] text-ok tabular-nums">
          <Icon name="tick" size={11} class="stroke-[2.6]" />
          {step.ms !== undefined && duration(step.ms)}
        </span>
      );
    case 'failed':
      return (
        <span class="inline-flex shrink-0 items-center gap-0.5 rounded-full bg-bad/14 py-px pr-1.5 pl-1 text-[10.5px] leading-4 font-semibold text-bad">
          <Icon name="cross" size={10} class="stroke-[2.8]" />
          Failed
        </span>
      );
    default:
      return <span class="shrink-0 text-[10.5px] text-faint">Not run</span>;
  }
}

interface FailureProps {
  failure: TestFailure;
  /** The flow's file, e.g. NewTicket.cs. */
  file: string;
  onEnlarge(src: string, caption: string): void;
}

/** Why a step failed, as the probe tells it: what it waited for, on which page, the line, the screenshot, what to check. */
export function FailureDetails({ failure, file, onEnlarge }: FailureProps) {
  return (
    <div class="animate-step-in mt-1 overflow-hidden rounded-lg border border-bad/30 bg-panel shadow-[0_1px_0_0_color-mix(in_oklab,var(--bad)_8%,transparent)]">
      <div class="border-b border-bad/15 bg-bad/[0.06] px-3 py-2 text-[12.5px] leading-snug font-medium text-bad">{failure.message}</div>
      <div class="px-3 pt-2.5 pb-3">
        <dl class="space-y-1.5 text-[11.5px] leading-[1.5]">
          {failure.inLogin && <Row label="In">{failure.inLogin}, before the flow</Row>}
          {failure.waitingFor && (
            <Row label="Waited for">
              <code dir="ltr" class="font-mono [overflow-wrap:anywhere] text-fg/90">
                {failure.waitingFor}
              </code>
            </Row>
          )}
          {failure.url && (
            <Row label="On page">
              <span dir="ltr" class="font-mono [overflow-wrap:anywhere] text-fg/90">
                {failure.url}
              </span>
            </Row>
          )}
          {failure.line !== undefined && !failure.inLogin && (
            <Row label="Line">
              <span class="font-mono text-fg/90">{failure.line}</span>
              <span class="text-faint"> of {file}</span>
            </Row>
          )}
        </dl>
        {failure.screenshot && (
          <button
            type="button"
            onClick={() => onEnlarge(failure.screenshot!, failure.url ?? 'The page when it failed')}
            aria-label="Enlarge the screenshot of the page when it failed"
            class="group/shot relative mt-2.5 block w-full overflow-hidden rounded-md border border-line-strong bg-canvas"
          >
            <img src={failure.screenshot} alt="The page when it failed" class="block aspect-[16/9] w-full object-cover object-top" />
            <span class="absolute inset-0 grid place-items-center bg-black/0 opacity-0 transition group-hover/shot:bg-black/30 group-hover/shot:opacity-100">
              <span class="inline-flex items-center gap-1.5 rounded-md bg-black/70 px-2 py-1 text-[11.5px] font-medium text-white">
                <Icon name="expand" size={12} />
                Enlarge
              </span>
            </span>
          </button>
        )}
        {failure.checks.map((check) => (
          <div key={check} class="mt-2.5 flex gap-1.5 rounded-md bg-warn/10 px-2 py-1.5 text-[11.5px] leading-snug text-warn">
            <Icon name="warn" size={12} class="mt-px shrink-0" />
            <span>
              <span class="font-semibold">Check by hand:</span> {check}
            </span>
          </div>
        ))}
        {failure.hints.map((hint) => (
          <p key={hint} class="mt-2.5 flex gap-1.5 text-[11.5px] leading-snug text-muted">
            <Icon name="bulb" size={12} class="mt-px shrink-0 text-faint" />
            <span>{hint}</span>
          </p>
        ))}
        {failure.callLog && (
          <details class="group/log mt-2.5">
            <summary class="flex cursor-pointer list-none items-center gap-1 text-[11.5px] font-medium text-faint transition select-none hover:text-fg">
              <Icon name="chevron" size={12} class="transition group-open/log:rotate-90" />
              Playwright's call log
            </summary>
            <pre dir="ltr" class="mt-1.5 max-h-40 overflow-auto rounded-md border border-line bg-canvas px-2.5 py-2 font-mono text-[10.5px] leading-[1.6] whitespace-pre-wrap text-muted">
              {failure.callLog}
            </pre>
          </details>
        )}
      </div>
    </div>
  );
}

// A label over its value, which then has the card's whole width: a locator or an address seldom fits beside it.
function Row({ label, children }: { label: string; children: ComponentChildren }) {
  return (
    <div>
      <dt class="text-[10px] font-medium tracking-[0.06em] text-faint uppercase">{label}</dt>
      <dd class="min-w-0 text-muted">{children}</dd>
    </div>
  );
}

/** A screenshot at the size of the window; a click or Esc closes it. */
export function Lightbox({ src, caption, onClose }: { src: string; caption: string; onClose(): void }) {
  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      if (event.key !== 'Escape') return;
      event.preventDefault();
      event.stopPropagation();
      onClose();
    };
    addEventListener('keydown', onKey, true);
    return () => removeEventListener('keydown', onKey, true);
  }, [onClose]);
  return (
    <div
      role="dialog"
      aria-modal="true"
      aria-label="The page when it failed"
      onClick={onClose}
      class="animate-fade-in fixed inset-0 z-50 flex cursor-zoom-out flex-col items-center justify-center gap-3 bg-black/80 p-6 backdrop-blur-[2px]"
    >
      <img src={src} alt="The page when it failed" class="max-h-[calc(100vh-88px)] max-w-full rounded-lg shadow-2xl ring-1 ring-white/12" />
      <div class="flex max-w-full min-w-0 items-center gap-3 text-[12px] text-white/65">
        <span dir="ltr" class="min-w-0 truncate font-mono">
          {caption}
        </span>
        <span class="shrink-0 text-white/40">Click or Esc to close</span>
      </div>
    </div>
  );
}
