import type { ComponentChildren } from 'preact';
import { useEffect, useRef, useState } from 'preact/hooks';
import type { Theme } from '../theme';
import { cls } from '../text';
import type { Status } from '../types';
import { Icon, type IconName } from './icons';

export type View = 'code' | 'yaml' | 'elements';

interface Props {
  flow: string;
  /** Changes with each state, so the field shows the name the recorder took. */
  stamp: object;
  status: Status;
  canSave: boolean;
  canUndo: boolean;
  view: View;
  theme: Theme;
  onRename(name: string): void;
  onUndo(): void;
  onSave(): void;
  onPause(): void;
  onView(view: View): void;
  onTheme(): void;
}

export function TopBar(props: Props) {
  const saving = props.status === 'saving';
  const paused = props.status === 'paused';
  return (
    <header class="flex h-12 shrink-0 items-center gap-2 border-b border-line bg-panel pr-2.5 pl-3">
      <Status status={props.status} />
      <span class="mx-1 h-5 w-px shrink-0 bg-line" />
      <FlowName flow={props.flow} stamp={props.stamp} onRename={props.onRename} />
      <div class="ml-auto flex shrink-0 items-center gap-1">
        <Segmented
          label="Show"
          value={props.view}
          options={[
            ['code', 'Code', 'file'],
            ['yaml', 'YAML', 'list'],
            ['elements', 'Elements', 'elements'],
          ]}
          onChange={props.onView}
        />
        <span class="mx-1.5 h-5 w-px bg-line" />
        <Button
          onClick={props.onPause}
          disabled={saving || props.status === 'login'}
          title={paused ? 'Resume recording (F8 in the app)' : 'Pause recording: what you do in the app is not recorded (F8 in the app)'}
          active={paused}
        >
          <Icon name={paused ? 'play' : 'pause'} size={15} />
          <span class="max-[820px]:hidden">{paused ? 'Resume' : 'Pause'}</span>
        </Button>
        <Button onClick={props.onUndo} disabled={!props.canUndo || saving} title="Remove the last step from the flow (Ctrl+Z). The app stays as it is.">
          <Icon name="undo" size={15} />
          <span class="max-[820px]:hidden">Undo</span>
        </Button>
        <Button onClick={props.onTheme} title={props.theme === 'dark' ? 'Light theme' : 'Dark theme'} square>
          <Icon name={props.theme === 'dark' ? 'sun' : 'moon'} size={15} />
        </Button>
        <button
          type="button"
          onClick={props.onSave}
          disabled={saving || !props.canSave}
          title="End the recording and save the flow (Ctrl+S)"
          class="ml-1 inline-flex h-8 items-center gap-2 rounded-lg bg-accent px-3 text-[12.5px] font-semibold text-on-accent shadow-[0_1px_0_0_rgb(255_255_255/0.12)_inset,0_1px_2px_rgb(0_0_0/0.2)] transition hover:brightness-110 active:brightness-95 disabled:opacity-50 disabled:hover:brightness-100"
        >
          {saving && <span class="size-3 animate-spin-slow rounded-full border-[1.5px] border-on-accent border-t-transparent" />}
          <span>{saving ? 'Saving' : 'Save'}</span>
          {!saving && <kbd class="rounded bg-white/18 px-1.5 font-sans text-[10.5px] leading-[18px] font-medium text-on-accent/90 max-[820px]:hidden">Ctrl S</kbd>}
        </button>
      </div>
    </header>
  );
}

const statuses: Record<Status, { text: string; dot: string }> = {
  recording: { text: 'Recording', dot: 'bg-rec animate-rec' },
  paused: { text: 'Paused', dot: 'bg-warn' },
  login: { text: 'Logging in', dot: 'bg-info animate-pulse-soft' },
  saving: { text: 'Saving', dot: 'bg-accent' },
};

