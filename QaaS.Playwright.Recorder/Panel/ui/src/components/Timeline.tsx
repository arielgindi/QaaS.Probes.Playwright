import { useEffect, useRef, useState } from 'preact/hooks';
import { isGoing, useNow } from '../test';
import { candidateKind, candidateStatus, cls, detailOf, effectLabel, plural, shortReasons, subjectOf, verbOf } from '../text';
import type { Action, Effect, Expect, PanelState, Rerecord, Status, Step, TestFailure, TestRun, TestStep } from '../types';
import { Icon, type IconName, kindIcon } from './icons';
import { PaneHeader } from './PaneHeader';
import { FailureDetails, StepStatus } from './TestRun';
import { Button, focusOnMount, EmptyState, Kbd, Spinner, Tag } from './ui';

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
  /** Applies a suggestion of the recorder's: leaves out what it says. */
  onApplySuggestion(key: string): void;
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
  // What is tried shows as it is tried, under the steps.
  const triedCount = tried?.length;
  useEffect(() => {
    if (triedCount !== undefined) list.current?.querySelector('[aria-label="Tried"] li:last-child, [aria-label="Tried"]')?.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
  }, [triedCount]);
  // The step running stays in view as the test goes through the flow.
  useEffect(() => {
    if (running) list.current?.querySelector(`[data-action="${CSS.escape(running)}"]`)?.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
  }, [running]);

  const failure = test?.failure;
  return (
    <section aria-label="Steps" class="area-steps flex min-h-0 flex-col border-r border-line bg-panel">
      <PaneHeader title="Steps" count={count || undefined}>
        <Switch on={autoChecks} disabled={!canEdit} onChange={props.onAutoChecks} />
      </PaneHeader>
      <div ref={list} class="min-h-0 flex-1 overflow-y-auto px-2 pb-6" onMouseLeave={() => onHover(null)}>
        {failure && !failure.action && test.state === 'failed' && (
          <div class="px-1 pt-3">
            <FailureDetails failure={failure} file={file} onEnlarge={onEnlarge} />
          </div>
        )}
        {here === null && <RecordingHere rerecord={rerecord!} />}
        {steps.length === 0
          ? here === undefined && <Empty status={status} />
          : steps.map((step, index) => (
              <div key={step.actions[0]?.id ?? step.name} class={index === 0 ? 'pt-2' : 'pt-4'}>
                <div class="group/step flex min-w-0 items-center gap-1.5 px-2 pb-1.5">
                  <StepName name={step.name} disabled={!canEdit || step.actions.length === 0} onRename={(name) => props.onRenameStep(step.actions[0].id, name)} />
                  {step.dialog && (
                    <span dir="auto" title={`In the dialog “${step.dialog}”`} class="flex min-w-0 shrink items-center gap-1 text-meta text-faint">
                      <Icon name="dialog" size={11} class="shrink-0" />
                      <span class="truncate">{step.dialog}</span>
                    </span>
                  )}
                  <span class="ml-auto flex shrink-0 items-center gap-0.5">
                    {canRerecord && step.actions.length > 0 && (
                      <span class="hidden items-center gap-0.5 group-hover/step:flex group-focus-within/step:flex">
                        <Button
                          size="sm"
                          iconOnly
                          icon="rerecord"
                          label={`Re-record ${step.name}: replay the flow up to it in the app, then do it again`}
                          onClick={() => ask(step, step.actions[0], true, false)}
                        />
                        <Button
                          size="sm"
                          iconOnly
                          icon="recordAfter"
                          label={`Record after ${step.name}: replay the flow through it in the app, then record what goes after it`}
                          onClick={() => ask(step, step.actions.at(-1)!, true, true)}
                        />
                      </span>
                    )}
                    {test && <StepMark actions={step.actions} test={test} />}
                  </span>
                </div>
                {asking?.step && step.actions.some((action) => action.id === asking.action) && (
                  <Confirm asking={asking} login={props.login} onConfirm={confirm} onCancel={() => setAsking(null)} />
                )}
                <ol class="relative space-y-0.5 before:absolute before:top-5 before:bottom-5 before:left-[20px] before:w-px before:bg-line">
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
                      onApplySuggestion={props.onApplySuggestion}
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
            ))}
        {tried && <Tried tried={tried} rerecord={rerecord} canAdd={canEdit} onHover={onHover} onAdd={props.onAddTried} />}
        {notes.length > 0 && (
          <div class="mx-1 mt-5 rounded-lg border border-warn/30 bg-warn/[0.06] px-3 py-2.5 text-meta text-warn">
            <div class="mb-1 flex items-center gap-1.5 font-semibold">
              <Icon name="warn" size={13} />
              Check by hand
            </div>
            <ul class="space-y-1 pl-5">
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
    <li role="separator" class="flex items-center gap-2 py-2 pl-2 text-meta text-info">
      <span class="flex-1 border-t border-dashed border-info/40" />
      {label}
      <span class="flex-1 border-t border-dashed border-info/40" />
    </li>
  );
}

// Where what is recorded in a part goes, while it records.
function RecordingHere({ rerecord }: { rerecord: Rerecord }) {
  return (
    <li role="separator" class="flex list-none items-center gap-2 py-2 pl-[15px] text-meta font-medium text-rec">
      <span class="size-2 shrink-0 animate-rec rounded-full bg-rec text-rec" />
      <span class="shrink-0">{rerecord.fresh.length > 0 ? 'Recording here' : rerecord.after ? `Recording after ${rerecord.part}` : `Re-recording ${rerecord.part} here`}</span>
      <span class="flex-1 border-t border-dashed border-rec/40" />
    </li>
  );
}

// What recording a part again does in the app, before it does it.
function Confirm({ asking, login, onConfirm, onCancel }: { asking: Asking; login?: string; onConfirm(): void; onCancel(): void }) {
  const part = asking.loop ? 'loop' : 'step';
  return (
    <div
      role="dialog"
      aria-label={asking.after ? 'Record after' : 'Re-record'}
      data-own-keys
      onKeyDown={(event) => event.key === 'Escape' && onCancel()}
      class="animate-pop-in mx-1 my-1.5 rounded-xl border border-line-strong bg-raised p-3 text-body shadow-e2"
    >
      <p class="font-semibold text-fg">
        {asking.after ? 'Record after ' : 'Re-record '}
        <span dir="auto">{asking.label}</span>?
      </p>
      <p class="mt-1 text-meta text-muted">
        The app goes back to the start{login ? `, logs in with ${login},` : ''} and replays the flow {asking.after ? 'through' : 'up to'} this {part}, really
        doing it in your app. {asking.after ? 'Then record what goes after it.' : 'Then do it again; the steps after it stay.'}
      </p>
      {asking.loop && (
        <p class="mt-1.5 flex gap-1.5 text-meta text-warn">
          <Icon name="warn" size={12} class="mt-0.5 shrink-0" />
          {asking.after ? 'What you record goes after all its repeats.' : `All ${plural(asking.loop, 'repeat')} of the loop are recorded again.`}
        </p>
      )}
      <div class="mt-3 flex items-center gap-1.5">
        <Button variant="primary" size="sm" icon={asking.after ? 'recordAfter' : 'rerecord'} autoFocus onClick={onConfirm}>
          {asking.after ? 'Record after it' : 'Re-record'}
        </Button>
        <Button size="sm" onClick={onCancel}>
          Cancel
        </Button>
      </div>
    </div>
  );
}

// While trying: what the recorder would write for each action tried, with Add to flow.
function Tried({ tried, rerecord, canAdd, onHover, onAdd }: { tried: Action[]; rerecord?: Rerecord; canAdd: boolean; onHover(id: string | null): void; onAdd(id: string): void }) {
  const where = rerecord?.phase === 'recording' ? 'where the part records' : 'at the end of the flow';
  return (
    <section aria-label="Tried" class="mx-1 mt-5 rounded-xl border border-try/30 bg-try/[0.04] p-1.5">
      <div class="flex items-center gap-1.5 px-1.5 pt-0.5 pb-1.5 text-meta font-semibold text-try">
        <Icon name="flask" size={12} />
        Tried, not recorded
        {tried.length > 0 && <span class="ml-auto font-normal text-muted tabular-nums">{tried.length}</span>}
      </div>
      {tried.length === 0 ? (
        <p class="px-1.5 pb-1.5 text-meta text-muted">Click, type or choose in the app: each action shows here as it would be written. Add to flow puts one {where}.</p>
      ) : (
        <ol class="space-y-1.5">
          {tried.map((action) => (
            <TriedCard key={action.id} action={action} canAdd={canAdd} where={where} onHover={onHover} onAdd={() => onAdd(action.id)} />
          ))}
        </ol>
      )}
    </section>
  );
}

function TriedCard({ action, canAdd, where, onHover, onAdd }: { action: Action; canAdd: boolean; where: string; onHover(id: string | null): void; onAdd(): void }) {
  const detail = detailOf(action);
  const checks = (action.expects ?? []).filter((expect) => expect.auto);
  const others = action.candidates.filter((candidate) => candidate.index !== action.picked);
  const why = [shortReasons(action.reasons), action.note].filter(Boolean);
  return (
    <li data-tried={action.id} onMouseEnter={() => onHover(action.id)} class="group/tried rounded-lg border border-line bg-panel px-2.5 py-2 text-meta transition hover:border-line-strong">
      <div class="flex min-w-0 items-baseline gap-1.5">
        <Icon name={kindIcon(action.kind)} size={12} class="shrink-0 self-center text-faint" />
        <span class="shrink-0 text-muted">{verbOf(action)}</span>
        <span dir="auto" class="min-w-0 truncate text-body font-medium text-fg">
          {subjectOf(action)}
        </span>
        <span class="ml-auto self-center">
          <Tag tone="try">tried</Tag>
        </span>
      </div>
      {detail && <Detail action={action} detail={detail} />}
      {action.omitted ? (
        <p class="mt-1.5 text-muted">{action.omitted}</p>
      ) : (
        <>
          <div class="mt-1.5 flex min-w-0 items-baseline gap-1.5">
            <span class="shrink-0 text-faint">would use</span>
            <code dir="ltr" class="min-w-0 truncate font-mono text-fg/90" title={action.locator}>
              {action.locator ?? 'nothing finds it'}
            </code>
          </div>
          {why.length > 0 && (
            <p dir="ltr" class="mt-0.5 truncate font-mono text-tag text-faint" title={why.join('  ·  ')}>
              {why.join('  ·  ')}
            </p>
          )}
          {action.checks.map((check) => (
            <p key={check} class="mt-1 flex gap-1.5 text-warn">
              <Icon name="warn" size={11} class="mt-0.5 shrink-0" />
              {check}
            </p>
          ))}
          {others.length > 0 && (
            <details class="group/others mt-1">
              <summary class="flex cursor-pointer list-none items-center gap-1 text-faint transition select-none hover:text-fg">
                <Icon name="chevron" size={11} class="transition group-open/others:rotate-90" />
                {plural(others.length, 'other locator')}
              </summary>
              <ul aria-label="Other locators" class="mt-1 space-y-px pl-4">
                {others.map((candidate) => {
                  const status = candidateStatus(candidate);
                  return (
                    <li key={candidate.index} title={`${candidateKind(candidate)}: ${candidate.code}`} class="flex min-w-0 items-baseline gap-1.5">
                      <code dir="ltr" class="min-w-0 flex-1 truncate font-mono text-muted">{candidate.label}</code>
                      <span class={cls('shrink-0 tabular-nums', status.ok ? 'text-ok' : 'text-faint')}>
                        {status.ok ? '✓' : '✗'} {status.text}
                      </span>
                    </li>
                  );
                })}
              </ul>
            </details>
          )}
          {(checks.length > 0 || action.settings.length > 0) && (
            <div class="mt-1.5 flex flex-wrap gap-1">
              {checks.map((expect) => (
                <span key={expect.key} class="inline-flex max-w-full min-w-0 items-center gap-1 rounded-sm bg-ok/10 px-1.5 text-ok">
                  <Icon name="tick" size={11} class="shrink-0 stroke-[2.4]" />
                  <span dir="auto" class="truncate">{expect.text}</span>
                </span>
              ))}
              {action.settings.map((setting) => (
                <span key={setting.role} class="inline-flex max-w-full min-w-0 items-center gap-1 rounded-sm bg-raised px-1.5 text-muted" title="The setting of the flow it would type or choose">
                  <span class="shrink-0 text-faint">setting</span>
                  <span class="max-w-[55%] min-w-0 shrink-0 truncate font-mono text-fg/90">{setting.name}</span>
                  <span dir="auto" class="min-w-0 truncate">= {setting.value}</span>
                </span>
              ))}
            </div>
          )}
        </>
      )}
      {canAdd && !action.omitted && (
        <Button size="sm" variant="secondary" icon="plus" onClick={onAdd} title={`Add this action to the flow, ${where}, as the recorder would have written it`} class="mt-2">
          Add to flow
        </Button>
      )}
    </li>
  );
}

interface CardProps extends Pick<Props, 'file' | 'canRun' | 'canEdit' | 'canRerecord' | 'onSelect' | 'onHover' | 'onRunTo' | 'onEnlarge' | 'onDelete' | 'onDropCheck' | 'onApplySuggestion'> {
  action: Action;
  selected: boolean;
  test?: TestStep;
  failure?: TestFailure;
  now: number;
  /** Recorded in the part being recorded again. */
  fresh: boolean;
  onRerecord(after: boolean): void;
}

// An effect an auto check of the action already says, in its list of checks.
const checkedBy: Record<string, (key: string) => boolean> = {
  dialogOpened: (key) => key === 'opened',
  dialogClosed: (key) => key.startsWith('closed:'),
  urlChanged: (key) => key === 'url',
  alert: (key) => key.startsWith('alert:'),
  tabSelected: (key) => key === 'selected',
};

function Card({ action, selected, test, failure, file, now, canRun, canEdit, canRerecord, fresh, onRerecord, onSelect, onHover, onRunTo, onEnlarge, onDelete, onDropCheck, onApplySuggestion }: CardProps) {
  const detail = detailOf(action);
  const state = test?.state;
  const expects = action.expects ?? [];
  // What the page did, unless a check below says it already.
  const effects = action.effects.filter((effect) => !expects.some((expect) => checkedBy[effect.kind]?.(expect.key)));
  const tools = canRun || canRerecord || canEdit;
  return (
    <li
      data-action={action.id}
      onMouseEnter={() => onHover(action.id)}
      class={cls(
        'group/card relative animate-step-in rounded-lg border transition-colors',
        state === 'skipped' && 'opacity-60',
        state === 'failed'
          ? selected
            ? 'border-bad/55 bg-bad/10'
            : 'border-bad/35 bg-bad/[0.06] hover:bg-bad/[0.09]'
          : state === 'running'
            ? 'border-accent-fg/45 bg-accent/[0.08]'
            : selected
              ? 'border-accent-fg/40 bg-accent/[0.09]'
              : fresh
                ? 'border-rec/30 bg-rec/[0.05] hover:bg-rec/[0.08]'
                : 'border-transparent hover:bg-raised',
      )}
    >
      <button type="button" aria-current={selected} onClick={() => onSelect(action.id)} class="flex w-full min-w-0 gap-2.5 rounded-lg py-2 pr-2.5 pl-2 text-left">
        <KindMark kind={action.kind} state={state} warn={action.checks.length > 0} selected={selected} />
        <span class="block min-w-0 flex-1">
          <span class={cls('flex min-w-0 items-baseline gap-1.5', tools && 'group-hover/card:pr-24 group-focus-within/card:pr-24')}>
            <span class="shrink-0 text-meta text-muted">{verbOf(action)}</span>
            <span dir="auto" class="min-w-0 truncate text-body font-medium text-fg">
              {subjectOf(action)}
            </span>
            {(action.added || test || fresh) && (
              <span class={cls('ml-auto flex shrink-0 items-center gap-1.5 self-center', tools && 'group-hover/card:hidden group-focus-within/card:hidden')}>
                {fresh && <Tag tone="rec" title="Recorded now, in place of the part recorded again">new</Tag>}
                {action.added && <Tag tone="accent" title="Added in the panel">added</Tag>}
                {test && <StepStatus step={test} now={now} />}
              </span>
            )}
          </span>
          {detail && <Detail action={action} detail={detail} />}
          {action.locator && (
            <code dir="ltr" title={action.locator} class="mt-0.5 block truncate font-mono text-meta text-faint">
              {action.locator}
            </code>
          )}
          {effects.length > 0 && (
            <span class="mt-1 flex flex-wrap gap-x-2.5 gap-y-0.5 text-meta text-muted">
              {effects.map((effect) => (
                <EffectLine key={effect.kind + effect.text} effect={effect} />
              ))}
            </span>
          )}
          {action.checks.map((check) => (
            <span key={check} class="mt-1.5 flex gap-1.5 rounded-md bg-warn/10 px-1.5 py-1 text-meta text-warn">
              <Icon name="warn" size={12} class="mt-0.5 shrink-0" />
              <span>{check}</span>
            </span>
          ))}
        </span>
      </button>
      {(action.suggestions ?? []).map((suggestion) => (
        <div key={suggestion.key} data-suggestion={suggestion.key} class="mx-1.5 mb-1.5 ml-[38px] flex items-start gap-2 rounded-md border border-dashed border-accent/35 bg-accent/[0.06] px-2 py-1.5 text-meta text-muted">
          <Icon name="wand" size={13} class="mt-0.5 shrink-0 text-accent-fg" />
          <span class="min-w-0 flex-1">{suggestion.text}</span>
          <button
            type="button"
            disabled={!canEdit}
            onClick={() => onApplySuggestion(suggestion.key)}
            title="Leaves it out of the flow; Undo brings it back"
            class="shrink-0 rounded-md bg-accent/12 px-2 py-0.5 font-medium text-accent-fg transition hover:bg-accent/20 disabled:opacity-50"
          >
            {suggestion.apply}
          </button>
        </div>
      ))}
      {expects.length > 0 && (
        <ul aria-label="Checks" class="space-y-px pr-1.5 pb-1.5 pl-[38px]">
          {expects.map((expect) => (
            <ExpectRow key={expect.key} expect={expect} canEdit={canEdit} onDrop={(drop) => (expect.auto ? onDropCheck(action.id, expect.key, drop) : onDelete(expect.key))} />
          ))}
        </ul>
      )}
      {tools && (
        <span class="absolute top-1.5 right-1.5 hidden items-center gap-px rounded-md border border-line-strong bg-panel p-px shadow-e2 group-focus-within/card:flex group-hover/card:flex">
          {canRun && (
            <Button size="sm" iconOnly icon="runTo" label="Run to here: test the flow up to this step, and this step too (Ctrl+Shift+Enter on the selected step)" onClick={() => onRunTo(action.id)} />
          )}
          {canRerecord && (
            <>
              <Button size="sm" iconOnly icon="rerecord" label="Re-record: replay the flow up to this step in the app, then do it again" onClick={() => onRerecord(false)} />
              <Button size="sm" iconOnly icon="recordAfter" label="Record after this: replay the flow through this step in the app, then record what goes after it" onClick={() => onRerecord(true)} />
            </>
          )}
          {canEdit && (
            <Button size="sm" iconOnly icon="trash" label={`Delete ${verbOf(action)} ${subjectOf(action)} (Delete). Undo brings it back.`} onClick={() => onDelete(action.id)} class="hover:text-bad" />
          )}
        </span>
      )}
      {failure && (
        <div class="px-2 pb-2">
          <FailureDetails failure={failure} file={file} onEnlarge={onEnlarge} />
        </div>
      )}
    </li>
  );
}

// The action's kind, in a small square on the line of the steps, coloured by how it went.
function KindMark({ kind, state, warn, selected }: { kind: string; state?: TestStep['state']; warn: boolean; selected: boolean }) {
  return (
    <span
      class={cls(
        'relative z-[1] mt-px grid size-6 shrink-0 place-items-center rounded-md border bg-panel transition-colors',
        state === 'failed'
          ? 'border-bad/60 text-bad'
          : state === 'passed'
            ? 'border-ok/45 text-ok'
            : state === 'running'
              ? 'border-accent-fg/70 text-accent-fg'
              : warn
                ? 'border-warn/50 text-warn'
                : selected
                  ? 'border-accent-fg/50 text-accent-fg'
                  : 'border-line-strong text-muted',
      )}
    >
      {state === 'running' ? <Spinner size={11} /> : <Icon name={kindIcon(kind)} size={13} />}
    </span>
  );
}

// What was typed, chosen or pressed: "= Printer jammed", "→ Software", "key Enter".
function Detail({ action, detail }: { action: Action; detail: string }) {
  const lead = action.kind === 'choose' ? '→' : action.kind === 'press' ? 'key' : action.kind === 'fill' ? '=' : 'with';
  return (
    <span class="mt-0.5 flex min-w-0 items-baseline gap-1.5 text-meta">
      <span class="shrink-0 text-faint">{lead}</span>
      <span dir="auto" class="min-w-0 truncate text-fg/85">
        {detail}
      </span>
    </span>
  );
}

// A check after the action: an auto check, which its ✕ drops and ↺ brings back, or one the person chose, which ✕
// deletes.
function ExpectRow({ expect, canEdit, onDrop }: { expect: Expect; canEdit: boolean; onDrop(drop: boolean): void }) {
  const dropped = !!expect.dropped;
  const action = !expect.auto ? 'Delete this check' : dropped ? 'Bring this auto check back' : 'Drop this auto check';
  const kind = !expect.auto ? 'Your check' : dropped ? 'Auto check, dropped' : 'Auto check';
  return (
    <li class="group/expect flex min-h-6 min-w-0 items-center gap-1.5 rounded-md pr-0.5 pl-1.5 text-meta hover:bg-canvas/70">
      <span title={kind} class="grid size-3.5 shrink-0 place-items-center">
        <Icon name={expect.auto ? 'tick' : 'checkCircle'} size={12} class={cls(dropped ? 'text-faint' : 'text-ok', expect.auto && 'stroke-[2.4]')} />
      </span>
      <span dir="auto" class={cls('min-w-0 flex-1 truncate', dropped ? 'text-faint line-through decoration-faint/60' : expect.auto ? 'text-muted' : 'text-fg')} title={`${kind}: ${expect.text}${expect.of ? ` · ${expect.of}` : ''}`}>
        {expect.text}
        {expect.of && <span class="text-faint"> · <span dir="ltr" class="font-mono">{expect.of}</span></span>}
      </span>
      {dropped && <span class="shrink-0 text-tag text-faint">dropped</span>}
      {canEdit && (
        <button
          type="button"
          onClick={() => onDrop(!dropped)}
          title={action}
          aria-label={action}
          class={cls(
            'grid size-5 shrink-0 place-items-center rounded-sm opacity-0 transition group-hover/expect:opacity-100 focus-visible:opacity-100',
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
        class="group/name -mx-1 inline-flex min-w-0 shrink items-center gap-1 rounded-sm px-1 font-mono text-meta font-medium text-muted transition hover:bg-raised hover:text-fg disabled:pointer-events-none"
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
    <label class="-mx-1 flex min-w-0 items-center rounded-sm border border-accent-fg/50 bg-panel px-1 font-mono text-meta text-fg">
      <span class="sr-only">Step name</span>
      <input
        ref={focusOnMount}
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
      class="-mr-1 inline-flex h-6 items-center gap-1.5 rounded-md px-1.5 text-meta font-medium text-muted transition hover:bg-raised hover:text-fg disabled:pointer-events-none disabled:opacity-50"
    >
      Auto checks
      <span class={cls('relative h-3.5 w-6 rounded-full transition-colors', on ? 'bg-ok' : 'bg-line-strong')}>
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
      <span class="inline-flex size-5 shrink-0 items-center justify-center text-bad" title="A step in it failed">
        <Icon name="cross" size={11} class="stroke-[2.6]" />
      </span>
    );
  if (states.includes('running')) return <Spinner size={10} class="mx-[5px] text-accent-fg" />;
  if (states.length > 0 && states.every((state) => state === 'passed'))
    return (
      <span class="inline-flex size-5 shrink-0 items-center justify-center text-ok" title="Every step in it passed">
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

function EffectLine({ effect }: { effect: Effect }) {
  return (
    <span class="inline-flex max-w-full min-w-0 items-center gap-1" title={effect.text ? `${effectLabel(effect)}: ${effect.text}` : effectLabel(effect)}>
      <Icon name={effectIcons[effect.kind] ?? 'dialog'} size={11} class="shrink-0 text-faint" />
      <span class="shrink-0">{effectLabel(effect)}</span>
      {effect.text && (
        <span dir={effect.kind === 'urlChanged' ? 'ltr' : 'auto'} class={cls('min-w-0 truncate text-fg/80', effect.kind === 'urlChanged' && 'font-mono')}>
          {effect.text}
        </span>
      )}
    </span>
  );
}

function Empty({ status }: { status: Status }) {
  if (status === 'login')
    return (
      <div class="space-y-3 px-2 pt-4" aria-label="Logging in">
        {[0, 1, 2].map((row) => (
          <div key={row} class="flex gap-2.5 px-2 opacity-70">
            <span class="skeleton size-6 shrink-0" />
            <span class="flex-1 space-y-1.5 pt-0.5">
              <span class="skeleton block h-3 w-3/5" />
              <span class="skeleton block h-2.5 w-4/5" />
            </span>
          </div>
        ))}
        <p class="px-2 pt-2 text-center text-meta text-muted">Your login flow is running. The recording starts on the page it leaves the app on.</p>
      </div>
    );
  if (status === 'paused')
    return (
      <EmptyState icon="pause" title="Recording paused" class="h-full pb-12">
        Resume (<Kbd>F8</Kbd>) to record your first step.
      </EmptyState>
    );
  return (
    <EmptyState icon="pointer" title="Click something in the app to record the first step" class="h-full pb-12">
      <p>Each action shows up here as it is recorded, with the code it writes.</p>
      <p class="mt-3 flex flex-wrap items-center justify-center gap-x-2.5 gap-y-1 text-meta">
        <span class="flex items-center gap-1"><Kbd>F6</Kbd> check</span>
        <span class="flex items-center gap-1"><Kbd>F7</Kbd> try</span>
        <span class="flex items-center gap-1"><Kbd>F8</Kbd> pause</span>
        <span class="flex items-center gap-1"><Kbd>Ctrl K</Kbd> all commands</span>
      </p>
    </EmptyState>
  );
}
