import type { ComponentChildren } from 'preact';
import { useEffect, useState } from 'preact/hooks';
import { candidateKind, candidateStatus, cls, effectLabel, isUnusable, subjectOf, verbOf } from '../text';
import { duration } from '../test';
import type { Action, Candidate, PickedElement, Setting, TestStep } from '../types';
import { Icon, type IconName } from './icons';
import { PaneHeader } from './PaneHeader';

interface Props {
  action?: Action;
  disabled: boolean;
  /** How the action went in the last test run. */
  test?: TestStep;
  /** Whether "Run to here" can start. */
  canRun: boolean;
  onChoose(action: Action, candidate: number | null): void;
  onHighlight(action: string | null, candidate?: number): void;
  onRunTo(id: string): void;
  onEdit(action: Action, setting: Setting, change: SettingChange): void;
}

/** A change of a setting: its name, its default, back to the value recorded, unique per run, or a date from today. */
export type SettingChange = { name?: string; value?: string; reset?: boolean; unique?: boolean; relative?: boolean };

/** The selected action: how its element is found, every other way to find it, and the values it records. */
export function Inspector({ action, disabled, test, canRun, onChoose, onHighlight, onRunTo, onEdit }: Props) {
  return (
    <aside aria-label="Inspector" class="area-inspector flex min-h-0 flex-col bg-panel">
      <PaneHeader title="Inspector">
        {action?.lines && (
          <span class="font-mono text-[11px] text-faint">
            {action.lines.first === action.lines.last ? `line ${action.lines.first}` : `lines ${action.lines.first}–${action.lines.last}`}
          </span>
        )}
        {action?.lines && (
          <button
            type="button"
            disabled={!canRun}
            onClick={() => onRunTo(action.id)}
            title="Run the flow up to this step, and this step too (Ctrl+Shift+Enter)"
            class="-mr-1.5 inline-flex h-6 items-center gap-1 rounded-md px-1.5 text-[11.5px] font-medium text-muted transition hover:bg-raised hover:text-fg disabled:pointer-events-none disabled:opacity-40"
          >
            <Icon name="runTo" size={11} class="text-ok" />
            Run to here
          </button>
        )}
      </PaneHeader>
      {action ? (
        <div class="min-h-0 flex-1 overflow-y-auto px-4 pt-4 pb-6">
          <Heading kind={verbOf(action)} title={subjectOf(action)}>
            {action.kind !== 'navigate' && action.kind !== 'newTab' && action.value && <Chip>{action.value}</Chip>}
            {action.dialog && <InDialog title={action.dialog} />}
            {action.page !== 'page' && <span>in {action.page}</span>}
            {action.added && <Chip tone="accent">Added in the panel</Chip>}
            {test && <TestChip step={test} />}
          </Heading>
          {action.checks.length > 0 && <Checks checks={action.checks} />}
          {action.candidates.length > 0 && (
            <Section
              title="Locator"
              aside={
                action.chosen !== undefined &&
                action.auto !== undefined && <LinkButton disabled={disabled} onClick={() => onChoose(action, null)}>Use the recorder's choice</LinkButton>
              }
            >
              <Hint>Every way the recorder found to locate the element. Click one to use it; point at one to see what it finds in the app.</Hint>
              <Locators
                candidates={action.candidates}
                picked={action.picked}
                auto={action.auto}
                chosen={action.chosen}
                disabled={disabled}
                onChoose={(index) => onChoose(action, index)}
                onHighlight={(index) => onHighlight(index === null ? null : action.id, index ?? undefined)}
              />
            </Section>
          )}
          {action.settings.length > 0 && (
            <Section title="Recorded values">
              <Hint>Settings of the flow, with these as defaults; change them per case in the YAML. Click a name or a value to change it.</Hint>
              <dl class="divide-y divide-line overflow-hidden rounded-lg border border-line">
                {action.settings.map((setting) => (
                  <SettingRow key={setting.role + setting.name} setting={setting} disabled={disabled} onEdit={(change) => onEdit(action, setting, change)} />
                ))}
              </dl>
            </Section>
          )}
          {action.effects.length > 0 && (
            <Section title="What it did">
              <ul class="space-y-1.5 text-[12.5px]">
                {action.effects.map((effect) => (
                  <li key={effect.kind + effect.text} class="flex min-w-0 items-baseline gap-2">
                    <span class="w-[92px] shrink-0 text-muted">{effectLabel(effect)}</span>
                    {effect.text && (
                      <span dir={effect.kind === 'urlChanged' ? 'ltr' : 'auto'} class={cls('min-w-0 break-words text-fg', effect.kind === 'urlChanged' && 'font-mono text-[11.5px]')}>
                        {effect.text}
                      </span>
                    )}
                  </li>
                ))}
              </ul>
            </Section>
          )}
        </div>
      ) : (
        <Empty icon="pointer" title="No step selected" text="Select a step to see how its element is found, and every other way to find it." />
      )}
    </aside>
  );
}

