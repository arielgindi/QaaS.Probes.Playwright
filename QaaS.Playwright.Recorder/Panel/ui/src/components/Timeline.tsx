import { useEffect, useRef, useState } from 'preact/hooks';
import { isGoing, useNow } from '../test';
import { cls, detailOf, effectLabel, subjectOf, verbOf } from '../text';
import type { Action, Effect, Expect, PanelState, Status, Step, TestFailure, TestRun, TestStep } from '../types';
import { Icon, type IconName, kindIcon } from './icons';
import { PaneHeader } from './PaneHeader';
import { FailureDetails, Spinner, StepStatus } from './TestRun';

interface Props {
  steps: Step[];
  count: number;
  notes: string[];
  selected: string | null;
  status: Status;
  /** The test run going on, or the last one. */
  test?: TestRun;
  /** The recording this one continues, whose last action a divider follows. */
  continued?: PanelState['continued'];
  /** The flow's file, e.g. NewTicket.cs, which a failure's line is of. */
  file: string;
  /** Whether "Run to here" can start: no test runs, and the recording is not saving or logging in. */
  canRun: boolean;
  /** Whether the person may change the flow: not while it is saved. */
  canEdit: boolean;
  /** Whether the flow writes its auto checks. */
  autoChecks: boolean;
  onSelect(id: string): void;
  onHover(id: string | null): void;
  onRunTo(id: string): void;
  onEnlarge(src: string, caption: string): void;
  onDelete(id: string): void;
  onRenameStep(action: string, name: string): void;
  onDropCheck(action: string, key: string, drop: boolean): void;
  onAutoChecks(on: boolean): void;
}

/**
 * The steps of the flow, each a method, with a card per action, the checks after it, and how each went in the last test
 * run. A step is renamed in place, an action deleted from its card, and an auto check dropped with its ✕.
 */
export function Timeline(props: Props) {
  const { steps, count, notes, selected, status, test, continued, file, canRun, canEdit, autoChecks, onSelect, onHover, onRunTo, onEnlarge } = props;
  const list = useRef<HTMLDivElement>(null);
  const now = useNow(isGoing(test), 100);
  const running = test && Object.entries(test.steps).find(([, step]) => step.state === 'running')?.[0];
  useEffect(() => {
    list.current?.querySelector('[aria-current="true"]')?.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
  }, [selected, count]);
  // The step running stays in view as the test goes through the flow.
  useEffect(() => {
    if (running) list.current?.querySelector(`[data-action="${CSS.escape(running)}"]`)?.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
  }, [running]);

  const failure = test?.failure;
  return (
    <section aria-label="Steps" class="area-steps flex min-h-0 flex-col border-r border-line bg-panel">
      <PaneHeader title="Steps">
        {count > 0 && <span class="tabular-nums">{count}</span>}
        <Switch on={autoChecks} disabled={!canEdit} onChange={props.onAutoChecks} />
      </PaneHeader>
      <div ref={list} class="min-h-0 flex-1 overflow-y-auto px-2 pb-4" onMouseLeave={() => onHover(null)}>
        {failure && !failure.action && test.state === 'failed' && (
          <div class="px-1 pt-3">
            <FailureDetails failure={failure} file={file} onEnlarge={onEnlarge} />
          </div>
        )}
        {steps.length === 0 ? (
          <Empty status={status} />
        ) : (
          steps.map((step) => (
            <div key={step.actions[0]?.id ?? step.name} class="pt-3">
              <div class="flex min-w-0 items-center gap-2 px-2 pb-1.5 text-[11px] text-faint">
                <StepName name={step.name} disabled={!canEdit || step.actions.length === 0} onRename={(name) => props.onRenameStep(step.actions[0].id, name)} />
                {step.dialog && (
                  <span dir="auto" class="min-w-0 truncate rounded border border-line px-1.5 leading-4">
                    {step.dialog}
                  </span>
                )}
                {test && <StepMark actions={step.actions} test={test} />}
              </div>
              <ol class="relative space-y-1 before:absolute before:top-4 before:bottom-4 before:left-[22px] before:w-px before:bg-line-strong">
                {step.actions.flatMap((action) => [
                  <Card
                    key={action.id}
                    action={action}
                    selected={action.id === selected}
                    test={test?.steps[action.id]}
                    failure={test?.state === 'failed' && failure?.action === action.id ? failure : undefined}
                    file={file}
                    now={now}
                    canRun={canRun}
                    canEdit={canEdit}
                    onSelect={onSelect}
                    onHover={onHover}
                    onRunTo={onRunTo}
                    onEnlarge={onEnlarge}
                    onDelete={props.onDelete}
                    onDropCheck={props.onDropCheck}
                  />,
                  action.id === continued?.after && <Continued key="continued" label={continued.label} />,
                ])}
              </ol>
            </div>
          ))
        )}
        {status === 'paused' && steps.length > 0 && (
          <div class="mx-1 mt-3 flex items-center gap-2 rounded-lg border border-dashed border-warn/40 px-3 py-2 text-[12px] text-warn">
            <Icon name="pause" size={13} class="shrink-0" />
            Paused here: nothing is recorded until you resume
          </div>
        )}
        {notes.length > 0 && (
          <div class="mx-1 mt-4 rounded-lg border border-warn/30 bg-warn/8 p-2.5 text-[12px] text-warn">
            <div class="mb-1 flex items-center gap-1.5 font-medium">
              <Icon name="warn" size={13} />
              Check by hand
            </div>
            <ul class="space-y-1 pl-5 leading-snug">
              {notes.map((note) => (
                <li key={note} class="list-disc">
                  {note}
                </li>
              ))}
            </ul>
          </div>
        )}
      </div>
    </section>
  );
}

