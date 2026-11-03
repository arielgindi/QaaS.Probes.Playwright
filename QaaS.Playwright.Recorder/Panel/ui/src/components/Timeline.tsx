import { useEffect, useRef, useState } from 'preact/hooks';
import { isGoing, useNow } from '../test';
import { candidateKind, candidateStatus, cls, detailOf, effectLabel, plural, subjectOf, verbOf } from '../text';
import type { Action, Effect, Expect, PanelState, Rerecord, Status, Step, TestFailure, TestRun, TestStep } from '../types';
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
  /** A part of the flow recorded again, or recorded after, and how it goes. */
  rerecord?: Rerecord;
  /** While trying: the actions tried, as the recorder would write them. */
  tried?: Action[];
  /** The login flow the recording runs after, which a replay runs first. */
  login?: string;
  /** Whether a step can be recorded again now: nothing runs, and no part is recorded again. */
  canRerecord: boolean;
  onRerecord(action: string, step: boolean, after: boolean, label: string): void;
  onAddTried(action: string): void;
}

/** Recording a part again, or after it, which the person confirms first: what it does in the app. */
interface Asking {
  action: string;
  step: boolean;
  after: boolean;
  label: string;
  /** The part is a loop, of this many repeats. */
  loop?: number;
}

/**
 * The steps of the flow, each a method, with a card per action, the checks after it, and how each went in the last test
 * run. A step is renamed in place, an action deleted from its card, and an auto check dropped with its ✕.
 */