// A setting: its name and value, each changed in place, and how the flow may type it: unique per run, or a date as days
// from today, which a date typed near the day of the recording suggests.
function SettingRow({ setting, disabled, onEdit }: { setting: Setting; disabled: boolean; onEdit(change: SettingChange): void }) {
  const number = setting.type === 'int' || setting.type === 'decimal';
  const valid = (text: string) =>
    setting.type === 'int' ? /^-?(0|[1-9]\d{0,8})$/.test(text) : setting.type === 'decimal' ? /^-?(0|[1-9]\d{0,14})(\.\d{1,10})?$/.test(text) : true;
  const { relative, suggest } = setting;
  return (
    <div class="bg-canvas px-2.5 py-1.5">
      <div class="flex min-w-0 items-baseline gap-3">
        <dt class="min-w-0 max-w-[62%] shrink-0">
          <InPlace
            label="Setting name"
            text={setting.name}
            mono
            class="tk-key"
            disabled={disabled || !setting.renamable}
            title={setting.renamable ? 'Rename the setting' : 'A loop names its list itself'}
            valid={(text) => /[A-Za-z]/.test(text)}
            onCommit={(name) => onEdit({ name })}
          />
        </dt>
        <dd class="min-w-0 flex-1 text-right">
          <InPlace
            label={`Value of ${setting.name}`}
            text={setting.text ?? ''}
            shown={setting.value}
            mono
            align="right"
            class={number ? 'tk-num' : 'tk-str'}
            disabled={disabled || setting.type === 'bool'}
            title={setting.type === 'bool' ? 'Change it per case in the YAML' : relative ? 'The days from today' : 'Change the default'}
            valid={valid}
            onCommit={(value) => onEdit({ value })}
          />
        </dd>
      </div>
      {setting.recorded !== undefined && (
        <Option icon="pencil">
          Recorded as <span dir="auto" class="font-mono text-fg/80">{setting.recorded}</span>
          <LinkButton disabled={disabled} onClick={() => onEdit({ reset: true })}>Use it</LinkButton>
        </Option>
      )}
      {setting.unique !== undefined && (
        <button
          type="button"
          role="checkbox"
          aria-checked={setting.unique}
          disabled={disabled}
          onClick={() => onEdit({ unique: !setting.unique })}
          title="The flow types it with its run's stamp appended, so a name it creates never repeats from run to run. A check of it reads the same value."
          class="group mt-1.5 flex w-full min-w-0 items-center gap-2 rounded-md text-left text-[11.5px] text-muted transition hover:text-fg disabled:opacity-50"
        >
          <span class={cls('grid size-3.5 shrink-0 place-items-center rounded border transition', setting.unique ? 'border-accent bg-accent text-on-accent' : 'border-line-strong group-hover:border-faint')}>
            {setting.unique && <Icon name="tick" size={10} class="stroke-[3]" />}
          </span>
          <span class="shrink-0 font-medium">Unique per run</span>
          <span dir="auto" class="min-w-0 truncate font-mono text-[10.5px] text-faint">
            {setting.text} {stampExample()}
          </span>
        </button>
      )}
      {relative && (
        <Option icon="calendar" tone="accent">
          {fromToday(relative.days)}, as <span class="font-mono">{relative.format}</span>
          <LinkButton disabled={disabled} onClick={() => onEdit({ relative: false })}>Type the date</LinkButton>
        </Option>
      )}
      {suggest && (
        <div class="mt-1.5 flex items-center gap-2 rounded-md border border-dashed border-accent/35 bg-accent/[0.06] px-2 py-1.5 text-[11.5px] leading-snug text-muted">
          <Icon name="calendar" size={13} class="shrink-0 text-accent-fg" />
          <span class="min-w-0 flex-1">{suggest.days === 0 ? 'The day it was recorded.' : `${days(suggest.days)} ${suggest.days > 0 ? 'after' : 'before'} the day it was recorded.`}</span>
          <button
            type="button"
            disabled={disabled}
            onClick={() => onEdit({ relative: true })}
            title={`The setting becomes the days from today (${suggest.days}), and the flow types the date in ${suggest.format}`}
            class="shrink-0 rounded-md bg-accent/12 px-2 py-0.5 font-medium text-accent-fg transition hover:bg-accent/20 disabled:opacity-50"
          >
            {suggest.days === 0 ? 'Type it as today' : `Type it as ${Math.abs(suggest.days)} ${suggest.days > 0 ? 'days from today' : 'days ago'}`}
          </button>
        </div>
      )}
    </div>
  );
}