// Where the recording continued ends, and what is recorded now begins.
function Continued({ label }: { label: string }) {
  return (
    <li role="separator" class="flex items-center gap-2 py-1.5 pl-2 text-[11px] text-info">
      <span class="flex-1 border-t border-dashed border-info/50" />
      {label}
      <span class="flex-1 border-t border-dashed border-info/50" />
    </li>
  );
}

interface CardProps extends Pick<Props, 'file' | 'canRun' | 'canEdit' | 'onSelect' | 'onHover' | 'onRunTo' | 'onEnlarge' | 'onDelete' | 'onDropCheck'> {
  action: Action;
  selected: boolean;
  test?: TestStep;
  failure?: TestFailure;
  now: number;
}

function Card({ action, selected, test, failure, file, now, canRun, canEdit, onSelect, onHover, onRunTo, onEnlarge, onDelete, onDropCheck }: CardProps) {
  const warn = action.checks.length > 0;
  const detail = detailOf(action);
  const state = test?.state;
  const expects = action.expects ?? [];
  return (
    <li
      data-action={action.id}
      onMouseEnter={() => onHover(action.id)}
      class={cls(
        'group/card relative animate-step-in rounded-lg border transition',
        state === 'skipped' && 'opacity-55',
        state === 'failed'
          ? selected
            ? 'border-bad/60 bg-bad/12'
            : 'border-bad/40 bg-bad/[0.07] hover:bg-bad/10'
          : state === 'running'
            ? 'border-accent/50 bg-accent/[0.08] shadow-[0_0_0_3px_color-mix(in_oklab,var(--accent)_10%,transparent)]'
            : selected
              ? 'border-accent/45 bg-accent/10'
              : 'border-transparent hover:bg-raised',
      )}
    >
      <button type="button" aria-current={selected} onClick={() => onSelect(action.id)} class="relative block w-full rounded-lg py-2 pr-2.5 pl-11 text-left">
        <span
          class={cls(
            'absolute top-[7px] left-[10px] grid size-6 place-items-center rounded-md border bg-panel transition',
            state === 'failed'
              ? 'border-bad/70 bg-bad/12 text-bad'
              : state === 'passed'
                ? 'border-ok/45 text-ok'
                : state === 'running'
                  ? 'border-accent text-accent-fg'
                  : warn
                    ? 'border-warn/50 text-warn'
                    : selected
                      ? 'border-accent/60 text-accent-fg'
                      : 'border-line-strong text-muted',
          )}
        >
          <Icon name={kindIcon(action.kind)} size={13} />
        </span>
        <span class="flex min-w-0 items-baseline gap-1.5">
          <span class="shrink-0 text-[12px] text-muted">{verbOf(action)}</span>
          <span dir="auto" class="min-w-0 truncate font-medium text-fg">
            {subjectOf(action)}
          </span>
          {(action.added || test) && (
            <span class={cls('ml-auto flex shrink-0 items-center gap-1.5 self-center', (canRun || canEdit) && 'group-hover/card:invisible')}>
              {action.added && (
                <span title="Added in the panel's Elements view" class="rounded border border-accent/30 bg-accent/10 px-1 text-[10px] leading-4 font-medium text-accent-fg">
                  added
                </span>
              )}
              {test && <StepStatus step={test} now={now} />}
            </span>
          )}
        </span>
        {detail && (
          <span class="mt-0.5 flex min-w-0 items-baseline gap-1.5 text-[12px]">
            <span class="shrink-0 text-faint">{action.kind === 'choose' ? 'option' : action.kind === 'press' ? 'key' : 'with'}</span>
            <span dir="auto" class="min-w-0 truncate text-muted">
              {detail}
            </span>
          </span>
        )}
        {action.locator && (
          <span class="mt-1.5 flex min-w-0">
            <code dir="ltr" class="min-w-0 truncate rounded-md border border-line bg-canvas px-1.5 py-px font-mono text-[11px] text-muted">
              {action.locator}
            </code>
          </span>
        )}
        {action.effects.length > 0 && (
          <span class="mt-1.5 flex flex-wrap gap-1">
            {action.effects.map((effect) => (
              <Badge key={effect.kind + effect.text} effect={effect} />
            ))}
          </span>
        )}
        {action.checks.map((check) => (
          <span key={check} class="mt-1.5 flex gap-1.5 rounded-md bg-warn/10 px-1.5 py-1 text-[11.5px] leading-snug text-warn">
            <Icon name="warn" size={12} class="mt-px shrink-0" />
            <span>{check}</span>
          </span>
        ))}
      </button>
      {expects.length > 0 && (
        <ul aria-label="Checks" class="space-y-px pr-1.5 pb-1.5 pl-[38px]">
          {expects.map((expect) => (
            <ExpectRow key={expect.key} expect={expect} canEdit={canEdit} onDrop={(drop) => (expect.auto ? onDropCheck(action.id, expect.key, drop) : onDelete(expect.key))} />
          ))}
        </ul>
      )}
      <span class="absolute top-[6px] right-1.5 hidden items-center gap-1 group-hover/card:flex group-focus-within/card:flex">
        {canRun && (
          <button
            type="button"
            onClick={() => onRunTo(action.id)}
            title="Run the flow up to this step, and this step too (Ctrl+Shift+Enter on the selected step)"
            class="inline-flex h-6 items-center gap-1 rounded-md border border-line-strong bg-panel px-1.5 text-[11px] font-medium text-muted shadow-[0_1px_2px_rgb(0_0_0/0.15)] transition hover:border-faint/60 hover:text-fg"
          >
            <Icon name="runTo" size={11} class="text-ok" />
            Run to here
          </button>
        )}
        {canEdit && (
          <button
            type="button"
            onClick={() => onDelete(action.id)}
            title="Delete this action from the flow (Delete). Undo brings it back."
            aria-label={`Delete ${verbOf(action)} ${subjectOf(action)}`}
            class="grid size-6 place-items-center rounded-md border border-line-strong bg-panel text-muted shadow-[0_1px_2px_rgb(0_0_0/0.15)] transition hover:border-bad/50 hover:text-bad"
          >
            <Icon name="trash" size={12} />
          </button>
        )}
      </span>
      {failure && <FailureDetails failure={failure} file={file} onEnlarge={onEnlarge} />}
    </li>
  );
}