export function Timeline(props: Props) {
  const { steps, count, notes, selected, status, test, continued, file, canRun, canEdit, autoChecks, onSelect, onHover, onRunTo, onEnlarge } = props;
  const { rerecord, tried, canRerecord } = props;
  const list = useRef<HTMLDivElement>(null);
  const [asking, setAsking] = useState<Asking | null>(null);
  useEffect(() => {
    if (!canRerecord) setAsking(null);
  }, [canRerecord]);
  const ask = (step: Step, action: Action, whole: boolean, after: boolean) => {
    // A loop's repeats share the lines of its body.
    const repeats = step.actions.filter((other) => other.lines && other.lines.first === action.lines?.first).length;
    const loop = repeats > 1;
    setAsking({ action: action.id, step: whole, after, label: whole || loop ? step.name : `${verbOf(action)} “${subjectOf(action)}”`, loop: loop ? repeats : undefined });
  };
  const confirm = () => {
    if (asking) props.onRerecord(asking.action, asking.step, asking.after, asking.label);
    setAsking(null);
  };
  // Where what is recorded in the part goes: after the last action recorded in it, else where the part was.
  const recording = rerecord?.phase === 'recording';
  const fresh = new Set(recording ? rerecord.fresh : []);
  const here = recording ? (rerecord.fresh.at(-1) ?? rerecord.at ?? null) : undefined;
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
        {here === null && <RecordingHere rerecord={rerecord!} />}
        {steps.length === 0 ? (
          here === undefined && <Empty status={status} />
        ) : (
          steps.map((step) => (
            <div key={step.actions[0]?.id ?? step.name} class="pt-3">
              <div class="group/step relative flex min-w-0 items-center gap-2 px-2 pb-1.5 text-[11px] text-faint">
                <StepName name={step.name} disabled={!canEdit || step.actions.length === 0} onRename={(name) => props.onRenameStep(step.actions[0].id, name)} />
                {step.dialog && (
                  <span dir="auto" class="min-w-0 truncate rounded border border-line px-1.5 leading-4">
                    {step.dialog}
                  </span>
                )}
                {test && <StepMark actions={step.actions} test={test} />}
                {canRerecord && step.actions.length > 0 && (
                  <span class="absolute -top-0.5 right-1.5 hidden items-center gap-1 group-hover/step:flex group-focus-within/step:flex">
                    <PartButton icon="rerecord" label="Re-record" title={`Re-record ${step.name}: replay the flow up to it in the app, then do it again`} onClick={() => ask(step, step.actions[0], true, false)} />
                    <PartButton icon="recordAfter" label="Record after" title={`Record after ${step.name}: replay the flow through it in the app, then record what goes after it`} onClick={() => ask(step, step.actions.at(-1)!, true, true)} />
                  </span>
                )}
              </div>
              {asking?.step && step.actions.some((action) => action.id === asking.action) && (
                <Confirm asking={asking} login={props.login} onConfirm={confirm} onCancel={() => setAsking(null)} />
              )}
              <ol class="relative space-y-1 before:absolute before:top-4 before:bottom-4 before:left-[22px] before:w-px before:bg-line-strong">
                {step.actions.flatMap((action) => [
                  <Card
                    key={action.id}
                    action={action}
                    fresh={fresh.has(action.id)}
                    canRerecord={canRerecord}
                    onRerecord={(after) => ask(step, action, false, after)}
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
                  asking && !asking.step && asking.action === action.id && (
                    <li key="asking">
                      <Confirm asking={asking} login={props.login} onConfirm={confirm} onCancel={() => setAsking(null)} />
                    </li>
                  ),
                  action.id === continued?.after && <Continued key="continued" label={continued.label} />,
                  action.id === here && <RecordingHere key="here" rerecord={rerecord!} />,
                ])}
              </ol>
            </div>
          ))
        )}
        {tried && <Tried tried={tried} rerecord={rerecord} canAdd={canEdit} onHover={onHover} onAdd={props.onAddTried} />}
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

// Where what is recorded in a part goes, while it records.
function RecordingHere({ rerecord }: { rerecord: Rerecord }) {
  return (
    <li role="separator" class="flex list-none items-center gap-2 py-1.5 pl-3 text-[11px] font-medium text-rec">
      <span class="size-2 shrink-0 animate-rec rounded-full bg-rec" />
      <span class="shrink-0">{rerecord.fresh.length > 0 ? 'Recording here' : rerecord.after ? `Recording after ${rerecord.part}` : `Re-recording ${rerecord.part} here`}</span>
      <span class="flex-1 border-t border-dashed border-rec/50" />
    </li>
  );
}

function PartButton({ icon, label, title, onClick }: { icon: IconName; label: string; title: string; onClick(): void }) {
  return (
    <button
      type="button"
      onClick={(event) => {
        event.stopPropagation();
        onClick();
      }}
      title={title}
      aria-label={title}
      class="inline-flex h-6 items-center gap-1 rounded-md border border-line-strong bg-panel px-1.5 text-[11px] font-medium text-muted shadow-[0_1px_2px_rgb(0_0_0/0.15)] transition hover:border-faint/60 hover:text-fg"
    >
      <Icon name={icon} size={12} class="text-rec" />
      <span class="max-[1100px]:sr-only">{label}</span>
    </button>
  );
}

// What recording a part again does in the app, before it does it.
function Confirm({ asking, login, onConfirm, onCancel }: { asking: Asking; login?: string; onConfirm(): void; onCancel(): void }) {
  const from = login ? `logs in with ${login}, ` : '';
  return (
    <div role="dialog" aria-label={asking.after ? 'Record after' : 'Re-record'} data-own-keys class="mx-1 my-1.5 rounded-lg border border-rec/35 bg-rec/[0.06] p-2.5 text-[12px] leading-snug text-fg">
      <p class="font-medium">
        {asking.after ? 'Record after ' : 'Re-record '}
        <span dir="auto">{asking.label}</span>?
      </p>
      <p class="mt-1 text-muted">
        The recorded window goes back to the start of the flow, {from}and replays it {asking.after ? `through this ${asking.loop ? 'loop' : 'step'}` : `up to this ${asking.loop ? 'loop' : 'step'}`},
        really doing what it does in your app; nothing is recorded meanwhile. {asking.after ? 'Then what you do goes here, before the steps after it.' : 'Then do it again: it replaces what it had, and the steps after it stay.'}
      </p>
      {asking.loop && !asking.after && <p class="mt-1 text-warn">It is a loop: re-recording one repeat re-records the whole loop, all {plural(asking.loop, 'repeat')} of it.</p>}
      {asking.loop && asking.after && <p class="mt-1 text-warn">It is a loop: what you record goes after all its repeats.</p>}
      <div class="mt-2 flex items-center gap-1.5">
        <button
          type="button"
          autoFocus
          onClick={onConfirm}
          onKeyDown={(event) => event.key === 'Escape' && onCancel()}
          class="inline-flex h-7 items-center gap-1.5 rounded-md bg-rec px-2.5 text-[11.5px] font-semibold text-white transition hover:brightness-110"
        >
          <Icon name={asking.after ? 'recordAfter' : 'rerecord'} size={12} />
          {asking.after ? 'Record after it' : 'Re-record'}
        </button>
        <button type="button" onClick={onCancel} class="inline-flex h-7 items-center rounded-md px-2 text-[11.5px] font-medium text-muted transition hover:bg-raised hover:text-fg">
          Cancel
        </button>
      </div>
    </div>
  );
}

// While trying: what the recorder would write for each action tried, dimmed, with Add to flow.
function Tried({ tried, rerecord, canAdd, onHover, onAdd }: { tried: Action[]; rerecord?: Rerecord; canAdd: boolean; onHover(id: string | null): void; onAdd(id: string): void }) {
  const where = rerecord?.phase === 'recording' ? 'where the part records' : 'at the end of the flow';
  return (
    <section aria-label="Tried" class="mx-1 mt-4 rounded-lg border border-info/25 bg-info/[0.04] p-1.5">
      <div class="flex items-center gap-1.5 px-1.5 pt-0.5 text-[11px] font-medium text-info">
        <Icon name="flask" size={12} />
        Tried, not recorded
        {tried.length > 0 && <span class="ml-auto font-normal tabular-nums text-faint">{tried.length}</span>}
      </div>
      <p class="px-1.5 pb-1.5 text-[11px] text-faint">Add to flow puts one {where}.</p>
      {tried.length === 0 ? (
        <p class="px-1.5 pb-1 text-[12px] leading-snug text-muted">Click, type or choose in the app: each action shows here as the recorder would write it.</p>
      ) : (
        <ol class="space-y-1.5">
          {tried.map((action) => (
            <TriedCard key={action.id} action={action} canAdd={canAdd} onHover={onHover} onAdd={() => onAdd(action.id)} />
          ))}
        </ol>
      )}
    </section>
  );
}

function TriedCard({ action, canAdd, onHover, onAdd }: { action: Action; canAdd: boolean; onHover(id: string | null): void; onAdd(): void }) {
  const detail = detailOf(action);
  const checks = (action.expects ?? []).filter((expect) => expect.auto);
  const others = action.candidates.filter((candidate) => candidate.index !== action.picked);
  const why = [...(action.reasons ?? []), ...(action.note ? [action.note] : [])];
  return (
    <li data-tried={action.id} onMouseEnter={() => onHover(action.id)} class="group/tried rounded-lg border border-dashed border-line-strong bg-panel/60 px-2.5 py-2 text-[12px] transition hover:bg-panel">
      <div class="opacity-75 transition group-hover/tried:opacity-100">
        <div class="flex min-w-0 items-baseline gap-1.5">
          <Icon name={kindIcon(action.kind)} size={12} class="shrink-0 self-center text-faint" />
          <span class="shrink-0 text-muted">{verbOf(action)}</span>
          <span dir="auto" class="min-w-0 truncate font-medium text-fg">
            {subjectOf(action)}
          </span>
          <span class="ml-auto shrink-0 self-center rounded border border-info/35 px-1 text-[10px] leading-4 font-medium text-info">tried</span>
        </div>
        {detail && (
          <div class="mt-0.5 flex min-w-0 items-baseline gap-1.5">
            <span class="shrink-0 text-faint">{action.kind === 'choose' ? 'option' : action.kind === 'press' ? 'key' : 'with'}</span>
            <span dir="auto" class="min-w-0 truncate text-muted">{detail}</span>
          </div>
        )}
        {action.omitted ? (
          <p class="mt-1.5 text-[11.5px] leading-snug text-faint">{action.omitted}</p>
        ) : (
          <>
            <div class="mt-1.5 flex min-w-0 items-baseline gap-1.5">
              <span class="shrink-0 text-[11px] text-faint">would use</span>
              <code dir="ltr" class="min-w-0 truncate rounded-md border border-line bg-canvas px-1.5 py-px font-mono text-[11px] text-fg/85" title={action.locator}>
                {action.locator ?? 'nothing finds it'}
              </code>
            </div>
            {why.length > 0 && (
              <p dir="ltr" class="mt-1 font-mono text-[10.5px] leading-snug break-words text-faint">
                {why.join('  ·  ')}
              </p>
            )}
            {action.checks.map((check) => (
              <p key={check} class="mt-1 flex gap-1.5 text-[11px] leading-snug text-warn">
                <Icon name="warn" size={11} class="mt-px shrink-0" />
                {check}
              </p>
            ))}
            {others.length > 0 && (
              <ul aria-label="Other locators" class="mt-1.5 space-y-px">
                {others.map((candidate) => {
                  const status = candidateStatus(candidate);
                  return (
                    <li key={candidate.index} title={`${candidateKind(candidate)}: ${candidate.code}`} class="flex min-w-0 items-baseline gap-1.5 text-[11px]">
                      <code dir="ltr" class="min-w-0 flex-1 truncate font-mono text-[10.5px] text-muted">{candidate.label}</code>
                      <span class={cls('shrink-0 tabular-nums', status.ok ? 'text-ok/90' : 'text-faint')}>
                        {status.ok ? '✓' : '✗'} {status.text}
                      </span>
                    </li>
                  );
                })}
              </ul>
            )}
            {(checks.length > 0 || action.settings.length > 0) && (
              <div class="mt-1.5 flex flex-wrap gap-1">
                {checks.map((expect) => (
                  <span key={expect.key} class="inline-flex max-w-full min-w-0 items-center gap-1 rounded-md border border-ok/25 bg-ok/[0.06] px-1.5 py-px text-[11px] text-ok/90">
                    <Icon name="tick" size={11} class="shrink-0 stroke-[2.4]" />
                    <span dir="auto" class="truncate">{expect.text}</span>
                  </span>
                ))}
                {action.settings.map((setting) => (
                  <span key={setting.role} class="inline-flex max-w-full min-w-0 items-center gap-1 rounded-md border border-line bg-raised px-1.5 py-px text-[11px] text-muted" title="The setting of the flow it would type or choose">
                    <span class="shrink-0 text-faint">setting</span>
                    <span class="shrink-0 font-mono text-[10.5px] text-fg/85">{setting.name}</span>
                    <span dir="auto" class="min-w-0 truncate">= {setting.value}</span>
                  </span>
                ))}
              </div>
            )}
          </>
        )}
      </div>
      {canAdd && !action.omitted && (
        <button
          type="button"
          onClick={onAdd}
          title="Add this action to the flow, as the recorder would have written it"
          class="mt-2 inline-flex h-6 items-center gap-1 rounded-md border border-accent/35 bg-accent/10 px-2 text-[11px] font-medium text-accent-fg transition hover:bg-accent/18"
        >
          <Icon name="plus" size={11} class="stroke-[2.4]" />
          Add to flow
        </button>
      )}
    </li>
  );
}

interface CardProps extends Pick<Props, 'file' | 'canRun' | 'canEdit' | 'canRerecord' | 'onSelect' | 'onHover' | 'onRunTo' | 'onEnlarge' | 'onDelete' | 'onDropCheck'> {
  action: Action;
  selected: boolean;
  test?: TestStep;
  failure?: TestFailure;
  now: number;
  /** Recorded in the part being recorded again. */
  fresh: boolean;
  onRerecord(after: boolean): void;
}

function Card({ action, selected, test, failure, file, now, canRun, canEdit, canRerecord, fresh, onRerecord, onSelect, onHover, onRunTo, onEnlarge, onDelete, onDropCheck }: CardProps) {
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
              : fresh
                ? 'border-rec/30 bg-rec/[0.05] hover:bg-rec/[0.08]'
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
          {(action.added || test || fresh) && (
            <span class={cls('ml-auto flex shrink-0 items-center gap-1.5 self-center', (canRun || canEdit) && 'group-hover/card:invisible')}>
              {fresh && (
                <span title="Recorded now, in place of the part recorded again" class="rounded border border-rec/30 bg-rec/10 px-1 text-[10px] leading-4 font-medium text-rec">
                  new
                </span>
              )}
              {action.added && (
                <span title="Added in the panel" class="rounded border border-accent/30 bg-accent/10 px-1 text-[10px] leading-4 font-medium text-accent-fg">
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
        {canRerecord && (
          <>
            <CardButton icon="rerecord" title="Re-record: replay the flow up to this step in the app, then do it again" onClick={() => onRerecord(false)} />
            <CardButton icon="recordAfter" title="Record after this: replay the flow through this step in the app, then record what goes after it" onClick={() => onRerecord(true)} />
          </>
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

function CardButton({ icon, title, onClick }: { icon: IconName; title: string; onClick(): void }) {
  return (
    <button
      type="button"
      onClick={onClick}
      title={title}
      aria-label={title}
      class="grid size-6 place-items-center rounded-md border border-line-strong bg-panel text-muted shadow-[0_1px_2px_rgb(0_0_0/0.15)] transition hover:border-rec/50 hover:text-rec"
    >
      <Icon name={icon} size={12} />
    </button>
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
