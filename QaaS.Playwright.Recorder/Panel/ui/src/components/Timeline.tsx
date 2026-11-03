import { useEffect, useRef } from 'preact/hooks';
import { cls, detailOf, effectLabel, subjectOf, verbOf } from '../text';
import type { Action, Effect, Status, Step } from '../types';
import { Icon, type IconName, kindIcon } from './icons';
import { PaneHeader } from './PaneHeader';

interface Props {
  steps: Step[];
  count: number;
  notes: string[];
  selected: string | null;
  status: Status;
  onSelect(id: string): void;
  onHover(id: string | null): void;
}

/** The steps of the flow, each a method, with a card per action. */
export function Timeline({ steps, count, notes, selected, status, onSelect, onHover }: Props) {
  const list = useRef<HTMLDivElement>(null);
  useEffect(() => {
    list.current?.querySelector('[aria-current="true"]')?.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
  }, [selected, count]);

  return (
    <section aria-label="Steps" class="area-steps flex min-h-0 flex-col border-r border-line bg-panel">
      <PaneHeader title="Steps">{count > 0 && <span class="tabular-nums">{count}</span>}</PaneHeader>
      <div ref={list} class="min-h-0 flex-1 overflow-y-auto px-2 pb-4" onMouseLeave={() => onHover(null)}>
        {steps.length === 0 ? (
          <Empty status={status} />
        ) : (
          steps.map((step) => (
            <div key={step.name} class="pt-3">
              <div class="flex min-w-0 items-center gap-2 px-2 pb-1.5 text-[11px] text-faint">
                <span class="truncate font-mono">{step.name}</span>
                {step.dialog && (
                  <span dir="auto" class="min-w-0 truncate rounded border border-line px-1.5 leading-4">
                    {step.dialog}
                  </span>
                )}
              </div>
              <ol class="relative space-y-1 before:absolute before:top-4 before:bottom-4 before:left-[22px] before:w-px before:bg-line-strong">
                {step.actions.map((action) => (
                  <Card key={action.id} action={action} selected={action.id === selected} onSelect={onSelect} onHover={onHover} />
                ))}
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

function Card({ action, selected, onSelect, onHover }: { action: Action; selected: boolean } & Pick<Props, 'onSelect' | 'onHover'>) {
  const warn = action.checks.length > 0;
  const detail = detailOf(action);
  return (
    <li class="animate-step-in">
      <button
        type="button"
        aria-current={selected}
        onClick={() => onSelect(action.id)}
        onMouseEnter={() => onHover(action.id)}
        class={cls(
          'relative block w-full rounded-lg border py-2 pr-2.5 pl-11 text-left transition',
          selected ? 'border-accent/45 bg-accent/10' : 'border-transparent hover:bg-raised',
        )}
      >
        <span
          class={cls(
            'absolute top-[7px] left-[10px] grid size-6 place-items-center rounded-md border bg-panel transition',
            warn ? 'border-warn/50 text-warn' : selected ? 'border-accent/60 text-accent-fg' : 'border-line-strong text-muted',
          )}
        >
          <Icon name={kindIcon(action.kind)} size={13} />
        </span>
        <span class="flex min-w-0 items-baseline gap-1.5">
          <span class="shrink-0 text-[12px] text-muted">{verbOf(action)}</span>
          <span dir="auto" class="min-w-0 truncate font-medium text-fg">
            {subjectOf(action)}
          </span>
          {action.added && (
            <span title="Added in the panel's Elements view" class="ml-auto shrink-0 rounded border border-accent/30 bg-accent/10 px-1 text-[10px] leading-4 font-medium text-accent-fg">
              added
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
    </li>
  );
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