// A check after the action: an auto check, which its ✕ drops and ↺ brings back, or one the person chose, which ✕
// deletes.
function ExpectRow({ expect, canEdit, onDrop }: { expect: Expect; canEdit: boolean; onDrop(drop: boolean): void }) {
  const dropped = !!expect.dropped;
  const action = !expect.auto ? 'Delete this check' : dropped ? 'Bring this auto check back' : 'Drop this auto check';
  return (
    <li class="group/expect flex min-w-0 items-center gap-1.5 rounded-md py-px pr-0.5 pl-1.5 text-[11.5px] leading-5 hover:bg-canvas/70">
      <Icon name={expect.auto ? 'tick' : 'checkCircle'} size={12} class={cls('shrink-0', dropped ? 'text-faint/70' : expect.auto ? 'text-ok/75' : 'text-ok', expect.auto && 'stroke-[2.4]')} />
      <span dir="auto" class={cls('min-w-0 truncate', dropped ? 'text-faint line-through decoration-faint/60' : expect.auto ? 'text-muted' : 'text-fg')} title={expect.of ? `${expect.text} · ${expect.of}` : expect.text}>
        {expect.text}
        {expect.of && <span class="text-faint"> · <span dir="ltr" class="font-mono text-[10.5px]">{expect.of}</span></span>}
      </span>
      <span class={cls('ml-auto shrink-0 rounded px-1 text-[10px] leading-4 font-medium', expect.auto ? 'text-faint' : 'bg-ok/12 text-ok')}>
        {dropped ? 'dropped' : expect.auto ? 'auto' : 'check'}
      </span>
      {canEdit && (
        <button
          type="button"
          onClick={() => onDrop(!dropped)}
          title={action}
          aria-label={action}
          class={cls(
            'grid size-5 shrink-0 place-items-center rounded opacity-0 transition group-hover/expect:opacity-100 focus-visible:opacity-100',
            dropped ? 'text-accent-fg hover:bg-accent/12' : 'text-muted hover:bg-bad/12 hover:text-bad',
          )}
        >
          <Icon name={dropped ? 'restore' : 'cross'} size={11} class="stroke-[2.4]" />
        </button>
      )}
    </li>
  );
}

