import type { ComponentChildren } from 'preact';
import { useEffect, useRef, useState } from 'preact/hooks';
import { tokenizeCSharp } from '../syntax/csharp';
import { candidateKind, candidateStatus, cls, effectLabel, isUnusable, subjectOf, verbOf } from '../text';
import { duration } from '../test';
import type { Action, Candidate, PickedElement, Setting, TestStep } from '../types';
import { toast } from '../toast';
import { Icon, type IconName } from './icons';
import { PaneHeader } from './PaneHeader';
import { Button, focusOnMount, EmptyState, Kbd, Link, SectionTitle } from './ui';

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
          <span class="font-mono text-meta text-faint">
            {action.lines.first === action.lines.last ? `line ${action.lines.first}` : `lines ${action.lines.first}–${action.lines.last}`}
          </span>
        )}
        {action?.lines && (
          <Button size="sm" icon="runTo" disabled={!canRun} onClick={() => onRunTo(action.id)} title="Test the flow up to this step, and this step too (Ctrl+Shift+Enter)" class="-mr-1.5">
            Run to here
          </Button>
        )}
      </PaneHeader>
      {action ? (
        <div class="min-h-0 flex-1 overflow-y-auto px-4 pt-3.5 pb-6">
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
                action.auto !== undefined && <Link disabled={disabled} onClick={() => onChoose(action, null)}>Use the recorder's choice</Link>
              }
            >
              <Hint>Click one to use it instead; point at one to see what it finds in the app.</Hint>
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
            <Section title="Settings">
              <Hint>The values it recorded are their defaults; each case sets its own in the YAML. Click a name or a value to change it.</Hint>
              <dl class="divide-y divide-line overflow-hidden rounded-lg border border-line">
                {action.settings.map((setting) => (
                  <SettingRow key={setting.role + setting.name} setting={setting} disabled={disabled} onEdit={(change) => onEdit(action, setting, change)} />
                ))}
              </dl>
            </Section>
          )}
          {action.effects.length > 0 && (
            <Section title="What the page did">
              <ul class="space-y-1.5 text-body">
                {action.effects.map((effect) => (
                  <li key={effect.kind + effect.text} class="flex min-w-0 items-baseline gap-2">
                    <span class="w-[92px] shrink-0 text-faint">{effectLabel(effect)}</span>
                    {effect.text && (
                      <span dir={effect.kind === 'urlChanged' ? 'ltr' : 'auto'} class={cls('min-w-0 break-words text-fg', effect.kind === 'urlChanged' && 'font-mono text-meta')}>
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
        <EmptyState icon="pointer" title="No step selected" class="flex-1 pb-8">
          Select a step to see how its element is found, and every other way to find it.
        </EmptyState>
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
          <Link disabled={disabled} onClick={() => onEdit({ reset: true })}>Use it</Link>
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
          class="group mt-1.5 flex w-full min-w-0 items-center gap-2 rounded-md text-left text-meta text-muted transition hover:text-fg disabled:opacity-50"
        >
          <span class={cls('grid size-3.5 shrink-0 place-items-center rounded border transition', setting.unique ? 'border-accent bg-accent text-on-accent' : 'border-line-strong group-hover:border-faint')}>
            {setting.unique && <Icon name="tick" size={10} class="stroke-[3]" />}
          </span>
          <span class="shrink-0 font-medium">Unique per run</span>
          <span dir="auto" class="min-w-0 truncate font-mono text-tag text-faint">
            {setting.text} {stampExample()}
          </span>
        </button>
      )}
      {relative && (
        <Option icon="calendar" tone="accent">
          {fromToday(relative.days)}, as <span class="font-mono">{relative.format}</span>
          <Link disabled={disabled} onClick={() => onEdit({ relative: false })}>Type the date</Link>
        </Option>
      )}
      {suggest && (
        <div class="mt-1.5 flex items-center gap-2 rounded-md border border-dashed border-accent/35 bg-accent/[0.06] px-2 py-1.5 text-meta text-muted">
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
    <div class={cls('mt-1.5 flex min-w-0 items-center gap-1.5 text-meta', tone === 'accent' ? 'text-accent-fg' : 'text-muted')}>
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
          '-mx-1 max-w-[calc(100%+0.5rem)] truncate rounded px-1 font-mono text-meta transition enabled:hover:bg-raised enabled:hover:ring-1 enabled:hover:ring-line-strong',
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
      ref={focusOnMount}
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
        'h-6 w-full min-w-0 rounded border bg-panel px-1.5 font-mono text-meta text-fg outline-none',
        props.align === 'right' && 'text-right',
        ok ? 'border-accent/60' : 'border-bad/70',
      )}
    />
  );
}

interface ElementProps {
  element?: PickedElement;
  /** The step "Use for" changes. */
  step?: Action;
  /** Where a step added goes, e.g. "at the end". */
  where: string;
  disabled: boolean;
  /** What Undo takes back. */
  undo?: string;
  onChoose(candidate: number | null): void;
  onHighlight(candidate: number | null): void;
  onUse(action: Action): void;
  onAdd(kind: 'click' | 'fill' | 'check', value: string): void;
  /** Another check chosen, for its lines to show. */
  onCheckKind(kind: string): void;
  onUndo(): void;
  /** Set to what Enter in the tree does: what the inspector offers first. */
  primary: { current: (() => void) | null };
}

/** The element picked in the Elements view: the steps it can become, each with the lines it writes, and every way to locate it. */
export function ElementInspector({ element, step, where, disabled, onChoose, onHighlight, onUse, onAdd, onCheckKind, onUndo, primary }: ElementProps) {
  const [fill, setFill] = useState<string | null>(null);
  const fillField = useRef<HTMLInputElement>(null);
  useEffect(() => setFill(null), [element?.node]);
  // What a step made of it did, with Undo.
  const setDone = (text: string) => toast({ text, icon: 'tick', tone: 'ok', action: { label: 'Undo', icon: 'undo', run: onUndo } });
  // The field takes the keys as it opens: what is typed next is the text.
  useEffect(() => {
    if (fill !== null) fillField.current?.focus();
  }, [fill !== null]);

  const previews = new Map((element?.previews ?? []).map((preview) => [preview.kind, preview]));
  const checks = element?.checks ?? [];
  const check = checks[element?.check ?? 0];
  const canUse = !!step && previews.has('use') && !disabled;
  const use = () => {
    if (!step || !canUse) return;
    onUse(step);
    setDone(`“${verbOf(step)} ${subjectOf(step)}” now acts on it`);
  };
  const click = () => {
    if (disabled) return;
    onAdd('click', '');
    setDone(`Added a click ${where}`);
  };
  const openFill = () => {
    if (disabled || !element?.fillable) return;
    setFill(fill ?? '');
    fillField.current?.focus();
  };
  const addCheck = () => {
    if (disabled || !check) return;
    onAdd('check', check.kind);
    setDone(`Added a check ${where}: ${check.name.toLowerCase()}`);
  };
  const first = element?.primary === 'use' && canUse ? 'use' : element?.primary === 'fill' && element.fillable ? 'fill' : 'click';
  primary.current = !element ? null : first === 'use' ? use : first === 'fill' ? openFill : click;

  if (!element)
    return (
      <aside aria-label="Inspector" class="area-inspector flex min-h-0 flex-col bg-panel">
        <PaneHeader title="Inspector" />
        <EmptyState icon="elements" title="No element picked" class="flex-1 pb-8">
          Pick one in the tree, or in the app with <Kbd>Ctrl Shift C</Kbd>, to make it a step and see every way to locate it.
        </EmptyState>
      </aside>
    );

  const candidate = element.candidates.find((option) => option.index === element.picked);
  const status = candidate && candidateStatus(candidate);
  const copy = async () => {
    if (candidate && (await copyText(candidate.code))) toast({ text: 'Copied the locator', icon: 'copy' });
  };
  return (
    <aside aria-label="Inspector" class="area-inspector flex min-h-0 flex-col bg-panel">
      <PaneHeader title="Inspector">
        <span class="font-mono text-meta text-faint">{element.page}</span>
      </PaneHeader>
      <div class="min-h-0 flex-1 overflow-y-auto px-4 pt-3.5 pb-6">
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
        <section aria-label="Make it a step" class="mt-5 space-y-1.5">
          <SectionTitle
            aside={
              status && (
                <span
                  title={candidate ? `${candidateKind(candidate)}: what the flow finds it by` : undefined}
                  class={cls('inline-flex items-center gap-1 rounded-full px-2 text-meta leading-5 font-medium tabular-nums', status.ok ? 'bg-ok/12 text-ok' : 'bg-bad/12 text-bad')}
                >
                  <Icon name={status.ok ? 'tick' : 'cross'} size={10} class="stroke-[2.6]" />
                  its locator: {status.text}
                </span>
              )
            }
          >
            Make it a step
          </SectionTitle>
          <StepChoice
            icon="target"
            label={step ? `Use for “${verbOf(step)} ${subjectOf(step)}”` : 'Use for the selected step'}
            hint={step ? (canUse ? '' : 'it acts on no element') : 'select one on the left'}
            title={step && canUse ? `“${verbOf(step)} ${subjectOf(step)}” acts on this element instead` : undefined}
            primary={first === 'use'}
            disabled={!canUse}
            code={previews.get('use')?.code}
            was={previews.get('use')?.was}
            onClick={use}
          />
          <StepChoice icon="click" label="Add click" hint={where} primary={first === 'click'} disabled={disabled} code={previews.get('click')?.code} onClick={click} />
          {element.fillable && (
            <StepChoice icon="fill" label="Add fill…" hint={where} primary={first === 'fill'} disabled={disabled} pressed={fill !== null} code={previews.get('fill')?.code} onClick={openFill}>
              {fill !== null && (
                <form
                  class="mt-2 flex gap-1.5"
                  onClick={(event) => event.stopPropagation()}
                  onSubmit={(event) => {
                    event.preventDefault();
                    onAdd('fill', fill);
                    setFill(null);
                    setDone(`Added a fill ${where}`);
                  }}
                >
                  <input
                    ref={fillField}
                    dir="auto"
                    value={fill}
                    aria-label="The text to type"
                    placeholder="The text to type, then Enter"
                    onInput={(event) => setFill(event.currentTarget.value)}
                    onKeyDown={(event) => {
                      if (event.key !== 'Escape') return;
                      event.stopPropagation();
                      setFill(null);
                    }}
                    class="h-8 min-w-0 flex-1 rounded-lg border border-line-strong bg-panel px-2.5 text-body text-fg outline-none placeholder:text-faint focus:border-accent/60"
                  />
                  <ActionButton primary submit>
                    Add
                  </ActionButton>
                </form>
              )}
            </StepChoice>
          )}
          {check && (
            <StepChoice icon="checkCircle" label="Add check" hint={where} disabled={disabled} code={previews.get('check')?.code} onClick={addCheck}>
              <select
                aria-label="What to check"
                value={check.kind}
                onClick={(event) => event.stopPropagation()}
                onKeyDown={(event) => event.stopPropagation()}
                onChange={(event) => onCheckKind(event.currentTarget.value)}
                class="mt-2 h-7 w-full max-w-full rounded-md border border-line-strong bg-panel px-1.5 text-meta text-fg outline-none focus:border-accent/60"
              >
                {checks.map((each) => (
                  <option key={each.kind} value={each.kind}>
                    {each.name}
                    {each.value !== undefined && each.value !== '' ? ` “${each.value.length > 40 ? `${each.value.slice(0, 39)}…` : each.value}”` : ''}
                  </option>
                ))}
              </select>
            </StepChoice>
          )}
          <div class="flex min-h-7 items-center gap-2 pt-1 text-meta">
            <span class="flex min-w-0 items-center gap-1 text-faint">
              <Kbd>↵</Kbd> in the tree makes the one marked; <Kbd>Ctrl Z</Kbd> takes it back
            </span>
            <Button size="sm" icon="copy" disabled={!candidate} onClick={copy} title="Copy the locator's C#" class="ml-auto">
              Copy
            </Button>
          </div>
        </section>
        {element.candidates.length > 0 ? (
          <Section
            title="Locator"
            aside={element.chosen !== undefined && <Link disabled={disabled} onClick={() => onChoose(null)}>Use the recorder's choice</Link>}
          >
            <Hint>The recorder's choice comes first. Click another to use it.</Hint>
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

// A step the element can become: what it does, the lines it writes, and for the first, the key that makes it.
function StepChoice(props: {
  icon: IconName;
  label: string;
  hint: string;
  title?: string;
  primary?: boolean;
  pressed?: boolean;
  disabled?: boolean;
  code?: string;
  was?: string;
  onClick(): void;
  children?: ComponentChildren;
}) {
  return (
    <div
      role="button"
      tabIndex={props.disabled ? -1 : 0}
      aria-disabled={props.disabled}
      aria-pressed={props.pressed}
      title={props.title}
      onClick={() => !props.disabled && props.onClick()}
      onKeyDown={(event) => {
        if (event.target !== event.currentTarget || (event.key !== 'Enter' && event.key !== ' ')) return;
        event.preventDefault();
        if (!props.disabled) props.onClick();
      }}
      class={cls(
        'block w-full rounded-lg border px-3 py-2 text-left transition',
        props.disabled ? 'cursor-not-allowed border-line opacity-50' : 'cursor-pointer hover:bg-raised',
        !props.disabled && (props.primary ? 'border-accent/55 bg-accent/7 shadow-[0_0_0_3px_color-mix(in_oklab,var(--accent)_9%,transparent)]' : 'border-line bg-canvas hover:border-line-strong'),
      )}
    >
      <span class="flex min-w-0 items-center gap-2">
        <Icon name={props.icon} size={14} class={cls('shrink-0', props.primary ? 'text-accent-fg' : 'text-muted')} />
        <span dir="auto" class="min-w-0 truncate text-body font-medium text-fg">{props.label}</span>
        {props.hint && <span class="min-w-0 shrink-0 truncate text-meta text-faint">{props.hint}</span>}
        {props.primary && !props.disabled && <kbd class="ml-auto shrink-0 rounded border border-accent/40 px-1 font-sans text-tag leading-4 text-accent-fg">↵</kbd>}
      </span>
      {props.was && <CodeLines code={props.was} gone />}
      {props.code && <CodeLines code={props.code} />}
      {props.children}
    </div>
  );
}

// The lines a step writes (+) or takes out (−), the first few of them.
const SHOWN_LINES = 4;

function CodeLines({ code, gone }: { code: string; gone?: boolean }) {
  const lines = tokenizeCSharp(code);
  const more = lines.length - SHOWN_LINES;
  return (
    <code dir="ltr" class={cls('mt-1.5 block font-mono text-meta break-words whitespace-pre-wrap', gone && 'line-through decoration-bad/60 opacity-60')}>
      {lines.slice(0, more > 1 ? SHOWN_LINES : lines.length).map((tokens, at) => (
        <span key={at} class="block">
          <span class={cls('mr-1.5 select-none', gone ? 'text-bad' : 'text-ok')}>{gone ? '−' : '+'}</span>
          {tokens.map((token, index) => (
            <span key={index} class={`tk-${token.kind}`}>
              {token.text}
            </span>
          ))}
        </span>
      ))}
      {more > 1 && <span class="block font-sans text-meta text-faint no-underline">… {more} more lines</span>}
    </code>
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
              <span class="min-w-0 truncate text-body font-medium text-fg">{candidateKind(candidate)}</span>
              {candidate.index === auto && <Tag title="The recorder's own choice">Auto</Tag>}
              {isPicked && chosen !== undefined && <Tag accent title="Your choice">Chosen</Tag>}
              <span
                class={cls(
                  'ml-auto inline-flex shrink-0 items-center gap-1 rounded-full px-2 py-px text-meta font-medium tabular-nums',
                  status.ok ? 'bg-ok/12 text-ok' : 'bg-bad/12 text-bad',
                )}
              >
                <Icon name={status.ok ? 'tick' : 'cross'} size={10} class="stroke-[2.6]" />
                {status.text}
              </span>
            </span>
            <code dir="ltr" class="mt-2 block font-mono text-meta break-words text-muted">
              {candidate.code}
            </code>
            {candidate.problem && (
              <span class="mt-1.5 flex gap-1.5 text-meta text-warn">
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
      <div class="text-meta font-medium text-muted">{kind}</div>
      <div dir="auto" class="mt-0.5 text-head font-semibold tracking-[-0.01em] break-words text-fg">
        {title}
      </div>
      {children && <div class="mt-2 flex flex-wrap items-center gap-1.5 text-meta text-muted">{children}</div>}
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
    <span class={cls('inline-flex items-center gap-1 rounded-md border px-1.5 py-px text-meta font-medium', tone)}>
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
        mono && 'font-mono text-meta',
      )}
    >
      {children}
    </span>
  );
}

function Checks({ checks }: { checks: string[] }) {
  return (
    <div class="mt-3.5 space-y-1 rounded-lg border border-warn/30 bg-warn/8 p-2.5 text-meta text-warn">
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
    <div class={cls('mt-3.5 flex gap-2 rounded-lg border p-2.5 text-meta', tone === 'info' ? 'border-info/30 bg-info/8 text-info' : 'border-warn/30 bg-warn/8 text-warn')}>
      <Icon name={icon} size={13} class="mt-px shrink-0" />
      <span>{children}</span>
    </div>
  );
}

function Section({ title, aside, children }: { title: string; aside?: ComponentChildren; children: ComponentChildren }) {
  return (
    <section class="mt-6">
      <SectionTitle aside={aside}>{title}</SectionTitle>
      {children}
    </section>
  );
}

function Hint({ children }: { children: ComponentChildren }) {
  return <p class="mb-2.5 text-meta text-faint">{children}</p>;
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
        'inline-flex h-8 items-center gap-1.5 rounded-lg px-2.5 text-body font-medium transition disabled:pointer-events-none disabled:opacity-45',
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
      class={cls('shrink-0 rounded px-1.5 text-tag leading-4 font-medium', accent ? 'bg-accent/15 text-accent-fg' : 'border border-line text-muted')}
    >
      {children}
    </span>
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