// "3 days", "1 day".
function days(count: number): string {
  return `${Math.abs(count)} day${Math.abs(count) === 1 ? '' : 's'}`;
}

// "3 days from today", "2 days before today", "Today".
function fromToday(count: number): string {
  return count === 0 ? 'Today' : `${days(count)} ${count > 0 ? 'from' : 'before'} today`;
}

// A stamp as a run appends it: the month, day and time, and three characters of its own.
function stampExample(): string {
  const now = new Date();
  const two = (value: number) => String(value).padStart(2, '0');
  return `${two(now.getMonth() + 1)}${two(now.getDate())}-${two(now.getHours())}${two(now.getMinutes())}${two(now.getSeconds())}-k7q`;
}

function Option({ icon, tone, children }: { icon: IconName; tone?: 'accent'; children: ComponentChildren }) {
  return (
    <div class={cls('mt-1.5 flex min-w-0 items-center gap-1.5 text-[11.5px] leading-snug', tone === 'accent' ? 'text-accent-fg' : 'text-muted')}>
      <Icon name={icon} size={12} class="shrink-0" />
      <span class="min-w-0 flex-1 truncate">{children}</span>
    </div>
  );
}

// A text changed in place: a click opens it, Enter or leaving it commits what is valid, Escape keeps it as it was.
function InPlace(props: {
  label: string;
  text: string;
  shown?: string;
  mono?: boolean;
  align?: 'right';
  class: string;
  disabled: boolean;
  title: string;
  valid(text: string): boolean;
  onCommit(text: string): void;
}) {
  const [draft, setDraft] = useState<string | null>(null);
  if (draft === null)
    return (
      <button
        type="button"
        disabled={props.disabled}
        onClick={() => setDraft(props.text)}
        title={props.disabled ? props.title : `${props.title}: ${props.shown ?? props.text}`}
        dir="auto"
        class={cls(
          // As wide as its column with the margins it reaches past it by.
          '-mx-1 max-w-[calc(100%+0.5rem)] truncate rounded px-1 font-mono text-[11.5px] transition enabled:hover:bg-raised enabled:hover:ring-1 enabled:hover:ring-line-strong',
          props.class,
        )}
      >
        {props.shown ?? props.text}
      </button>
    );
  const ok = props.valid(draft);
  const commit = () => {
    setDraft(null);
    if (ok && draft !== props.text) props.onCommit(draft);
  };
  return (
    <input
      autoFocus
      aria-label={props.label}
      aria-invalid={!ok}
      dir="auto"
      value={draft}
      spellcheck={false}
      onInput={(event) => setDraft(event.currentTarget.value)}
      onBlur={commit}
      onKeyDown={(event) => {
        if (event.key === 'Enter') event.currentTarget.blur();
        if (event.key === 'Escape') setDraft(null);
      }}
      class={cls(
        'h-6 w-full min-w-0 rounded border bg-panel px-1.5 font-mono text-[11.5px] text-fg outline-none',
        props.align === 'right' && 'text-right',
        ok ? 'border-accent/60' : 'border-bad/70',
      )}
    />
  );
}