// The step's method name, renamed in place: Enter or leaving the field renames it, Escape keeps it, and an empty name
// gives it back the recorder's.
function StepName({ name, disabled, onRename }: { name: string; disabled: boolean; onRename(name: string): void }) {
  const [draft, setDraft] = useState<string | null>(null);
  if (draft === null)
    return (
      <button
        type="button"
        disabled={disabled}
        onClick={() => setDraft(name.replace(/Async$/, ''))}
        title="Rename the step's method"
        class="group/name -mx-1 inline-flex min-w-0 items-center gap-1 rounded px-1 font-mono transition hover:bg-raised hover:text-fg disabled:pointer-events-none"
      >
        <span class="truncate">{name}</span>
        <Icon name="pencil" size={11} class="shrink-0 opacity-0 transition group-hover/name:opacity-100" />
      </button>
    );
  const commit = () => {
    const next = draft.trim();
    setDraft(null);
    if (next !== name.replace(/Async$/, '')) onRename(next);
  };
  return (
    <label class="-mx-1 flex min-w-0 items-center rounded border border-accent/50 bg-panel px-1 font-mono text-fg">
      <span class="sr-only">Step name</span>
      <input
        autoFocus
        value={draft}
        spellcheck={false}
        onInput={(event) => setDraft(event.currentTarget.value)}
        onBlur={commit}
        onKeyDown={(event) => {
          if (event.key === 'Enter') event.currentTarget.blur();
          if (event.key === 'Escape') setDraft(null);
        }}
        class="min-w-[6ch] bg-transparent outline-none [field-sizing:content]"
      />
      <span class="text-faint">Async</span>
    </label>
  );
}

