import type { ComponentChildren } from 'preact';
import { useEffect, useRef, useState } from 'preact/hooks';
import type { Theme } from '../theme';
import { cls } from '../text';
import { Icon } from './icons';

export type View = 'code' | 'yaml';

interface Props {
  flow: string;
  /** Changes with each state, so the field shows the name the recorder took. */
  stamp: object;
  saving: boolean;
  canSave: boolean;
  canUndo: boolean;
  view: View;
  theme: Theme;
  onRename(name: string): void;
  onUndo(): void;
  onSave(): void;
  onView(view: View): void;
  onTheme(): void;
}

export function TopBar(props: Props) {
  return (
    <header class="flex h-12 shrink-0 items-center gap-3 border-b border-line bg-panel pr-2.5 pl-4">
      <Status saving={props.saving} />
      <span class="h-5 w-px bg-line" />
      <FlowName flow={props.flow} stamp={props.stamp} onRename={props.onRename} />
      <div class="ml-auto flex items-center gap-1.5">
        <Segmented
          label="Show"
          value={props.view}
          options={[
            ['code', 'Code'],
            ['yaml', 'YAML'],
          ]}
          onChange={props.onView}
        />
        <span class="mx-1 h-5 w-px bg-line" />
        <Button onClick={props.onUndo} disabled={!props.canUndo || props.saving} title="Remove the last step from the flow (Ctrl+Z). The app stays as it is.">
          <Icon name="undo" size={15} />
          <span>Undo</span>
        </Button>
        <Button onClick={props.onTheme} title={props.theme === 'dark' ? 'Light theme' : 'Dark theme'} square>
          <Icon name={props.theme === 'dark' ? 'sun' : 'moon'} size={15} />
        </Button>
        <button
          type="button"
          onClick={props.onSave}
          disabled={props.saving || !props.canSave}
          title="End the recording and save the flow (Ctrl+S)"
          class="ml-1 inline-flex h-8 items-center gap-2 rounded-lg bg-accent pr-2 pl-3 font-medium text-on-accent shadow-sm transition hover:brightness-110 active:brightness-95 disabled:opacity-60"
        >
          <span>{props.saving ? 'Saving…' : 'Save'}</span>
          <kbd class="rounded bg-white/18 px-1.5 py-px font-sans text-[10.5px] font-medium tracking-wide text-on-accent/90">Ctrl S</kbd>
        </button>
      </div>
    </header>
  );
}

function Status({ saving }: { saving: boolean }) {
  return saving ? (
    <span class="flex items-center gap-2 text-[12px] font-medium text-muted">
      <span class="size-3 animate-spin-slow rounded-full border-[1.5px] border-accent border-t-transparent" />
      Saving
    </span>
  ) : (
    <span class="flex items-center gap-2 text-[12px] font-medium text-muted" role="status">
      <span class="size-2 animate-rec rounded-full bg-rec" />
      Recording
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
    <label class="group flex min-w-0 items-center rounded-md border border-transparent px-1.5 py-1 transition focus-within:border-line-strong focus-within:bg-raised hover:bg-raised">
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
        class="min-w-[4ch] max-w-[34ch] truncate bg-transparent text-[14px] font-semibold tracking-[-0.01em] text-fg outline-none [field-sizing:content]"
      />
      <span class="pl-px text-[14px] text-faint">.cs</span>
      <Icon name="pencil" size={13} class="ml-2 text-faint opacity-0 transition group-hover:opacity-100 group-focus-within:opacity-0" />
    </label>
  );
}

function Segmented<T extends string>(props: { label: string; value: T; options: [T, string][]; onChange(value: T): void }) {
  return (
    <div role="radiogroup" aria-label={props.label} class="flex h-8 items-center rounded-lg border border-line bg-canvas p-0.5">
      {props.options.map(([value, text]) => (
        <button
          type="button"
          role="radio"
          aria-checked={props.value === value}
          onClick={() => props.onChange(value)}
          class={cls(
            'h-full rounded-md px-2.5 text-[12px] font-medium transition',
            props.value === value ? 'bg-raised text-fg shadow-[0_0_0_1px_var(--line-strong)]' : 'text-muted hover:text-fg',
          )}
        >
          {text}
        </button>
      ))}
    </div>
  );
}

function Button(props: { onClick(): void; disabled?: boolean; title: string; square?: boolean; children: ComponentChildren }) {
  return (
    <button
      type="button"
      onClick={props.onClick}
      disabled={props.disabled}
      title={props.title}
      aria-label={props.square ? props.title : undefined}
      class={cls(
        'inline-flex h-8 items-center gap-1.5 rounded-lg text-[12.5px] font-medium text-muted transition hover:bg-raised hover:text-fg disabled:pointer-events-none disabled:opacity-40',
        props.square ? 'w-8 justify-center' : 'px-2.5',
      )}
    >
      {props.children}
    </button>
  );
}