function Status({ status }: { status: Status }) {
  const { text, dot } = statuses[status];
  return (
    <span role="status" class="flex shrink-0 items-center gap-2 rounded-full py-1 pr-2.5 pl-2 text-[12px] font-medium text-muted">
      <span class={cls('size-2 rounded-full', dot, status === 'paused' && 'rounded-[2px]')} />
      <span class="max-[560px]:sr-only">{text}</span>
    </span>
  );
}

// The flow's class name, edited in place; Enter or leaving the field renames it, Escape keeps the name.
function FlowName({ flow, stamp, onRename }: { flow: string; stamp: object; onRename(name: string): void }) {
  const [draft, setDraft] = useState(flow);
  const input = useRef<HTMLInputElement>(null);
  useEffect(() => {
    if (document.activeElement !== input.current) setDraft(flow);
  }, [flow, stamp]);

  const commit = () => {
    const name = draft.trim();
    if (name && name !== flow) onRename(name);
    else setDraft(flow);
  };

  return (
    <label
      title="Rename the flow"
      class="group flex min-w-0 items-center rounded-md border border-transparent px-1.5 py-1 transition focus-within:border-line-strong focus-within:bg-raised hover:bg-raised"
    >
      <span class="sr-only">Flow name</span>
      <input
        ref={input}
        value={draft}
        spellcheck={false}
        onInput={(event) => setDraft(event.currentTarget.value)}
        onBlur={commit}
        onKeyDown={(event) => {
          if (event.key === 'Enter') event.currentTarget.blur();
          if (event.key === 'Escape') {
            setDraft(flow);
            requestAnimationFrame(() => input.current?.blur());
          }
        }}
        class="min-w-[4ch] max-w-[30ch] truncate bg-transparent text-[14px] font-semibold tracking-[-0.01em] text-fg outline-none [field-sizing:content] focus-visible:outline-none"
      />
      <span class="pl-px text-[14px] text-faint">.cs</span>
      <Icon name="pencil" size={13} class="ml-2 shrink-0 text-faint opacity-0 transition group-hover:opacity-100 group-focus-within:opacity-0" />
    </label>
  );
}

function Segmented<T extends string>(props: { label: string; value: T; options: [T, string, IconName][]; onChange(value: T): void }) {
  return (
    <div role="radiogroup" aria-label={props.label} class="flex h-8 items-center gap-px rounded-lg border border-line bg-canvas p-0.5">
      {props.options.map(([value, text, icon]) => (
        <button
          type="button"
          role="radio"
          aria-checked={props.value === value}
          title={text}
          onClick={() => props.onChange(value)}
          class={cls(
            'inline-flex h-full items-center gap-1.5 rounded-md px-2.5 text-[12px] font-medium transition',
            props.value === value ? 'bg-raised text-fg shadow-[0_0_0_1px_var(--line-strong),0_1px_2px_rgb(0_0_0/0.12)]' : 'text-muted hover:text-fg',
          )}
        >
          <Icon name={icon} size={13} class={cls('min-[1000px]:hidden', props.value === value ? 'text-fg' : 'text-faint')} />
          <span class="max-[999px]:sr-only">{text}</span>
        </button>
      ))}
    </div>
  );
}

function Button(props: { onClick(): void; disabled?: boolean; title: string; square?: boolean; active?: boolean; children: ComponentChildren }) {
  return (
    <button
      type="button"
      onClick={props.onClick}
      disabled={props.disabled}
      title={props.title}
      aria-label={props.title}
      aria-pressed={props.active}
      class={cls(
        'inline-flex h-8 shrink-0 items-center justify-center gap-1.5 rounded-lg text-[12.5px] font-medium transition disabled:pointer-events-none disabled:opacity-40',
        props.active ? 'bg-warn/14 text-warn hover:bg-warn/20' : 'text-muted hover:bg-raised hover:text-fg',
        props.square ? 'w-8' : 'px-2.5 max-[820px]:w-8 max-[820px]:px-0',
      )}
    >
      {props.children}
    </button>
  );
}