// Auto checks on or off for the whole flow, as --no-auto-checks.
function Switch({ on, disabled, onChange }: { on: boolean; disabled: boolean; onChange(on: boolean): void }) {
  return (
    <button
      type="button"
      role="switch"
      aria-checked={on}
      disabled={disabled}
      onClick={() => onChange(!on)}
      title={on ? 'Auto checks are on: after each action the flow checks what it did. Click to turn them all off, as --no-auto-checks.' : 'Auto checks are off. Click to have the flow check after each action what it did.'}
      class="-mr-1 inline-flex h-6 items-center gap-1.5 rounded-md px-1.5 text-[11px] font-medium text-muted transition hover:bg-raised hover:text-fg disabled:pointer-events-none disabled:opacity-50"
    >
      Auto checks
      <span class={cls('relative h-3.5 w-6 rounded-full transition', on ? 'bg-ok' : 'bg-line-strong')}>
        <span class={cls('absolute top-0.5 size-2.5 rounded-full bg-white shadow-[0_1px_1px_rgb(0_0_0/0.25)] transition-all', on ? 'left-3' : 'left-0.5')} />
      </span>
    </button>
  );
}

// How a step's actions went: all passed, one failed, or one runs.
function StepMark({ actions, test }: { actions: Action[]; test: TestRun }) {
  const states = actions.map((action) => test.steps[action.id]?.state);
  if (states.includes('failed'))
    return (
      <span class="ml-auto inline-flex shrink-0 items-center text-bad" title="A step in it failed">
        <Icon name="cross" size={11} class="stroke-[2.6]" />
      </span>
    );
  if (states.includes('running')) return <Spinner size={9} class="ml-auto text-accent-fg" />;
  if (states.length > 0 && states.every((state) => state === 'passed'))
    return (
      <span class="ml-auto inline-flex shrink-0 items-center text-ok" title="Every step in it passed">
        <Icon name="tick" size={11} class="stroke-[2.6]" />
      </span>
    );
  return null;
}

const effectIcons: Record<string, IconName> = {
  dialogOpened: 'dialog',
  dialogClosed: 'closed',
  urlChanged: 'url',
  alert: 'alert',
  tabSelected: 'tab',
  popup: 'popup',
  jsDialog: 'alert',
};

function Badge({ effect }: { effect: Effect }) {
  return (
    <span class="inline-flex max-w-full min-w-0 items-center gap-1 rounded-md border border-line bg-raised px-1.5 py-px text-[11px] text-muted">
      <Icon name={effectIcons[effect.kind] ?? 'dialog'} size={11} class="shrink-0" />
      <span class="shrink-0">{effectLabel(effect)}</span>
      {effect.text && (
        <span dir={effect.kind === 'urlChanged' ? 'ltr' : 'auto'} class={cls('min-w-0 truncate text-fg/80', effect.kind === 'urlChanged' && 'font-mono text-[10.5px]')}>
          {effect.text}
        </span>
      )}
    </span>
  );
}

function Empty({ status }: { status: Status }) {
  const [icon, title, text]: [IconName, string, string] =
    status === 'login'
      ? ['key', 'Logging in…', 'Your login flow is running. The recording starts on the page it leaves the app on.']
      : status === 'paused'
        ? ['pause', 'Recording paused', 'Resume to record your first step. What you do in the app meanwhile is not recorded.']
        : ['pointer', 'Click something in the app to record the first step', 'Each action shows up here as it is recorded, with the code it writes and how its element is found.'];
  return (
    <div class="flex h-full flex-col items-center justify-center px-8 pb-12 text-center">
      <div class="relative mb-5 grid size-12 place-items-center rounded-lg border border-line-strong bg-raised text-accent-fg">
        <span class="absolute -inset-1.5 rounded-[11px] border border-accent/25" />
        <Icon name={icon} size={20} />
      </div>
      <p class="text-[13.5px] font-medium text-fg">{title}</p>
      <p class="mt-1.5 max-w-[32ch] text-[12.5px] leading-relaxed text-muted">{text}</p>
    </div>
  );
}
