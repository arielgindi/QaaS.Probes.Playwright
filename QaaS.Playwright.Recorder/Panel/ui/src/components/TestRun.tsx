import type { ComponentChildren } from 'preact';
import { useEffect } from 'preact/hooks';
import { duration } from '../test';
import type { TestFailure, TestStep } from '../types';
import { Icon } from './icons';
import { Spinner } from './ui';

// How a test went, step by step: on each card, and why one failed. The run itself shows in the status strip.

/** An action's state in the run, at the right of its card. */
export function StepStatus({ step, now }: { step: TestStep; now: number }) {
  switch (step.state) {
    case 'running':
      return (
        <span class="inline-flex shrink-0 items-center gap-1 text-meta font-medium text-accent-fg tabular-nums">
          <Spinner size={10} />
          {step.since !== undefined && `${(Math.max(0, now - step.since) / 1000).toFixed(1)} s`}
        </span>
      );
    case 'passed':
      return (
        <span title="Passed" class="inline-flex shrink-0 items-center gap-0.5 text-meta text-ok tabular-nums">
          <Icon name="tick" size={11} class="stroke-[2.6]" />
          {step.ms !== undefined && duration(step.ms)}
        </span>
      );
    case 'failed':
      return (
        <span class="inline-flex h-4 shrink-0 items-center gap-0.5 rounded-sm bg-bad/14 pr-1.5 pl-1 text-tag leading-none font-semibold text-bad">
          <Icon name="cross" size={10} class="stroke-[2.8]" />
          Failed
        </span>
      );
    default:
      return <span class="shrink-0 text-tag text-faint">Not run</span>;
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
    <div class="animate-step-in mt-1 overflow-hidden rounded-lg border border-bad/30 bg-panel">
      <div class="border-b border-bad/15 bg-bad/[0.06] px-3 py-2 text-body font-medium text-bad">{failure.message}</div>
      <div class="px-3 pt-2.5 pb-3">
        <dl class="space-y-2 text-meta">
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
              <span class="inline-flex items-center gap-1.5 rounded-md bg-black/70 px-2 py-1 text-meta font-medium text-white">
                <Icon name="expand" size={12} />
                Enlarge
              </span>
            </span>
          </button>
        )}
        {failure.checks.map((check) => (
          <div key={check} class="mt-2.5 flex gap-1.5 rounded-md bg-warn/10 px-2 py-1.5 text-meta text-warn">
            <Icon name="warn" size={12} class="mt-0.5 shrink-0" />
            <span>
              <span class="font-semibold">Check by hand:</span> {check}
            </span>
          </div>
        ))}
        {failure.hints.map((hint) => (
          <p key={hint} class="mt-2.5 flex gap-1.5 text-meta text-muted">
            <Icon name="bulb" size={12} class="mt-0.5 shrink-0 text-faint" />
            <span>{hint}</span>
          </p>
        ))}
        {failure.callLog && (
          <details class="group/log mt-2.5">
            <summary class="flex cursor-pointer list-none items-center gap-1 text-meta font-medium text-muted transition select-none hover:text-fg">
              <Icon name="chevron" size={12} class="transition group-open/log:rotate-90" />
              Playwright's call log
            </summary>
            <pre dir="ltr" class="mt-1.5 max-h-40 overflow-auto rounded-md border border-line bg-canvas px-2.5 py-2 font-mono text-tag whitespace-pre-wrap text-muted">
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
      <dt class="text-tag font-semibold text-faint">{label}</dt>
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
      <img src={src} alt="The page when it failed" class="max-h-[calc(100vh-88px)] max-w-full rounded-lg shadow-e3 ring-1 ring-white/12" />
      <div class="flex max-w-full min-w-0 items-center gap-3 text-meta text-white/70">
        <span dir="ltr" class="min-w-0 truncate font-mono">
          {caption}
        </span>
        <span class="shrink-0 text-white/50">Click or Esc to close</span>
      </div>
    </div>
  );
}