interface ElementProps {
  element?: PickedElement;
  /** The step "Use for this step" changes, and the one an added step follows. */
  step?: Action;
  last: boolean;
  disabled: boolean;
  onChoose(candidate: number | null): void;
  onHighlight(candidate: number | null): void;
  onUse(action: Action): void;
  onAddClick(after?: string): void;
  onAddFill(value: string, after?: string): void;
}

/** The element picked in the Elements view: every way to locate it, and the steps it can become. */
export function ElementInspector({ element, step, last, disabled, onChoose, onHighlight, onUse, onAddClick, onAddFill }: ElementProps) {
  const [fill, setFill] = useState<string | null>(null);
  const [done, setDone] = useState<string | null>(null);
  useEffect(() => {
    setFill(null);
    setDone(null);
  }, [element?.node]);
  useEffect(() => {
    if (!done) return;
    const timer = window.setTimeout(() => setDone(null), 2400);
    return () => window.clearTimeout(timer);
  }, [done]);

  if (!element)
    return (
      <aside aria-label="Inspector" class="area-inspector flex min-h-0 flex-col bg-panel">
        <PaneHeader title="Inspector" />
        <Empty icon="elements" title="No element picked" text="Pick an element in the tree to see every way to locate it, and to make it a step." />
      </aside>
    );

  const candidate = element.candidates.find((option) => option.index === element.picked);
  const after = step && !last ? step.id : undefined;
  const where = after ? `after “${verbOf(step!)} ${subjectOf(step!)}”` : 'at the end';
  const copy = async () => {
    if (candidate && (await copyText(candidate.code))) setDone('Copied the locator');
  };
  return (
    <aside aria-label="Inspector" class="area-inspector flex min-h-0 flex-col bg-panel">
      <PaneHeader title="Inspector">
        <span class="font-mono text-[11px] tracking-normal text-faint normal-case">{element.page}</span>
      </PaneHeader>
      <div class="min-h-0 flex-1 overflow-y-auto px-4 pt-4 pb-6">
        <Heading kind="Element" title={element.name || `<${element.tag}>`}>
          <Chip mono>{`<${element.tag}>`}</Chip>
          {element.dialog && <InDialog title={element.dialog} />}
        </Heading>
        {element.gone && (
          <Note icon="freeze" tone="info">
            Not on the page now. Its locators were found when you froze the page; a step on it replays once the step before it
            opens it again.
          </Note>
        )}
        <div class="mt-4 rounded-xl border border-line bg-canvas p-3">
          <div class="flex flex-wrap gap-1.5">
            <ActionButton primary disabled={disabled || !step} title={step ? undefined : 'Select a step on the left first'} onClick={() => {
              if (!step) return;
              onUse(step);
              setDone(`“${verbOf(step)} ${subjectOf(step)}” now acts on it`);
            }}>
              <Icon name="target" size={14} />
              Use for this step
            </ActionButton>
            <ActionButton disabled={disabled} onClick={() => {
              onAddClick(after);
              setDone(`Added a click ${where}`);
            }}>
              <Icon name="click" size={14} />
              Add click
            </ActionButton>
            <ActionButton disabled={disabled || !element.fillable} title={element.fillable ? undefined : 'Only a text field can be filled'} onClick={() => setFill(fill === null ? '' : null)} pressed={fill !== null}>
              <Icon name="fill" size={14} />
              Add fill…
            </ActionButton>
            <ActionButton disabled={!candidate} onClick={copy} title="Copy the locator's C#">
              <Icon name="copy" size={14} />
              Copy locator
            </ActionButton>
          </div>
          {fill !== null && (
            <form
              class="mt-2.5 flex gap-1.5"
              onSubmit={(event) => {
                event.preventDefault();
                onAddFill(fill, after);
                setFill(null);
                setDone(`Added a fill ${where}`);
              }}
            >
              <input
                autoFocus
                dir="auto"
                value={fill}
                placeholder="The text to type"
                onInput={(event) => setFill(event.currentTarget.value)}
                onKeyDown={(event) => event.key === 'Escape' && setFill(null)}
                class="h-8 min-w-0 flex-1 rounded-lg border border-line-strong bg-panel px-2.5 text-[12.5px] text-fg outline-none placeholder:text-faint focus:border-accent/60"
              />
              <ActionButton primary submit>Add</ActionButton>
            </form>
          )}
          <p class={cls('mt-2.5 text-[11.5px] leading-snug', done ? 'text-ok' : 'text-faint')} role="status">
            {done ? (
              <span class="inline-flex items-center gap-1">
                <Icon name="tick" size={12} class="stroke-[2.4]" />
                {done}
              </span>
            ) : step ? (
              <>
                “Use for this step” changes “{verbOf(step)} {subjectOf(step)}”. A step you add goes {where}, written as a
                recorded one.
              </>
            ) : (
              <>A step you add goes at the end; select a step on the left to put it after that one.</>
            )}
          </p>
        </div>
        {element.candidates.length > 0 ? (
          <Section
            title="Locator"
            aside={element.chosen !== undefined && <LinkButton disabled={disabled} onClick={() => onChoose(null)}>Use the recorder's choice</LinkButton>}
          >
            <Hint>The recorder's choice comes first, as for a recorded step. Click one to use another.</Hint>
            <Locators
              candidates={element.candidates}
              picked={element.picked}
              auto={element.auto}
              chosen={element.chosen}
              disabled={disabled}
              onChoose={onChoose}
              onHighlight={element.gone ? () => undefined : onHighlight}
            />
          </Section>
        ) : (
          <Note icon="warn" tone="warn">Nothing locates this element.</Note>
        )}
      </div>
    </aside>
  );
}

interface LocatorsProps {
  candidates: Candidate[];
  picked?: number;
  auto?: number;
  chosen?: number;
  disabled: boolean;
  onChoose(candidate: number | null): void;
  onHighlight(candidate: number | null): void;
}

function Locators({ candidates, picked, auto, chosen, disabled, onChoose, onHighlight }: LocatorsProps) {
  return (
    <div role="radiogroup" aria-label="Locator" class="space-y-1.5" onMouseLeave={() => onHighlight(null)}>
      {candidates.map((candidate) => {
        const isPicked = candidate.index === picked;
        const unusable = isUnusable(candidate);
        const status = candidateStatus(candidate);
        // Back to the recorder's own choice, rather than pinning it.
        const choose = () => !isPicked && !unusable && !disabled && onChoose(candidate.index === auto ? null : candidate.index);
        return (
          <button
            key={candidate.index}
            type="button"
            role="radio"
            aria-checked={isPicked}
            aria-disabled={unusable || disabled}
            title={unusable ? `The flow cannot use it: ${candidate.rejection}` : undefined}
            onClick={choose}
            onMouseEnter={() => onHighlight(candidate.index)}
            onFocus={() => onHighlight(candidate.index)}
            class={cls(
              'group block w-full rounded-lg border px-3 py-2.5 text-left transition',
              isPicked
                ? 'border-accent/55 bg-accent/9 shadow-[0_0_0_3px_color-mix(in_oklab,var(--accent)_10%,transparent)]'
                : 'border-line bg-panel hover:border-line-strong hover:bg-raised',
              unusable ? 'cursor-not-allowed opacity-55' : !isPicked && 'cursor-pointer',
            )}
          >
            <span class="flex min-w-0 items-center gap-2">
              <span class={cls('grid size-3.5 shrink-0 place-items-center rounded-full border transition', isPicked ? 'border-accent bg-accent' : 'border-line-strong group-hover:border-faint')}>
                {isPicked && <span class="size-1.5 rounded-full bg-on-accent" />}
              </span>
              <span class="min-w-0 truncate text-[12.5px] font-medium text-fg">{candidateKind(candidate)}</span>
              {candidate.index === auto && <Tag title="The recorder's own choice">Auto</Tag>}
              {isPicked && chosen !== undefined && <Tag accent title="Your choice">Chosen</Tag>}
              <span
                class={cls(
                  'ml-auto inline-flex shrink-0 items-center gap-1 rounded-full px-2 py-px text-[11px] font-medium tabular-nums',
                  status.ok ? 'bg-ok/12 text-ok' : 'bg-bad/12 text-bad',
                )}
              >
                <Icon name={status.ok ? 'tick' : 'cross'} size={10} class="stroke-[2.6]" />
                {status.text}
              </span>
            </span>
            <code dir="ltr" class="mt-2 block font-mono text-[11.5px] leading-[1.6] break-words text-muted">
              {candidate.code}
            </code>
            {candidate.problem && (
              <span class="mt-1.5 flex gap-1.5 text-[11.5px] leading-snug text-warn">
                <Icon name="warn" size={12} class="mt-px shrink-0" />
                {candidate.problem}
              </span>
            )}
          </button>
        );
      })}
    </div>
  );
}

function Heading({ kind, title, children }: { kind: string; title: string; children?: ComponentChildren }) {
  return (
    <div>
      <div class="text-[11px] font-medium tracking-[0.06em] text-faint uppercase">{kind}</div>
      <div dir="auto" class="mt-1 text-[15px] leading-snug font-semibold tracking-[-0.01em] break-words text-fg">
        {title}
      </div>
      {children && <div class="mt-2 flex flex-wrap items-center gap-1.5 text-[12px] text-muted">{children}</div>}
    </div>
  );
}

function TestChip({ step }: { step: TestStep }) {
  const [tone, text] =
    step.state === 'passed'
      ? ['border-ok/30 bg-ok/10 text-ok', `Passed${step.ms !== undefined ? ` in ${duration(step.ms)}` : ''}`]
      : step.state === 'failed'
        ? ['border-bad/35 bg-bad/10 text-bad', 'Failed in the test']
        : step.state === 'running'
          ? ['border-accent/35 bg-accent/10 text-accent-fg', 'Running']
          : ['border-line bg-canvas text-faint', 'Not run in the test'];
  return (
    <span class={cls('inline-flex items-center gap-1 rounded-md border px-1.5 py-px text-[11.5px] font-medium', tone)}>
      {step.state === 'passed' && <Icon name="tick" size={11} class="stroke-[2.6]" />}
      {step.state === 'failed' && <Icon name="cross" size={11} class="stroke-[2.6]" />}
      {text}
    </span>
  );
}

function InDialog({ title }: { title: string }) {
  return (
    <span class="inline-flex max-w-full min-w-0 items-center gap-1">
      <Icon name="dialog" size={12} class="shrink-0 text-faint" />
      <span class="shrink-0">in</span>
      <span dir="auto" class="truncate text-fg/85">
        {title}
      </span>
    </span>
  );
}

function Chip({ children, mono, tone }: { children: ComponentChildren; mono?: boolean; tone?: 'accent' }) {
  return (
    <span
      dir="auto"
      class={cls(
        'max-w-full truncate rounded-md border px-1.5 py-px',
        tone === 'accent' ? 'border-accent/30 bg-accent/10 text-accent-fg' : 'border-line bg-canvas text-fg/85',
        mono && 'font-mono text-[11.5px]',
      )}
    >
      {children}
    </span>
  );
}

function Checks({ checks }: { checks: string[] }) {
  return (
    <div class="mt-3.5 space-y-1 rounded-lg border border-warn/30 bg-warn/8 p-2.5 text-[12px] leading-snug text-warn">
      {checks.map((check) => (
        <div key={check} class="flex gap-1.5">
          <Icon name="warn" size={13} class="mt-px shrink-0" />
          <span>{check}</span>
        </div>
      ))}
    </div>
  );
}

function Note({ icon, tone, children }: { icon: IconName; tone: 'info' | 'warn'; children: ComponentChildren }) {
  return (
    <div class={cls('mt-3.5 flex gap-2 rounded-lg border p-2.5 text-[12px] leading-snug', tone === 'info' ? 'border-info/30 bg-info/8 text-info' : 'border-warn/30 bg-warn/8 text-warn')}>
      <Icon name={icon} size={13} class="mt-px shrink-0" />
      <span>{children}</span>
    </div>
  );
}

function Section({ title, aside, children }: { title: string; aside?: ComponentChildren; children: ComponentChildren }) {
  return (
    <section class="mt-6">
      <div class="mb-2 flex items-center text-[11px] font-medium tracking-[0.06em] text-faint uppercase">
        {title}
        <span class="ml-auto tracking-normal normal-case">{aside}</span>
      </div>
      {children}
    </section>
  );
}

function Hint({ children }: { children: ComponentChildren }) {
  return <p class="mb-2.5 text-[12px] leading-relaxed text-muted">{children}</p>;
}

function LinkButton({ disabled, onClick, children }: { disabled: boolean; onClick(): void; children: ComponentChildren }) {
  return (
    <button type="button" disabled={disabled} onClick={onClick} class="rounded px-1 text-[11.5px] font-medium text-accent-fg transition hover:underline disabled:opacity-50">
      {children}
    </button>
  );
}

function ActionButton(props: { primary?: boolean; submit?: boolean; pressed?: boolean; disabled?: boolean; title?: string; onClick?(): void; children: ComponentChildren }) {
  return (
    <button
      type={props.submit ? 'submit' : 'button'}
      disabled={props.disabled}
      title={props.title}
      aria-pressed={props.pressed}
      onClick={props.onClick}
      class={cls(
        'inline-flex h-8 items-center gap-1.5 rounded-lg px-2.5 text-[12.5px] font-medium transition disabled:pointer-events-none disabled:opacity-45',
        props.primary
          ? 'bg-accent text-on-accent shadow-[0_1px_0_0_rgb(255_255_255/0.12)_inset] hover:brightness-110'
          : props.pressed
            ? 'border border-accent/45 bg-accent/10 text-fg'
            : 'border border-line-strong bg-panel text-fg hover:border-faint/60 hover:bg-raised',
      )}
    >
      {props.children}
    </button>
  );
}

function Tag({ accent, title, children }: { accent?: boolean; title: string; children: ComponentChildren }) {
  return (
    <span
      title={title}
      class={cls('shrink-0 rounded px-1.5 text-[10.5px] leading-4 font-medium', accent ? 'bg-accent/15 text-accent-fg' : 'border border-line text-muted')}
    >
      {children}
    </span>
  );
}

function Empty({ icon, title, text }: { icon: IconName; title: string; text: string }) {
  return (
    <div class="flex flex-1 flex-col items-center justify-center gap-2 px-8 pb-8 text-center">
      <span class="mb-1 grid size-9 place-items-center rounded-lg border border-line bg-raised text-faint">
        <Icon name={icon} size={16} />
      </span>
      <p class="text-[13px] font-medium text-fg">{title}</p>
      <p class="max-w-[30ch] text-[12.5px] leading-relaxed text-muted">{text}</p>
    </div>
  );
}

// The clipboard API needs a page of its own origin; the panel's has none, so the copy command is the way.
async function copyText(text: string): Promise<boolean> {
  try {
    await navigator.clipboard.writeText(text);
    return true;
  } catch {
    const area = document.createElement('textarea');
    area.value = text;
    area.style.position = 'fixed';
    area.style.opacity = '0';
    document.body.appendChild(area);
    area.select();
    const copied = document.execCommand('copy');
    area.remove();
    return copied;
  }
}
