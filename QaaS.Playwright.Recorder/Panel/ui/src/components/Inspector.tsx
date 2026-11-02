import type { ComponentChildren } from 'preact';
import { candidateKind, candidateStatus, cls, effectLabel, isUnusable, subjectOf, verbOf } from '../text';
import type { Action, Candidate } from '../types';
import { Icon } from './icons';
import { PaneHeader } from './PaneHeader';

interface Props {
  action?: Action;
  disabled: boolean;
  onChoose(action: Action, candidate: number | null): void;
  onHighlight(action: string | null, candidate?: number): void;
}

/** The selected action: how its element is found, every other way to find it, and the values it records. */
export function Inspector({ action, disabled, onChoose, onHighlight }: Props) {
  return (
    <aside aria-label="Inspector" class="area-inspector flex min-h-0 flex-col bg-panel">
      <PaneHeader title="Inspector">
        {action?.lines && (
          <span class="font-mono text-[11px] text-faint">
            {action.lines.first === action.lines.last ? `line ${action.lines.first}` : `lines ${action.lines.first}–${action.lines.last}`}
          </span>
        )}
      </PaneHeader>
      {action ? (
        <div class="min-h-0 flex-1 overflow-y-auto px-4 pt-3.5 pb-6">
          <Heading action={action} />
          {action.checks.length > 0 && (
            <div class="mt-3.5 space-y-1 rounded-lg border border-warn/30 bg-warn/8 p-2.5 text-[12px] leading-snug text-warn">
              {action.checks.map((check) => (
                <div key={check} class="flex gap-1.5">
                  <Icon name="warn" size={13} class="mt-px shrink-0" />
                  <span>{check}</span>
                </div>
              ))}
            </div>
          )}
          {action.candidates.length > 0 && (
            <Section
              title="Locator"
              aside={
                action.chosen !== undefined &&
                action.auto !== undefined && (
                  <button
                    type="button"
                    disabled={disabled}
                    onClick={() => onChoose(action, null)}
                    class="rounded px-1 text-[11.5px] font-medium text-accent-fg normal-case transition hover:underline"
                  >
                    Use the recorder's choice
                  </button>
                )
              }
            >
              <p class="mb-2.5 text-[12px] leading-relaxed text-muted">
                Every way the recorder found to locate the element. Click one to use it; point at one to see what it finds in the app.
              </p>
              <div role="radiogroup" aria-label="Locator" class="space-y-1.5" onMouseLeave={() => onHighlight(null)}>
                {action.candidates.map((candidate) => (
                  <Alternative
                    key={candidate.index}
                    candidate={candidate}
                    action={action}
                    disabled={disabled}
                    onChoose={onChoose}
                    onHighlight={onHighlight}
                  />
                ))}
              </div>
            </Section>
          )}
          {action.settings.length > 0 && (
            <Section title="Recorded values">
              <p class="mb-2 text-[12px] leading-relaxed text-muted">Settings of the flow, with these as defaults; change them per case in the YAML.</p>
              <dl class="divide-y divide-line overflow-hidden rounded-lg border border-line">
                {action.settings.map((setting) => (
                  <div key={setting.name} class="flex items-baseline gap-3 bg-canvas px-2.5 py-1.5">
                    <dt class="tk-key shrink-0 font-mono text-[11.5px]">{setting.name}</dt>
                    <dd dir="auto" class="tk-str min-w-0 flex-1 truncate text-right font-mono text-[11.5px]" title={setting.value}>
                      {setting.value}
                    </dd>
                  </div>
                ))}
              </dl>
            </Section>
          )}
          {action.effects.length > 0 && (
            <Section title="What it did">
              <ul class="space-y-1 text-[12.5px]">
                {action.effects.map((effect) => (
                  <li key={effect.kind + effect.text} class="flex min-w-0 items-baseline gap-2">
                    <span class="shrink-0 text-muted">{effectLabel(effect)}</span>
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
        <div class="flex flex-1 items-center justify-center px-8 text-center text-[12.5px] text-faint">
          Select a step to see how its element is found.
        </div>
      )}
    </aside>
  );
}

function Heading({ action }: { action: Action }) {
  const value = action.kind === 'navigate' || action.kind === 'newTab' ? undefined : action.value;
  return (
    <div>
      <div class="text-[11px] font-medium tracking-[0.06em] text-faint uppercase">{verbOf(action)}</div>
      <div dir="auto" class="mt-1 text-[15px] leading-snug font-semibold tracking-[-0.01em] break-words text-fg">
        {subjectOf(action)}
      </div>
      {(value || action.dialog || action.page !== 'page') && (
        <div class="mt-1.5 flex flex-wrap items-center gap-1.5 text-[12px] text-muted">
          {value && (
            <span dir="auto" class="max-w-full truncate rounded-md border border-line bg-canvas px-1.5 py-px text-fg/85">
              {value}
            </span>
          )}
          {action.dialog && (
            <span class="inline-flex max-w-full min-w-0 items-center gap-1">
              <Icon name="dialog" size={12} class="shrink-0 text-faint" />
              <span class="shrink-0">in</span>
              <span dir="auto" class="truncate text-fg/85">
                {action.dialog}
              </span>
            </span>
          )}
          {action.page !== 'page' && <span>in {action.page}</span>}
        </div>
      )}
    </div>
  );
}

function Section({ title, aside, children }: { title: string; aside?: ComponentChildren; children: ComponentChildren }) {
  return (
    <section class="mt-5">
      <div class="mb-1.5 flex items-center text-[11px] font-medium tracking-[0.06em] text-faint uppercase">
        {title}
        <span class="ml-auto tracking-normal">{aside}</span>
      </div>
      {children}
    </section>
  );
}

function Alternative({ candidate, action, disabled, onChoose, onHighlight }: { candidate: Candidate; action: Action } & Omit<Props, 'action'>) {
  const picked = candidate.index === action.picked;
  const unusable = isUnusable(candidate);
  const status = candidateStatus(candidate);
  const choose = () => {
    if (picked || unusable) return;
    // Back to the recorder's own choice, rather than pinning it.
    onChoose(action, candidate.index === action.auto ? null : candidate.index);
  };
  return (
    <button
      type="button"
      role="radio"
      aria-checked={picked}
      aria-disabled={unusable || disabled}
      title={unusable ? `The flow cannot use it: ${candidate.rejection}` : undefined}
      onClick={disabled ? undefined : choose}
      onMouseEnter={() => onHighlight(action.id, candidate.index)}
      class={cls(
        'block w-full rounded-lg border px-2.5 py-2 text-left transition',
        picked ? 'border-accent/55 bg-accent/10' : 'border-line hover:border-line-strong hover:bg-raised',
        unusable ? 'cursor-not-allowed opacity-55' : !picked && 'cursor-pointer',
      )}
    >
      <span class="flex min-w-0 items-center gap-2">
        <span class={cls('grid size-3.5 shrink-0 place-items-center rounded-full border transition', picked ? 'border-accent bg-accent' : 'border-line-strong')}>
          {picked && <span class="size-1.5 rounded-full bg-on-accent" />}
        </span>
        <span class="min-w-0 truncate text-[12.5px] font-medium text-fg">{candidateKind(candidate)}</span>
        {candidate.index === action.auto && <Tag title="The recorder's own choice">Auto</Tag>}
        {picked && action.chosen !== undefined && <Tag accent title="Your choice">Chosen</Tag>}
        <span
          class={cls(
            'ml-auto inline-flex shrink-0 items-center gap-1 rounded-full px-2 py-px text-[11px] font-medium',
            status.ok ? 'bg-ok/12 text-ok' : 'bg-bad/12 text-bad',
          )}
        >
          <Icon name={status.ok ? 'tick' : 'cross'} size={10} class="stroke-[2.6]" />
          {status.text}
        </span>
      </span>
      <code dir="ltr" class="mt-1.5 block font-mono text-[11.5px] leading-[1.55] [overflow-wrap:anywhere] text-muted">
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
}

function Tag({ accent, title, children }: { accent?: boolean; title: string; children: ComponentChildren }) {
  return (
    <span
      title={title}
      class={cls(
        'shrink-0 rounded px-1.5 text-[10.5px] leading-4 font-medium',
        accent ? 'bg-accent/15 text-accent-fg' : 'border border-line text-muted',
      )}
    >
      {children}
    </span>
  );
}
